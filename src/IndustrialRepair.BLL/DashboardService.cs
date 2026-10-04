using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;

namespace IndustrialRepair.BLL;

/// <summary>数据可视化大屏数据源</summary>
public class DashboardService
{
    private readonly BasicRepository _basic = new();

    public List<DashboardRow> Rows() => _basic.GetDashboard();

    /// <summary>
    /// 大屏汇总数据导出为 CSV（规划：山海鲸等外部大屏先接导出的统计数据，
    /// 课程提供数据库连接能力后可改实时数据源）。UTF-8 带 BOM，Excel/山海鲸均可直接打开。
    /// </summary>
    public string ExportCsv()
    {
        // NR7：CsvHelper 统一写出（UTF-8 BOM，RFC4180 转义由库保证）
        var rows = new List<Dictionary<string, object?>>();
        foreach (var r in Rows())
            rows.Add(new Dictionary<string, object?> { ["类别"] = r.Kind, ["指标"] = r.Label, ["数值"] = r.Val });

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IndustrialRepair", "export");
        return CsvIo.WriteRows(rows, Path.Combine(dir, $"dashboard_{DateTime.Now:yyyyMMdd_HHmmss}.csv"));
    }

    /// <summary>顶部汇总：今日工单/待派单/维修中/已完成/本月材料成本/库存总价值(R3)</summary>
    public (int today, int pending, int working, int done, decimal monthCost, decimal stockValue) Summary()
    {
        var t = DbHelper.GetTable(
            @"SELECT
                ISNULL(SUM(CASE WHEN CONVERT(date, CreatedAt)=CONVERT(date, GETDATE()) THEN 1 ELSE 0 END), 0) AS TodayCount,
                ISNULL(SUM(CASE WHEN Status='Pending' THEN 1 ELSE 0 END), 0) AS Pending,
                ISNULL(SUM(CASE WHEN Status='Working' THEN 1 ELSE 0 END), 0) AS Working,
                ISNULL(SUM(CASE WHEN Status='Done' THEN 1 ELSE 0 END), 0) AS DoneCount,
                (SELECT ISNULL(SUM(TotalAmount), 0) FROM dbo.picking_list
                 WHERE Status='Issued' AND CONVERT(NVARCHAR(7), IssuedAt, 120)=CONVERT(NVARCHAR(7), GETDATE(), 120)) AS MonthCost,
                (SELECT ISNULL(SUM(StockQty * Price), 0) FROM dbo.part) AS StockValue
              FROM dbo.work_order");
        var r = t.Rows[0];
        return ((int)r["TodayCount"], (int)r["Pending"], (int)r["Working"], (int)r["DoneCount"],
                (decimal)r["MonthCost"], (decimal)r["StockValue"]);
    }

    /// <summary>高成本工单 TopN（R3：报表页下半榜）</summary>
    public System.Data.DataTable OrderCostTop(int top = 10) =>
        DbHelper.GetTable($"SELECT TOP ({Math.Clamp(top, 1, 50)}) OrderNo, DeviceName, EngineerName, Status, MaterialCost FROM dbo.vw_OrderCost WHERE MaterialCost > 0 ORDER BY MaterialCost DESC");

    /// <summary>配件消耗分析（R21：TOP N 按用量/成本，三窗口）。</summary>
    public System.Data.DataTable ConsumptionAnalysis(int top = 10, string orderBy = "TotalQty") =>
        DbHelper.GetTable(
            $@"SELECT TOP ({Math.Clamp(top, 1, 50)}) PartCode, PartName, Spec, Unit,
                      TotalQty, TotalCost, Qty30d, Cost30d, Qty90d, Cost90d, StockQty, MinStock
              FROM dbo.vw_ConsumptionAnalysis
              ORDER BY {Math.Clamp(orderBy == "TotalCost" ? 1 : 0, 0, 1) switch { 1 => "TotalCost", _ => "TotalQty" }} DESC");

    /// <summary>按设备汇总成本（R24：成本中心报表）。</summary>
    public System.Data.DataTable DeviceCostReport() =>
        DbHelper.GetTable(
            @"SELECT DeviceCode, DeviceName, Location, OrderCount, TotalCost, AvgMinutes
              FROM dbo.vw_DeviceCost
              ORDER BY TotalCost DESC");
}
