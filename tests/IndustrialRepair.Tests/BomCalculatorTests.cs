using IndustrialRepair.BLL;
using Xunit;

namespace IndustrialRepair.Tests;

/// <summary>BOM 算料纯函数测试（规划重点：基础算料、最小领用量、领用步长、边界与异常）。</summary>
public class BomCalculatorTests
{
    private static BomRule Rule(decimal qtyPer, decimal min = 0m, decimal step = 0m) =>
        new(1, qtyPer, min, step);

    [Fact]
    public void 基础语义_与存储过程CEILING一致()
    {
        // 3 × 1.10 = 3.30 → Ceiling = 4（与 usp_GeneratePicking 默认公式一致）
        var r = BomCalculator.Calculate(new[] { Rule(3m) }, 1.10m);
        Assert.Single(r);
        Assert.Equal(4m, r[0].Qty);
    }

    [Fact]
    public void 整除时不进位()
    {
        var r = BomCalculator.Calculate(new[] { Rule(4m) }, 1.00m);
        Assert.Equal(4m, r[0].Qty);
    }

    [Fact]
    public void 小数用量_冗余后向上取整()
    {
        // 1.50 × 2.00 = 3.00 → 3；0.30 × 2.50 = 0.75 → 1
        Assert.Equal(3m, BomCalculator.Calculate(new[] { Rule(1.5m) }, 2.00m)[0].Qty);
        Assert.Equal(1m, BomCalculator.Calculate(new[] { Rule(0.3m) }, 2.50m)[0].Qty);
    }

    [Fact]
    public void 无规则时返回空_不默认为零()
    {
        Assert.Empty(BomCalculator.Calculate(Array.Empty<BomRule>(), 1.10m));
    }

    [Fact]
    public void 不低于最小领用量()
    {
        var r = BomCalculator.Calculate(new[] { Rule(1m, min: 5m) }, 1.00m);
        Assert.Equal(5m, r[0].Qty);
    }

    [Fact]
    public void 按领用步长向上取整()
    {
        // 6 → 不是 5 的倍数 → 10；10 → 已是倍数 → 10
        Assert.Equal(10m, BomCalculator.Calculate(new[] { Rule(6m, step: 5m) }, 1.00m)[0].Qty);
        Assert.Equal(10m, BomCalculator.Calculate(new[] { Rule(10m, step: 5m) }, 1.00m)[0].Qty);
    }

    [Fact]
    public void 最小量与步长叠加_先兜底再对齐步长()
    {
        // 2 × 1.05 = 2.10 → 3，最小量 4 → 4，步长 5 → 5
        var r = BomCalculator.Calculate(new[] { Rule(2m, min: 4m, step: 5m) }, 1.05m);
        Assert.Equal(5m, r[0].Qty);
    }

    [Fact]
    public void 多规则保持条目与顺序()
    {
        var rules = new[] { new BomRule(11, 2m), new BomRule(22, 5m), new BomRule(33, 1m) };
        var r = BomCalculator.Calculate(rules, 1.10m);
        Assert.Equal(new[] { 11, 22, 33 }, r.Select(x => x.PartId));
        Assert.Equal(new[] { 3m, 6m, 2m }, r.Select(x => x.Qty));
    }

    [Theory]
    [InlineData(0.49)]
    [InlineData(3.01)]
    public void 冗余系数越界抛异常(decimal bad)
    {
        Assert.Throws<ArgumentException>(() => BomCalculator.Calculate(new[] { Rule(1m) }, bad));
    }
}
