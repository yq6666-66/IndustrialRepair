using IndustrialRepair.UI;
using Xunit;

namespace IndustrialRepair.Tests;

/// <summary>大屏全屏状态机：进入隐藏导航、退出必须还原（修复退出后导航不恢复）。</summary>
public class DashboardFullscreenTests
{
    [Fact]
    public void Entering_fullscreen_hides_navigation()
    {
        var next = DashboardFullscreen.Toggle(new DashboardFullscreen.State(false, true));
        Assert.True(next.Fullscreen);
        Assert.False(next.NavVisible);
    }

    [Fact]
    public void Leaving_fullscreen_restores_navigation()
    {
        var next = DashboardFullscreen.Toggle(new DashboardFullscreen.State(true, false));
        Assert.False(next.Fullscreen);
        Assert.True(next.NavVisible);
    }

    [Fact]
    public void Double_toggle_returns_to_windowed_with_navigation()
    {
        var start = new DashboardFullscreen.State(false, true);
        var back = DashboardFullscreen.Toggle(DashboardFullscreen.Toggle(start));
        Assert.Equal(start, back);
    }

    [Fact]
    public void Inconsistent_state_still_restores_navigation_on_exit()
    {
        var next = DashboardFullscreen.Toggle(new DashboardFullscreen.State(true, true));
        Assert.False(next.Fullscreen);
        Assert.True(next.NavVisible);
    }
}
