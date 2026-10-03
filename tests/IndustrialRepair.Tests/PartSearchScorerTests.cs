using IndustrialRepair.BLL;
using IndustrialRepair.Models;
using Xunit;

namespace IndustrialRepair.Tests;

/// <summary>NR10：配件模糊搜索评分（纯函数，无数据库）。</summary>
public class PartSearchScorerTests
{
    private static Part P(string code, string name, string? spec = null) => new()
    { PartId = 1, PartCode = code, PartName = name, Spec = spec };

    [Fact]
    public void 完全匹配编码得满分()
    {
        Assert.Equal(100, PartSearchScorer.Score("P-ZC-001", P("P-ZC-001", "深沟球轴承")));
    }

    [Fact]
    public void 名称部分匹配高于阈值()
    {
        Assert.True(PartSearchScorer.Score("轴承", P("P-ZC-001", "深沟球轴承6205")) >= PartSearchScorer.Threshold);
    }

    [Fact]
    public void 编码拼写错误可模糊命中()
    {
        // P-ZC-001 错敲一位 → P-ZC-010，编辑距离仍能给出较高分
        Assert.True(PartSearchScorer.Score("P-ZC-010", P("P-ZC-001", "深沟球轴承")) >= PartSearchScorer.Threshold);
    }

    [Fact]
    public void 规格字段也参与评分()
    {
        Assert.True(PartSearchScorer.Score("25x52x15", P("P-ZC-001", "深沟球轴承", "6205-2RS 25x52x15")) > 0);
    }

    [Fact]
    public void 完全不相关低于阈值()
    {
        Assert.True(PartSearchScorer.Score("xyzabc123", P("P-ZC-001", "深沟球轴承6205")) < PartSearchScorer.Threshold);
    }

    [Fact]
    public void 排序按分数降序并过滤阈值()
    {
        var parts = new[]
        {
            P("P-ZC-001", "深沟球轴承6205"),
            P("P-DQ-001", "三相交流接触器CJX2"),
            P("P-MF-001", "骨架油封TC40"),
        };
        var ranked = PartSearchScorer.Rank("轴承", parts);
        Assert.True(ranked.Count >= 1);
        Assert.True(ranked[0].Score >= ranked[^1].Score);   // 降序
        Assert.All(ranked, x => Assert.True(x.Score >= PartSearchScorer.Threshold));
    }

    [Fact]
    public void 空关键字返回空表()
    {
        Assert.Empty(PartSearchScorer.Rank("", new[] { P("P-ZC-001", "轴承") }));
    }
}
