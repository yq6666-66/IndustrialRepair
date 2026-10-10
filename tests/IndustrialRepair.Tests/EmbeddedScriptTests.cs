using System.Reflection;
using IndustrialRepair.BLL;
using Xunit;

namespace IndustrialRepair.Tests;

/// <summary>
/// 迁移脚本嵌入资源存在性：DbInitializer.ReadScript 嵌入优先的根基——
/// 任何一个 LogicalName 缺失都会让发布包在不带 db 目录时无法建库。
/// 新增迁移脚本时必须同步在 BLL.csproj 加 EmbeddedResource 并补进本清单。
/// </summary>
public class EmbeddedScriptTests
{
    private static readonly string[] ExpectedScripts =
    {
        "db.01_init.sql",
        "db.02_procs.sql",
        "db.04_material_review_migration.sql",
        "db.05_performance_indexes.sql",
        "db.06_new_features.sql",
        "db.07_pm_order_link.sql",
    };

    [Theory]
    [InlineData("db.01_init.sql")]
    [InlineData("db.02_procs.sql")]
    [InlineData("db.04_material_review_migration.sql")]
    [InlineData("db.05_performance_indexes.sql")]
    [InlineData("db.06_new_features.sql")]
    [InlineData("db.07_pm_order_link.sql")]
    public void 迁移脚本已嵌入BLL程序集(string logicalName)
    {
        var asm = typeof(DbInitializer).Assembly;
        Assert.NotNull(asm.GetManifestResourceInfo(logicalName));
        using var stream = asm.GetManifestResourceStream(logicalName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!, System.Text.Encoding.UTF8);
        var text = reader.ReadToEnd();
        Assert.False(string.IsNullOrWhiteSpace(text), $"{logicalName} 内容为空");
        Assert.Contains("GO", text, StringComparison.OrdinalIgnoreCase);   // 迁移脚本按 GO 分批
    }

    [Fact]
    public void 嵌入清单与清单常量一致()
    {
        var asm = typeof(DbInitializer).Assembly;
        var embedded = asm.GetManifestResourceNames()
            .Where(n => n.StartsWith("db.", StringComparison.Ordinal))
            .OrderBy(n => n).ToArray();
        var expected = ExpectedScripts.OrderBy(n => n).ToArray();
        Assert.Equal(expected, embedded);
    }
}
