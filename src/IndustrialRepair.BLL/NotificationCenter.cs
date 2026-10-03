using System;

namespace IndustrialRepair.BLL;

public enum NotificationLevel
{
    Info,
    Success,
    Warning,
    Alert
}

/// <summary>应用通知（托盘气泡等消费者渲染）。</summary>
public sealed record AppNotification(string Title, string Body, NotificationLevel Level = NotificationLevel.Info)
{
    public static AppNotification Info(string title, string body) => new(title, body, NotificationLevel.Info);
    public static AppNotification Success(string title, string body) => new(title, body, NotificationLevel.Success);
    public static AppNotification Warning(string title, string body) => new(title, body, NotificationLevel.Warning);
    public static AppNotification Alert(string title, string body) => new(title, body, NotificationLevel.Alert);
}

/// <summary>
/// 应用级通知中心（NR3）：BLL 事件（MQ 自动报修、定时任务预警等）统一在此发布，
/// UI（托盘气泡）与测试各自订阅。订阅者异常互不影响。
/// </summary>
public static class NotificationCenter
{
    /// <summary>通知发布事件；订阅方不得抛异常（内部已隔离），UI 线程自行封送。</summary>
    public static event Action<AppNotification>? Raised;

    /// <summary>记录的订阅者（测试/审计可读）。</summary>
    public static int SubscriberCount => Raised?.GetInvocationList().Length ?? 0;

    public static void Post(AppNotification notification)
    {
        if (Raised == null) return;
        foreach (var handler in Raised.GetInvocationList())
        {
            try { ((Action<AppNotification>)handler)(notification); }
            catch (Exception ex) { FileLog.Write($"通知订阅者异常：{ex.Message}"); }
        }
    }

    /// <summary>测试用：清空订阅。</summary>
    public static void ClearSubscribers() => Raised = null;
}
