using IndustrialRepair.BLL;
using Xunit;

namespace IndustrialRepair.Tests;

/// <summary>VR1：EOQ 纯算法单元测试（经济批量/再订购点/安全库存）。</summary>
public class InventoryOptimizerTests
{
    [Theory]
    [InlineData(1000, 10, 50, 0.25, 200)]     // sqrt(2*1000*50/2.5) = sqrt(40000) = 200
    [InlineData(3650, 5, 50, 0.25, 540)]       // sqrt(2*3650*50/1.25) = sqrt(292000) ~ 540
    [InlineData(100, 100, 50, 0.25, 20)]       // sqrt(2*100*50/25) = sqrt(400) = 20
    [InlineData(0, 10, 50, 0.25, 0)]           // 零需求 → 0
    [InlineData(1000, 0, 50, 0.25, 0)]         // 零单价 → 0
    public void EOQ经济批量(double demand, double price, double orderCost, double holdRate, double expected)
    {
        var eoq = InventoryOptimizer.CalculateEoq(demand, price, orderCost, holdRate);
        Assert.True(Math.Abs(eoq - expected) < 1.0, $"EOQ={eoq:F2} expected≈{expected}");
    }

    [Fact]
    public void 再订购点_日均乘交期加安全库存()
    {
        var rop = InventoryOptimizer.CalculateReorderPoint(avgDailyDemand: 10, leadTimeDays: 14, safetyStock: 20);
        Assert.Equal(10 * 14 + 20, rop);
    }

    [Fact]
    public void 再订购点_零交期等于安全库存()
    {
        var rop = InventoryOptimizer.CalculateReorderPoint(10, 0, 25);
        Assert.Equal(25, rop);
    }

    [Theory]
    [InlineData(5, 14, 1.65, 30.79)]   // 1.65 * 5 * sqrt(14) ~ 30.79
    [InlineData(0, 14, 1.65, 0)]        // 零标准差 → 0
    [InlineData(10, 30, 1.65, 90.74)]   // 1.65 * 10 * sqrt(30) ~ 90.74
    public void 安全库存(double stdDev, int leadTime, double z, double expected)
    {
        var ss = InventoryOptimizer.CalculateSafetyStock(stdDev, leadTime, z);
        Assert.True(Math.Abs(ss - expected) < 0.5, $"SS={ss:F2} expected≈{expected}");
    }

    [Fact]
    public void 安全库存_零交期()
    {
        Assert.Equal(0, InventoryOptimizer.CalculateSafetyStock(10, 0, 1.65));
    }

    [Fact]
    public void 默认常量合理()
    {
        Assert.True(InventoryOptimizer.OrderingCost > 0);
        Assert.True(InventoryOptimizer.HoldingRate > 0 && InventoryOptimizer.HoldingRate < 1);
        Assert.True(InventoryOptimizer.ServiceLevelZ > 1 && InventoryOptimizer.ServiceLevelZ < 3);
        Assert.True(InventoryOptimizer.DefaultLeadTimeDays > 0);
    }

    [Theory]
    [InlineData(0, 30, 100)]        // 无停机 → 100%
    [InlineData(43200, 30, 0)]       // 全月停机（30*24*60 分钟）→ 0%
    [InlineData(4320, 30, 90)]       // 停机 3 天 / 30 天 → 90%
    [InlineData(999999, 30, 0)]      // 超限裁剪到 0
    [InlineData(-500, 30, 100)]      // 负值裁剪到 100
    public void OEE可用率_纯函数(double downMinutes, int days, double expected)
    {
        var avail = DowntimeService.ComputeAvailability(downMinutes, days);
        Assert.True(Math.Abs(avail - expected) < 0.01, $"Availability={avail:F2} expected={expected}");
    }

    [Fact]
    public void OEE可用率_零周期防御()
    {
        Assert.Equal(100, DowntimeService.ComputeAvailability(100, 0));
    }
}
