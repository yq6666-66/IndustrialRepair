using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;

namespace IndustrialRepair.BLL;

/// <summary>批量导入结果行。</summary>
public record ImportRowResult(int RowNumber, string PartCode, string PartName, bool Success, string? Error);

/// <summary>
/// 配件批量导入（OR3）：Excel/CSV 上传 → FluentValidation 逐行校验 → 预览 → 确认写入。
/// Excel 模板列：编码 | 名称 | 类别 | 规格 | 单位 | 单价 | 库存 | 下限
/// </summary>
public class PartImportService
{
    private readonly PartService _parts = new();

    /// <summary>读取 Excel/CSV 文件并逐行校验（不写入），返回结果列表。</summary>
    public List<ImportRowResult> Validate(string filePath)
    {
        var table = ReadFile(filePath);
        var existing = _parts.Search(null).Select(p => p.PartCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var categories = _parts.Categories().Select(c => c.CategoryName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var results = new List<ImportRowResult>();

        for (int i = 0; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i];
            var code = row["编码"]?.ToString()?.Trim() ?? "";
            var name = row["名称"]?.ToString()?.Trim() ?? "";
            var cat = row["类别"]?.ToString()?.Trim() ?? "";
            var unit = row["单位"]?.ToString()?.Trim() ?? "个";
            decimal.TryParse(row["单价"]?.ToString(), out var price);
            int.TryParse(row["库存"]?.ToString(), out var stock);
            int.TryParse(row["下限"]?.ToString(), out var min);

            // FluentValidation 逐行校验（与服务层同一套规则）
            var error = new PartInputValidator().FirstError(new PartInput
            {
                PartCode = code, PartName = name, CategoryName = cat, Unit = unit,
                Price = price, StockQty = stock, MinStock = min
            });
            if (error == null && !categories.Contains(cat)) error = $"类别「{cat}」不存在。";
            if (error == null && existing.Contains(code)) error = $"编码 {code} 已存在。";

            results.Add(new ImportRowResult(i + 2, code, name, error == null, error));   // +2：Excel 行号（1 表头 + 1 起）
        }
        return results;
    }

    /// <summary>确认导入（只写校验通过的行），返回成功/失败计数。</summary>
    public (int imported, int skipped) Import(string filePath)
    {
        var results = Validate(filePath);
        var table = ReadFile(filePath);
        var categories = _parts.Categories().ToList();
        var (imported, skipped) = (0, 0);

        for (int i = 0; i < results.Count; i++)
        {
            if (!results[i].Success) { skipped++; continue; }
            var row = table.Rows[i];
            var cat = categories.First(c => c.CategoryName == row["类别"]?.ToString()?.Trim());
            _parts.Save(new Part
            {
                PartCode = row["编码"]?.ToString()?.Trim() ?? "",
                PartName = row["名称"]?.ToString()?.Trim() ?? "",
                CategoryId = cat.CategoryId,
                Spec = string.IsNullOrWhiteSpace(row["规格"]?.ToString()) ? null : row["规格"]?.ToString()?.Trim(),
                Unit = string.IsNullOrWhiteSpace(row["单位"]?.ToString()) ? "个" : row["单位"]?.ToString()?.Trim(),
                Price = decimal.TryParse(row["单价"]?.ToString(), out var p) ? p : 0,
                StockQty = int.TryParse(row["库存"]?.ToString(), out var s) ? s : 0,
                MinStock = int.TryParse(row["下限"]?.ToString(), out var m) ? m : 0,
                Status = true
            });
            imported++;
        }
        return (imported, skipped);
    }

    /// <summary>生成导入模板 Excel（带示例行）。</summary>
    public string GenerateTemplate()
    {
        var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("配件导入");
        string[] headers = { "编码", "名称", "类别", "规格", "单位", "单价", "库存", "下限" };
        for (int j = 0; j < headers.Length; j++) ws.Cell(1, j + 1).Value = headers[j];
        ws.Cell(2, 1).Value = "P-ZC-100"; ws.Cell(2, 2).Value = "示例轴承6205";
        ws.Cell(2, 3).Value = "轴承类"; ws.Cell(2, 4).Value = "6205-2RS";
        ws.Cell(2, 5).Value = "个"; ws.Cell(2, 6).Value = 20; ws.Cell(2, 7).Value = 50; ws.Cell(2, 8).Value = 10;
        ws.Range(1, 1, 1, 8).Style.Font.Bold = true;
        ws.Range(1, 1, 1, 8).Style.Fill.BackgroundColor = XLColor.FromHtml("#202938");
        ws.Range(1, 1, 1, 8).Style.Font.FontColor = XLColor.White;
        ws.Columns().AdjustToContents();
        var file = Path.Combine(ExcelService.ExportDir, "配件导入模板.xlsx");
        wb.SaveAs(file);
        return file;
    }

    private static DataTable ReadFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".xlsx" or ".xlsm")
        {
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheets.First();
            var range = ws.RangeUsed();
            var t = new DataTable();
            for (int col = 1; col <= range.ColumnCount(); col++)
            {
                var header = ws.Cell(1, col).GetString().Trim();
                t.Columns.Add(header);
            }
            for (int row = 2; row <= range.RowCount(); row++)
            {
                var dr = t.NewRow();
                for (int col = 1; col <= range.ColumnCount(); col++)
                    dr[col - 1] = ws.Cell(row, col).GetString();
                t.Rows.Add(dr);
            }
            return t;
        }
        throw new NotSupportedException($"不支持的文件格式 {ext}（请用 .xlsx）");
    }
}
