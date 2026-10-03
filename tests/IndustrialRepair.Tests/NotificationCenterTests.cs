using IndustrialRepair.BLL;
using Xunit;

namespace IndustrialRepair.Tests;

/// <summary>通知中心（NR3）：订阅投递、订阅者异常隔离、退订。</summary>
public class NotificationCenterTests : IDisposable
{
    public NotificationCenterTests() => NotificationCenter.ClearSubscribers();
    public void Dispose() => NotificationCenter.ClearSubscribers();

    [Fact]
    public void 投递到全部订阅者()
    {
        var got = new List<AppNotification>();
        NotificationCenter.Raised += n => got.Add(n);

        NotificationCenter.Post(AppNotification.Info("t1", "b1"));
        NotificationCenter.Post(AppNotification.Alert("t2", "b2"));

        Assert.Equal(2, got.Count);
        Assert.Equal(NotificationLevel.Alert, got[1].Level);
    }

    [Fact]
    public void 订阅者异常不影响其他订阅者()
    {
        var received = 0;
        NotificationCenter.Raised += _ => throw new InvalidOperationException("boom");
        NotificationCenter.Raised += _ => received++;

        NotificationCenter.Post(AppNotification.Info("t", "b"));

        Assert.Equal(1, received);   // 坏订阅者被隔离，好订阅者仍收到
    }

    [Fact]
    public void 退订后不再接收()
    {
        var count = 0;
        void Handler(AppNotification _) => count++;
        NotificationCenter.Raised += Handler;
        NotificationCenter.Post(AppNotification.Info("a", "b"));
        NotificationCenter.Raised -= Handler;
        NotificationCenter.Post(AppNotification.Info("c", "d"));

        Assert.Equal(1, count);
    }

    [Fact]
    public void 无订阅者投递不抛异常()
    {
        NotificationCenter.Post(AppNotification.Warning("t", "b"));
    }
}
