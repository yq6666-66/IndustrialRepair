using System;
using System.Data;
using System.Threading.Tasks;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;
using Quartz;

namespace IndustrialRepair.BLL;

/// <summary>
/// 库存预警汇总（每日 08:30）：未处理预警计数与清单写操作日志，
/// 有预警时上抛通知中心。逻辑在 RunOnce（可脱离调度直接调用），Execute 仅包装。
/// </summary>
public class StockAlertDigestJob : IJob
{
    public const string Module = "定时任务";
    public const string Action = "库存预警汇总";

    public Task Execute(IJobExecutionContext context)
    {
        RunOnce();
        return Task.CompletedTask;
    }

    /// <summary>执行一次汇总，返回摘要文本（冒烟/演示用）。</summary>
    public static string RunOnce()
    {
        var t = DbHelper.GetTable(
            @"SELECT p.PartCode, p.PartName, p.StockQty, p.MinStock
              FROM dbo.stock_alert a JOIN dbo.part p ON p.PartId = a.PartId
              WHERE a.Handled = 0");
        var summary = t.Rows.Count == 0
            ? "当前无未处理库存预警。"
            : $"共 {t.Rows.Count} 项未处理预警：" + string.Join("；",
                  t.Rows.OfType<DataRow>().Take(10)
                       .Select(r => $"{r["PartName"]}（{r["StockQty"]}/{r["MinStock"]}）"));

        new UserRepository().WriteOpLog(1, "系统", Module, Action, summary);
        if (t.Rows.Count > 0)
            NotificationCenter.Post(AppNotification.Warning("库存预警汇总", summary));
        FileLog.Write($"[定时任务] {Action}：{summary}");
        return summary;
    }
}

/// <summary>
/// 超时工单扫描（每 30 分钟）：已派单/维修中超过 StaleHours 小时未流转的工单，
/// 写操作日志并上抛通知中心。
/// </summary>
public class StaleOrderScanJob : IJob
{
    public const int StaleHours = 48;

    public Task Execute(IJobExecutionContext context)
    {
        RunOnce();
        return Task.CompletedTask;
    }

    /// <summary>停滞时间（小时）纯函数：最长未流转时刻 = MAX(CreatedAt, AssignTime, StartTime) 之后到现在的间隔。供单测。</summary>
    public static double StaleHoursOf(DateTime createdAt, DateTime? assignTime, DateTime? startTime, DateTime now) =>
        (now - new[] { createdAt, assignTime ?? DateTime.MinValue, startTime ?? DateTime.MinValue }.Max()).TotalHours;

    public static string RunOnce()
    {
        var t = DbHelper.GetTable(
            $@"SELECT TOP 10 o.OrderNo, d.DeviceName, o.Status,
                      DATEDIFF(HOUR, ISNULL(NULLIF(o.StartTime, '1900-01-01'),
                          ISNULL(NULLIF(o.AssignTime, '1900-01-01'), o.CreatedAt)), GETDATE()) AS StaleHours
              FROM dbo.work_order o
              JOIN dbo.device d ON d.DeviceId = o.DeviceId
              WHERE o.Status IN (N'Assigned', N'Working')
                AND ISNULL(NULLIF(o.StartTime, '1900-01-01'),
                    ISNULL(NULLIF(o.AssignTime, '1900-01-01'), o.CreatedAt)) < DATEADD(HOUR, -{StaleHours}, GETDATE())
              ORDER BY StaleHours DESC");
        var summary = t.Rows.Count == 0
            ? $"无停滞超过 {StaleHours} 小时的在办工单。"
            : $"{t.Rows.Count} 张工单停滞超 {StaleHours} 小时：" + string.Join("；",
                  t.Rows.OfType<DataRow>().Select(r => $"{r["OrderNo"]}（{r["Status"]}，{r["StaleHours"]}h）"));

        new UserRepository().WriteOpLog(1, "系统", StockAlertDigestJob.Module, "超时工单扫描", summary);
        if (t.Rows.Count > 0)
            NotificationCenter.Post(AppNotification.Warning("超时工单提醒", summary));
        FileLog.Write($"[定时任务] 超时工单扫描：{summary}");
        return summary;
    }
}

/// <summary>
/// 预防性维护调度作业（R41）：每 30 分钟检查到期 PM → 自动生成工单。
/// </summary>
public class PreventiveMaintenanceJob : IJob
{
    public Task Execute(IJobExecutionContext context)
    {
        RunOnce();
        return Task.CompletedTask;
    }

    /// <summary>执行一次到期检查（返回生成的工单号列表）。</summary>
    public static List<string> RunOnce()
    {
        var created = new MaintenanceScheduleService().CheckAndGenerate();
        if (created.Count > 0)
            FileLog.Write($"[PM] 自动生成 {created.Count} 张预防性维护工单：{string.Join(", ", created)}");
        return created;
    }
}

/// <summary>
/// 调度宿主（Quartz.NET）：FMain 登录后 Start，关闭时 Stop。
/// 启动即先同步跑一轮便于演示立即见效；重复 Start 幂等。
/// </summary>
public static class ScheduleHost
{
    private static IScheduler? _scheduler;
    private static readonly object Lock = new();

    public static bool Running => _scheduler?.IsStarted == true;

    public static void Start()
    {
        lock (Lock)
        {
            if (Running) return;
            try
            {
                _scheduler = new Quartz.Impl.StdSchedulerFactory().GetScheduler().GetAwaiter().GetResult();
                var digest = JobBuilder.Create<StockAlertDigestJob>().WithIdentity("stockDigest").Build();
                var digestTrig = TriggerBuilder.Create().WithIdentity("stockDigestTrig")
                    .WithCronSchedule("0 30 8 * * ?").Build();          // 每日 08:30
                var stale = JobBuilder.Create<StaleOrderScanJob>().WithIdentity("staleScan").Build();
                var staleTrig = TriggerBuilder.Create().WithIdentity("staleScanTrig")
                    .WithCronSchedule("0 0/30 * * * ?").Build();        // 每 30 分钟

                var pm = JobBuilder.Create<PreventiveMaintenanceJob>().WithIdentity("pmCheck").Build();
                var pmTrig = TriggerBuilder.Create().WithIdentity("pmCheckTrig")
                    .WithCronSchedule("0 5/30 * * * ?").Build();        // 每 30 分钟（偏移 5 分钟避免同时触发）

                _scheduler.ScheduleJob(digest, digestTrig);
                _scheduler.ScheduleJob(stale, staleTrig);
                _scheduler.ScheduleJob(pm, pmTrig);
                _scheduler.Start();

                // 启动即跑一轮（后台线程，不阻塞登录）
                Task.Run(() =>
                {
                    try { StockAlertDigestJob.RunOnce(); StaleOrderScanJob.RunOnce(); }
                    catch (Exception ex) { FileLog.Write("定时任务首轮执行失败：" + ex.Message); }
                });
                FileLog.Write("调度宿主已启动：库存预警汇总(每日08:30) + 超时工单扫描(每30分钟)");
            }
            catch (Exception ex)
            {
                FileLog.Write("调度宿主启动失败（不影响主流程）：" + ex.Message);
                _scheduler = null;
            }
        }
    }

    public static void Stop()
    {
        lock (Lock)
        {
            try { _scheduler?.Shutdown(true); } catch { }
            _scheduler = null;
        }
    }
}
