using System.Text;
using IndustrialRepair.BLL;
using Xunit;

namespace IndustrialRepair.Tests;

/// <summary>NR7：CsvHelper 统一读写的往返契约。</summary>
public class CsvIoTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"csvtest_{Guid.NewGuid():N}");

    public CsvIoTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private record Row(string 类别, string 指标, decimal 数值);

    [Fact]
    public void 写读往返保留特殊字符与BOM()
    {
        var file = CsvIo.WriteAll(new[]
        {
            new Row("状态分布", "带,逗号", 1.5m),
            new Row("预警", "带\"引号", 2m),
            new Row("日志", "带\r\n换行", 3m),
        }, Path.Combine(_dir, "rt.csv"));

        var raw = File.ReadAllBytes(file);
        Assert.Equal(0xEF, raw[0]); Assert.Equal(0xBB, raw[1]); Assert.Equal(0xBF, raw[2]);   // UTF-8 BOM

        var text = File.ReadAllText(file, Encoding.UTF8);
        Assert.Contains("\"带,逗号\"", text);
        Assert.Contains("\"带\"\"引号\"", text);
        Assert.Contains("带\r\n换行", text);   // 字段内换行被引号包裹
    }

    [Fact]
    public void 字典行写出表头与合计()
    {
        var file = CsvIo.WriteRows(new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["配件编码"] = "P-ZC-001", ["数量"] = 3 },
            new Dictionary<string, object?> { ["配件编码"] = "", ["数量"] = "" },
        }, Path.Combine(_dir, "dict.csv"));

        var lines = File.ReadAllLines(file);
        Assert.Equal("配件编码,数量", lines[0]);
        Assert.Equal("P-ZC-001,3", lines[1]);
    }

    [Fact]
    public void 标签读取支持注释与负样本空位()
    {
        var file = Path.Combine(_dir, "labels.csv");
        File.WriteAllText(file, "image,expectedPartCode\r\n# 注释行\r\na.png,P-ZC-001\r\nneg.png,\r\n", new UTF8Encoding(true));

        var labels = CsvIo.ReadLabels(file);
        Assert.Equal(2, labels.Count);
        Assert.Equal("P-ZC-001", labels[0].Expected);
        Assert.Null(labels[1].Expected);      // 负样本
    }

    [Fact]
    public void 标签缺表头被拒()
    {
        var file = Path.Combine(_dir, "bad.csv");
        File.WriteAllText(file, "wrong,header\r\na.png,P-ZC-001\r\n", new UTF8Encoding(true));
        Assert.ThrowsAny<Exception>(() => CsvIo.ReadLabels(file));
    }
}
