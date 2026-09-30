using IndustrialRepair.BLL;
using IndustrialRepair.Models;
using Xunit;

namespace IndustrialRepair.Tests;

/// <summary>候选规整测试（M2.2）：占位过滤 / 编码去重保高分 / 同分稳定排序 / Top3 截断。</summary>
public class RecognitionNormalizeTests
{
    private static RecognizeResult R(int id, string code, int score, string source = RecognizeSource.OpenCv) =>
        new() { PartId = id, PartCode = code, PartName = $"P{code}", Score = score, Source = source };

    [Fact]
    public void 过滤无PartId占位项()
    {
        var norm = RecognitionEngine.Normalize(new[] { R(1, "A", 80), R(0, "X", 99), R(0, "", 50) });
        Assert.Single(norm);
        Assert.Equal("A", norm[0].PartCode);
    }

    [Fact]
    public void 同编码去重保留高分_大小写归并()
    {
        var norm = RecognitionEngine.Normalize(new[] { R(1, "P-ZC-001", 70), R(1, "p-zc-001", 90) });
        Assert.Single(norm);
        Assert.Equal(90, norm[0].Score);
    }

    [Fact]
    public void 同分按编码升序稳定排序()
    {
        var norm = RecognitionEngine.Normalize(new[] { R(3, "C", 80), R(2, "A", 80), R(4, "B", 90) });
        Assert.Equal(new[] { "B", "A", "C" }, norm.Select(r => r.PartCode));
    }

    [Fact]
    public void 截断为前3()
    {
        var norm = RecognitionEngine.Normalize(new[]
        {
            R(1, "A", 10), R(2, "B", 20), R(3, "C", 30), R(4, "D", 40), R(5, "E", 50)
        });
        Assert.Equal(new[] { "E", "D", "C" }, norm.Select(r => r.PartCode));
    }
}

/// <summary>CSV 转义测试（M2.3）：逗号/引号/换行/组合与整行拼接。</summary>
public class CsvEscapeTests
{
    [Theory]
    [InlineData("普通", "普通")]
    [InlineData("带,逗号", "\"带,逗号\"")]
    [InlineData("带\"引号", "\"带\"\"引号\"")]
    [InlineData("带\n换行", "\"带\n换行\"")]
    [InlineData("", "")]          // 空字段不转义
    [InlineData(null, "")]        // null 字段按空
    public void 字段转义(string? input, string expected)
    {
        Assert.Equal(expected, RecognitionEvalCore.CsvEscape(input));
    }

    [Fact]
    public void 整行拼接含转义与CRLF()
    {
        var line = RecognitionEvalCore.CsvLine("a,b", "c", null);
        Assert.Equal("\"a,b\",c,\r\n", line);
    }
}

/// <summary>评测统计测试（M2.3）：Top1/Top3 分母、未匹配、负样本、混淆矩阵。</summary>
public class EvalStatsTests
{
    private static EvalRow Row(string img, string? expected, string? top1, params string[] top3) =>
        new(img, expected, top1, RecognizeSource.OpenCv, 80, top3, Unmatched: false, ElapsedMs: 1);

    [Fact]
    public void 分母含未匹配正样本()
    {
        var rows = new[]
        {
            Row("1", "A", "A", "A"),        // Top1 对
            Row("2", "A", "B", "A", "B"),   // Top1 错 Top3 命中
            Row("3", "A", null),            // 未匹配（计入分母）
        };
        var s = RecognitionEvalCore.Compute(rows);
        Assert.Equal(3, s.Positives);
        Assert.Equal(1, s.Top1Correct);
        Assert.Equal(2, s.Top3Hit);
        Assert.Equal(1, s.Unmatched);
    }

    [Fact]
    public void 负样本统计错误接受()
    {
        var rows = new[]
        {
            Row("neg1", null, "A", "A"),    // 负样本错误接受
            new EvalRow("neg2", null, null, null, 0, Array.Empty<string>(), Unmatched: true, ElapsedMs: 1), // 空候选=正确拒识
        };
        var s = RecognitionEvalCore.Compute(rows);
        Assert.Equal(2, s.Negatives);
        Assert.Equal(1, s.NegAccepted);
        Assert.Equal(0, s.Positives);
    }

    [Fact]
    public void 混淆矩阵计数()
    {
        var s = RecognitionEvalCore.Compute(new[]
        {
            Row("1", "A", "B"), Row("2", "A", "B"), Row("3", "A", "A"),
        });
        Assert.Equal(2, s.Confusion["A"]["B"]);
        Assert.Equal(1, s.Confusion["A"]["A"]);
    }
}
