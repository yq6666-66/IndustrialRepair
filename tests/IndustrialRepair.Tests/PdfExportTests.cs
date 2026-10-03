using System.Data;
using IndustrialRepair.BLL;
using Xunit;

namespace IndustrialRepair.Tests;

/// <summary>QuestPDF 导出（NR2）：通用表格 PDF 的文件契约（存在 / %PDF 魔数 / 大小合理）。</summary>
public class PdfExportTests
{
    [Fact]
    public void 通用表格PDF生成且为合法PDF文件()
    {
        var t = new DataTable();
        t.Columns.Add("编码", typeof(string));
        t.Columns.Add("名称", typeof(string));
        t.Columns.Add("数量", typeof(int));
        t.Rows.Add("P-ZC-001", "深沟球轴承6205", 4);
        t.Rows.Add("P-MF-001,含逗号", "骨架油封\"TC\"", 2);   // 中文/特殊字符排版不崩

        var file = PdfService.ExportTablePdf("单元测试表格", t, qrText: "UNITTEST");

        Assert.True(File.Exists(file), file);
        var bytes = File.ReadAllBytes(file);
        Assert.True(bytes.Length > 1024, $"大小 {bytes.Length}");
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }
}
