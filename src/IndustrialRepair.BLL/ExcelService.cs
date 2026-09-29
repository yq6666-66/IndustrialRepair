using System.Data;
using System.Text;
using ClosedXML.Excel;

namespace IndustrialRepair.BLL;

/// <summary>
/// Excel 报表导出（开源库 ClosedXML，~4.6k★，MIT）。
/// 支持任意 DataTable 一键导出 .xlsx：表头样式 + 冻结首行 + 自适应列宽 + 合计行。
/// </summary>
public static class ExcelService
{
    public static string ExportDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "IndustrialRepair", "exports");

    /// <summary>导出 DataTable 为 xlsx，返回文件路径</summary>
    public static string ExportTable(DataTable table, string fileTitle, string sheetName = "Sheet1", bool totalRow = false)
    {
        Directory.CreateDirectory(ExportDir);
        var file = Path.Combine(ExportDir, $"{fileTitle}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName);

        // 标题行
        ws.Cell(1, 1).Value = $"{fileTitle}（导出时间 {DateTime.Now:yyyy-MM-dd HH:mm:ss}）";
        ws.Range(1, 1, 1, Math.Max(table.Columns.Count, 1)).Merge().Style
          .Font.SetBold().Font.SetFontSize(13)
          .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        ws.Row(1).Height = 24;

        // 表头
        for (int c = 0; c < table.Columns.Count; c++)
            ws.Cell(2, c + 1).Value = table.Columns[c].ColumnName;
        var header = ws.Range(2, 1, 2, table.Columns.Count);
        header.Style.Font.SetBold().Fill.BackgroundColor = XLColor.FromHtml("#4472C4");
        header.Style.Font.FontColor = XLColor.White;
        ws.SheetView.FreezeRows(2);

        // 数据
        for (int r = 0; r < table.Rows.Count; r++)
            for (int c = 0; c < table.Columns.Count; c++)
                ws.Cell(r + 3, c + 1).Value = XLCellValue.FromObject(table.Rows[r][c]);

        // 合计行（数值列求和）
        if (totalRow && table.Rows.Count > 0)
        {
            int totalRowIndex = table.Rows.Count + 3;
            ws.Cell(totalRowIndex, 1).Value = "合计";
            for (int c = 0; c < table.Columns.Count; c++)
                if (IsNumeric(table.Columns[c].DataType))
                {
                    decimal sum = 0; bool any = false;
                    foreach (DataRow row in table.Rows)
                        if (row[c] != DBNull.Value) { sum += Convert.ToDecimal(row[c]); any = true; }
                    if (any) ws.Cell(totalRowIndex, c + 1).Value = sum;
                }
            ws.Range(totalRowIndex, 1, totalRowIndex, table.Columns.Count).Style.Font.SetBold();
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(file);
        return file;
    }

    private static bool IsNumeric(Type t) =>
        t == typeof(decimal) || t == typeof(int) || t == typeof(double) || t == typeof(long) || t == typeof(float);
}
