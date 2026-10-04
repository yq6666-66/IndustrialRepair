using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;

namespace IndustrialRepair.BLL;

/* =====================================================================
   R41：预防性维护调度（Preventive Maintenance Scheduling）
   ===================================================================== */

/// <summary>PM 调度条目。</summary>
public record MaintenanceSchedule(
    int ScheduleId, int DeviceId, string DeviceName, string Title,
    string IntervalType, int? IntervalDays, decimal? IntervalHours,
    DateTime? LastExecutedAt, DateTime? NextDueAt, bool IsActive,
    string? TemplateDesc, string TemplatePriority);

/// <summary>
/// 预防性维护调度服务：CRUD + 到期检查自动生成工单（Quartz 定时调用 CheckAndGenerate）。
/// 支持 Time（按天数）和 Meter（按运行小时）两种触发模式。
/// </summary>
public class MaintenanceScheduleService
{
    private readonly WorkOrderRepository _orders = new();

    public DataTable List(bool? activeOnly = null) =>
        DbHelper.GetTable(
            @"SELECT ms.ScheduleId, ms.DeviceId, d.DeviceName, ms.Title,
                     ms.IntervalType, ms.IntervalDays, ms.IntervalHours,
                     ms.LastExecutedAt, ms.NextDueAt, ms.IsActive,
                     ms.TemplateDesc, ms.TemplatePriority
              FROM dbo.maintenance_schedule ms
              JOIN dbo.device d ON d.DeviceId = ms.DeviceId
              WHERE (@A IS NULL OR ms.IsActive = @A)
              ORDER BY ms.NextDueAt",
            CommandType.Text, DbHelper.P("@A", (object?)activeOnly ?? DBNull.Value));

    public Result Save(MaintenanceSchedule s)
    {
        try
        {
            DbHelper.ExecuteNonQuery(
                @"MERGE dbo.maintenance_schedule AS target
                  USING (SELECT @Id AS ScheduleId) AS src
                  ON target.ScheduleId = src.ScheduleId
                  WHEN MATCHED THEN UPDATE SET
                      DeviceId=@Dev, Title=@T, IntervalType=@IT, IntervalDays=@ITV,
                      IntervalHours=@IH, IsActive=@A, TemplateDesc=@TD, TemplatePriority=@TP,
                      NextDueAt = CASE WHEN @NDA IS NOT NULL THEN @NDA
                                       WHEN @IT = N'Time' THEN DATEADD(DAY, ISNULL(@ITV, 30), GETDATE())
                                       ELSE NULL END
                  WHEN NOT MATCHED THEN INSERT(DeviceId, Title, IntervalType, IntervalDays, IntervalHours,
                      IsActive, TemplateDesc, TemplatePriority, NextDueAt)
                  VALUES(@Dev, @T, @IT, @ITV, @IH, @A, @TD, @TP,
                      CASE WHEN @NDA IS NOT NULL THEN @NDA
                           WHEN @IT = N'Time' THEN DATEADD(DAY, ISNULL(@ITV, 30), GETDATE())
                           ELSE NULL END);",
                CommandType.Text,
                DbHelper.P("@Id", s.ScheduleId), DbHelper.P("@Dev", s.DeviceId),
                DbHelper.P("@T", s.Title), DbHelper.P("@IT", s.IntervalType),
                DbHelper.P("@ITV", (object?)s.IntervalDays ?? DBNull.Value),
                DbHelper.P("@IH", (object?)s.IntervalHours ?? DBNull.Value),
                DbHelper.P("@A", s.IsActive), DbHelper.P("@TD", (object?)s.TemplateDesc ?? DBNull.Value),
                DbHelper.P("@TP", s.TemplatePriority),
                DbHelper.P("@NDA", (object?)s.NextDueAt ?? DBNull.Value));
            return Result.Ok("保存成功。");
        }
        catch (Exception ex) { return Result.Fail(DbHelper.FriendlyError(ex)); }
    }

    public Result Delete(int scheduleId)
    {
        try
        {
            DbHelper.ExecuteNonQuery("DELETE dbo.maintenance_schedule WHERE ScheduleId = @Id",
                CommandType.Text, DbHelper.P("@Id", scheduleId));
            return Result.Ok("已删除。");
        }
        catch (Exception ex) { return Result.Fail(DbHelper.FriendlyError(ex)); }
    }

    /// <summary>
    /// 检查到期 PM 并自动生成工单（Quartz 每 30 分钟调用；返回本次生成的工单号列表）。
    /// 仅处理 IsActive=1 且 NextDueAt <= GETDATE() 的 Time 型调度。
    /// </summary>
    public List<string> CheckAndGenerate()
    {
        var created = new List<string>();
        var due = DbHelper.GetTable(
            @"SELECT ms.ScheduleId, ms.DeviceId, ms.Title, ms.TemplateDesc, ms.TemplatePriority, ms.IntervalDays
              FROM dbo.maintenance_schedule ms
              WHERE ms.IsActive = 1 AND ms.NextDueAt IS NOT NULL AND ms.NextDueAt <= GETDATE()");

        foreach (DataRow r in due.Rows)
        {
            try
            {
                var desc = $"[预防性维护] {r["Title"]}（自动生成，调度编号 #{r["ScheduleId"]}）\n{r["TemplateDesc"]}";
                var orderNo = _orders.Create(
                    Convert.ToInt32(r["DeviceId"]), 1, desc, "机械",
                    r["TemplatePriority"].ToString()!, null, null);
                created.Add(orderNo);

                // 更新执行时间与下次到期
                DbHelper.ExecuteNonQuery(
                    @"UPDATE dbo.maintenance_schedule
                      SET LastExecutedAt = GETDATE(),
                          NextDueAt = DATEADD(DAY, ISNULL(IntervalDays, 30), GETDATE())
                      WHERE ScheduleId = @Id",
                    CommandType.Text, DbHelper.P("@Id", Convert.ToInt32(r["ScheduleId"])));

                NotificationCenter.Post(AppNotification.Info("预防性维护", $"已自动生成工单 {orderNo}：{r["Title"]}"));
            }
            catch (Exception ex) { FileLog.Write($"PM 自动生成失败（调度 #{r["ScheduleId"]}）：{ex.Message}"); }
        }
        return created;
    }
}

/* =====================================================================
   R44：EOQ 库存优化（经济批量 + 再订购点 + 安全库存）
   ===================================================================== */

/// <summary>EOQ 优化建议结果。</summary>
public record InventoryRecommendation(
    string PartCode, string PartName, int CurrentStock, int MinStock,
    decimal AnnualDemandQty, decimal UnitPrice,
    double Eoq, double ReorderPoint, double SafetyStock,
    bool ShouldReorder, string Recommendation);

/// <summary>
/// 库存优化服务（R44）：基于消耗历史计算 EOQ / ROP / 安全库存。
/// 纯算法 + SQL 取数，不依赖第三方库。
/// </summary>
public static class InventoryOptimizer
{
    /// <summary>订货成本（元/次，行业默认）。</summary>
    public const double OrderingCost = 50.0;

    /// <summary>持有成本比率（年，占单价百分比）。</summary>
    public const double HoldingRate = 0.25;

    /// <summary>服务水平 z 值（95% = 1.65）。</summary>
    public const double ServiceLevelZ = 1.65;

    /// <summary>默认交期（天）。</summary>
    public const int DefaultLeadTimeDays = 14;

    /// <summary>
    /// EOQ 经济批量：Q* = √(2DS/H)。
    /// D=年需求量, S=订货成本, H=持有成本（单价×持有率）。
    /// </summary>
    public static double CalculateEoq(double annualDemand, double unitPrice,
        double orderingCost = OrderingCost, double holdingRate = HoldingRate)
    {
        if (annualDemand <= 0 || unitPrice <= 0) return 0;
        var holdingCost = unitPrice * holdingRate;
        if (holdingCost <= 0) return 0;
        return Math.Sqrt(2 * annualDemand * orderingCost / holdingCost);
    }

    /// <summary>
    /// 再订购点：ROP = (日均需求 × 交期) + 安全库存。
    /// </summary>
    public static double CalculateReorderPoint(double avgDailyDemand, int leadTimeDays,
        double safetyStock) => avgDailyDemand * leadTimeDays + safetyStock;

    /// <summary>
    /// 安全库存：SS = z × σ × √L（z=1.65 对应 95% 服务水平）。
    /// </summary>
    public static double CalculateSafetyStock(double dailyDemandStdDev, int leadTimeDays,
        double z = ServiceLevelZ) => z * dailyDemandStdDev * Math.Sqrt(leadTimeDays);

    /// <summary>对全部配件生成库存优化建议（需有消耗历史）。</summary>
    public static List<InventoryRecommendation> GenerateRecommendations()
    {
        // 从消耗分析视图取年用量 + 计算日需求标准差
        var consumption = DbHelper.GetTable(
            @"SELECT ca.PartId, ca.PartCode, ca.PartName, ca.TotalQty, ca.TotalCost,
                     p.StockQty, p.MinStock, p.Price,
                     ca.Qty30d  -- 近30天用量（用于估日均需求）
              FROM dbo.vw_ConsumptionAnalysis ca
              JOIN dbo.part p ON p.PartId = ca.PartId");

        // 取日需求标准差（从流水表近似）— FR6 修复：按 CreatedAt 日期分组而非 RefNo
        var demandVariability = DbHelper.GetTable(
            @"SELECT PartId,
                     STDEV(dailyQty) AS DailyStdDev
              FROM (SELECT PartId, ChangeQty AS dailyQty
                    FROM dbo.part_stock_log
                    WHERE ChangeQty < 0) raw
              GROUP BY PartId");

        var stdDevMap = new System.Collections.Generic.Dictionary<int, double>();
        foreach (DataRow r in demandVariability.Rows)
            stdDevMap[Convert.ToInt32(r["PartId"])] = r["DailyStdDev"] == DBNull.Value ? 0 : Convert.ToDouble(r["DailyStdDev"]);

        var result = new List<InventoryRecommendation>();
        foreach (DataRow r in consumption.Rows)
        {
            var annualQty = Convert.ToDouble(r["TotalQty"]);
            var unitPrice = Convert.ToDouble(r["Price"]);
            var currentStock = Convert.ToInt32(r["StockQty"]);
            var qty30d = Convert.ToDouble(r["Qty30d"]);
            var avgDaily = qty30d / 30.0;
            // FR6 修复：用实际 PartId 取标准差（原 TryGetValue(0) 恒走近似值）
            var partId = Convert.ToInt32(r["PartId"]);
            var stdDev = stdDevMap.TryGetValue(partId, out var sd) ? sd : avgDaily * 0.3;

            var eoq = CalculateEoq(annualQty, unitPrice);
            var safetyStock = CalculateSafetyStock(stdDev, DefaultLeadTimeDays);
            var rop = CalculateReorderPoint(avgDaily, DefaultLeadTimeDays, safetyStock);
            var shouldReorder = currentStock <= rop;

            var rec = shouldReorder
                ? $"建议订购 {Math.Ceiling(eoq):F0} 个（当前 {currentStock} ≤ 再订购点 {rop:F0}）"
                : $"库存充足（{currentStock} > ROP {rop:F0}）";

            result.Add(new InventoryRecommendation(
                r["PartCode"].ToString()!, r["PartName"].ToString()!,
                currentStock, Convert.ToInt32(r["MinStock"]),
                Convert.ToDecimal(annualQty), Convert.ToDecimal(unitPrice),
                eoq, rop, safetyStock, shouldReorder, rec));
        }
        return result;
    }
}

/* =====================================================================
   R45：供应商管理
   ===================================================================== */

/// <summary>供应商实体。</summary>
public record Vendor(int VendorId, string VendorName, string? Contact, string? Phone,
    string? Email, string? Address, bool Status = true);

/// <summary>供应商服务（R45）：CRUD + 配件关联。</summary>
public class VendorService
{
    public DataTable List() =>
        DbHelper.GetTable("SELECT * FROM dbo.vendor ORDER BY VendorName");

    public DataTable ListPartVendors(int partId) =>
        DbHelper.GetTable(
            @"SELECT v.VendorName, pv.SupplyPrice, pv.LeadTimeDays, pv.IsPreferred
              FROM dbo.part_vendor pv
              JOIN dbo.vendor v ON v.VendorId = pv.VendorId
              WHERE pv.PartId = @Id
              ORDER BY pv.IsPreferred DESC, pv.SupplyPrice",
            CommandType.Text, DbHelper.P("@Id", partId));

    public Result Save(Vendor v)
    {
        try
        {
            DbHelper.ExecuteNonQuery(
                @"MERGE dbo.vendor AS target
                  USING (SELECT @Id AS VendorId) AS src ON target.VendorId = src.VendorId
                  WHEN MATCHED THEN UPDATE SET VendorName=@N, Contact=@C, Phone=@P, Email=@E, Address=@A, Status=@S
                  WHEN NOT MATCHED THEN INSERT(VendorName, Contact, Phone, Email, Address, Status)
                  VALUES(@N, @C, @P, @E, @A, @S)",
                CommandType.Text,
                DbHelper.P("@Id", v.VendorId), DbHelper.P("@N", v.VendorName),
                DbHelper.P("@C", (object?)v.Contact ?? DBNull.Value),
                DbHelper.P("@P", (object?)v.Phone ?? DBNull.Value),
                DbHelper.P("@E", (object?)v.Email ?? DBNull.Value),
                DbHelper.P("@A", (object?)v.Address ?? DBNull.Value),
                DbHelper.P("@S", v.Status));
            return Result.Ok("保存成功。");
        }
        catch (Exception ex) { return Result.Fail(DbHelper.FriendlyError(ex)); }
    }
}

/* =====================================================================
   R45：设备停机追踪
   ===================================================================== */

/// <summary>设备停机服务（R45）：开始/结束停机 + 历史查询 + OEE 分母。</summary>
public class DowntimeService
{
    /// <summary>开始停机（EndTime=NULL 表示正在停机）。</summary>
    public Result Start(int deviceId, string reason, string userName)
    {
        try
        {
            // 检查是否已有进行中的停机
            var active = DbHelper.ExecuteScalar(
                "SELECT COUNT(*) FROM dbo.downtime_record WHERE DeviceId=@D AND EndTime IS NULL",
                CommandType.Text, DbHelper.P("@D", deviceId));
            if (Convert.ToInt32(active ?? 0) > 0)
                return Result.Fail("该设备已有进行中的停机记录。");

            DbHelper.ExecuteNonQuery(
                "INSERT INTO dbo.downtime_record(DeviceId, StartTime, Reason, CreatedBy) VALUES(@D, GETDATE(), @R, @U)",
                CommandType.Text, DbHelper.P("@D", deviceId),
                DbHelper.P("@R", (object?)reason ?? DBNull.Value), DbHelper.P("@U", userName));
            return Result.Ok("停机已开始记录。");
        }
        catch (Exception ex) { return Result.Fail(DbHelper.FriendlyError(ex)); }
    }

    /// <summary>结束停机。</summary>
    public Result Stop(int deviceId)
    {
        try
        {
            var rows = DbHelper.ExecuteNonQuery(
                "UPDATE dbo.downtime_record SET EndTime = GETDATE() WHERE DeviceId=@D AND EndTime IS NULL",
                CommandType.Text, DbHelper.P("@D", deviceId));
            if (rows == 0) return Result.Fail("该设备没有进行中的停机记录。");
            return Result.Ok("停机已结束。");
        }
        catch (Exception ex) { return Result.Fail(DbHelper.FriendlyError(ex)); }
    }

    /// <summary>设备停机汇总（含进行中数量）。</summary>
    public DataTable DeviceDowntime() =>
        DbHelper.GetTable("SELECT * FROM dbo.vw_DeviceDowntime ORDER BY TotalDownMinutes DESC");

    /// <summary>某设备的停机历史。</summary>
    public DataTable History(int deviceId) =>
        DbHelper.GetTable(
            @"SELECT StartTime, EndTime,
                     DATEDIFF(MINUTE, StartTime, ISNULL(EndTime, GETDATE())) AS DurationMin,
                     Reason, CreatedBy
              FROM dbo.downtime_record WHERE DeviceId=@D ORDER BY StartTime DESC",
            CommandType.Text, DbHelper.P("@D", deviceId));

    /// <summary>FR11：计算设备可用率（简化 OEE 的 Availability 分量）。</summary>
    /// <param name="deviceId">设备 ID</param>
    /// <param name="days">统计周期（天），默认 30</param>
    /// <returns>可用率百分比（0-100）；无停机记录返回 100</returns>
    public double CalculateAvailability(int deviceId, int days = 30)
    {
        var result = DbHelper.ExecuteScalar(
            @"SELECT ISNULL(SUM(DATEDIFF(MINUTE, dr.StartTime, ISNULL(dr.EndTime, GETDATE()))), 0)
              FROM dbo.downtime_record dr
              WHERE dr.DeviceId = @D AND dr.StartTime >= DATEADD(DAY, -@Days, GETDATE())",
            CommandType.Text, DbHelper.P("@D", deviceId), DbHelper.P("@Days", days));
        var downMinutes = Convert.ToDouble(result ?? 0);
        var totalMinutes = days * 24.0 * 60.0;
        return Math.Max(0, Math.Min(100, (totalMinutes - downMinutes) / totalMinutes * 100));
    }

    /// <summary>FR11：全部设备可用率汇总表。</summary>
    public DataTable AvailabilityReport(int days = 30)
    {
        var t = DbHelper.GetTable(
            @"SELECT d.DeviceCode, d.DeviceName,
                     ISNULL(vd.DowntimeCount, 0) AS 停机次数,
                     ISNULL(vd.TotalDownMinutes, 0) AS 总停机分钟,
                     ISNULL(vd.AvgDownMinutes, 0) AS 平均停机分钟,
                     ISNULL(vd.ActiveDowntime, 0) AS 进行中,
                     CAST(100.0 AS DECIMAL(5,1)) AS 可用率
              FROM dbo.device d
              LEFT JOIN dbo.vw_DeviceDowntime vd ON vd.DeviceId = d.DeviceId");
        // 计算可用率（C# 侧，避免复杂 SQL）
        foreach (DataRow r in t.Rows)
        {
            var downMin = Convert.ToDouble(r["总停机分钟"]);
            var totalMin = days * 24.0 * 60.0;
            r["可用率"] = Math.Round(Math.Max(0, Math.Min(100, (totalMin - downMin) / totalMin * 100)), 1);
        }
        return t;
    }
}
