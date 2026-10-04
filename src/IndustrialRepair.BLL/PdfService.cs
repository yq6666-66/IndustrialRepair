using System;
using System.Data;
using System.IO;
using IndustrialRepair.DAL;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace IndustrialRepair.BLL;

/// <summary>
/// PDF 导出（开源库 QuestPDF，Community 许可——营收低于 100 万美元场景免费）。
/// 沿用项目「导出返回文件路径」约定；输出目录与 ExcelService 一致。
/// </summary>
public static class PdfService
{
    static PdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseEnvironmentFonts = true;   // 2026.9+ 默认不读系统字体，需显式启用（微软雅黑）
    }

    /// <summary>导出目录（与 ExcelService.ExportDir 相同位置）。</summary>
    public static string ExportDir
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IndustrialRepair", "exports");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>领料单 PDF：抬头（单号/工单/设备/生成人/冗余）+ 明细表 + 合计 + 单号二维码。</summary>
    public static string ExportPickingPdf(int pickingId)
    {
        var head = DbHelper.GetTable(
            @"SELECT pl.PickingNo, o.OrderNo, d.DeviceName, pl.GeneratedBy, pl.Redundancy,
                     pl.TotalAmount, pl.Status, CONVERT(NVARCHAR(16), pl.CreatedAt, 120) AS CreatedAt
              FROM dbo.picking_list pl
              JOIN dbo.work_order o ON o.OrderId = pl.OrderId
              JOIN dbo.device d ON d.DeviceId = o.DeviceId
              WHERE pl.PickingId = @Id", CommandType.Text, DbHelper.P("@Id", pickingId));
        if (head.Rows.Count == 0) throw new InvalidOperationException("领料单不存在。");
        var h = head.Rows[0];
        var items = DbHelper.GetTable(
            @"SELECT PartCode, PartName, Qty, UnitPrice, Amount, ISNULL(Remark, '') AS Remark
              FROM dbo.picking_item WHERE PickingId = @Id ORDER BY ItemId",
            CommandType.Text, DbHelper.P("@Id", pickingId));

        return Write($"领料单_{h["PickingNo"]}", doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontFamily("微软雅黑").FontSize(10.5f));

                page.Header().Column(col =>
                {
                    col.Item().AlignCenter().Text("工业维修智能化零件分配平台 · 领料单")
                        .FontSize(16).Bold().FontColor("#296CCA");
                    col.Item().PaddingTop(8).Row(r =>
                    {
                        r.RelativeItem().Text(t =>
                        {
                            t.Span($"单号：{h["PickingNo"]}").SemiBold();
                            t.Line($"工单：{h["OrderNo"]}    设备：{h["DeviceName"]}");
                            t.Line($"生成人：{h["GeneratedBy"]}    冗余系数：{h["Redundancy"]}    生成时间：{h["CreatedAt"]}");
                        });
                        r.ConstantItem(72).Image(BuildQr($"PICK:{h["PickingNo"]}"));
                    });
                });

                page.Content().PaddingVertical(10).Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(28);   // #
                        c.RelativeColumn(2);    // 编码
                        c.RelativeColumn(3);    // 名称
                        c.ConstantColumn(44);   // 数量
                        c.RelativeColumn(1.4f); // 单价
                        c.RelativeColumn(1.4f); // 金额
                        c.RelativeColumn(2);    // 备注
                    });
                    t.Header(header =>
                    {
                        foreach (var cap in new[] { "#", "编码", "名称", "数量", "单价", "金额", "备注" })
                            header.Cell().Border(0.5f).Background("#202938")
                                .Padding(4).Text(cap).FontColor("#FFFFFF").SemiBold();
                    });
                    var i = 0;
                    foreach (DataRow r in items.Rows)
                    {
                        i++;
                        t.Cell().Border(0.5f).Padding(4).Text(i.ToString());
                        t.Cell().Border(0.5f).Padding(4).Text(r["PartCode"].ToString()!);
                        t.Cell().Border(0.5f).Padding(4).Text(r["PartName"].ToString()!);
                        t.Cell().Border(0.5f).Padding(4).Text(r["Qty"].ToString()!);
                        t.Cell().Border(0.5f).Padding(4).Text($"{Convert.ToDecimal(r["UnitPrice"]):N2}");
                        t.Cell().Border(0.5f).Padding(4).Text($"{Convert.ToDecimal(r["Amount"]):N2}");
                        t.Cell().Border(0.5f).Padding(4).Text(r["Remark"].ToString()!);
                    }
                    t.Cell().ColumnSpan(5).Border(0.5f).Background("#F2F6FA")
                        .Padding(4).AlignRight().Text("合计金额（元）").SemiBold();
                    t.Cell().Border(0.5f).Background("#F2F6FA").Padding(4)
                        .Text($"{Convert.ToDecimal(h["TotalAmount"]):N2}").SemiBold();
                    t.Cell().Border(0.5f).Background("#F2F6FA").Padding(4).Text("");
                });

                page.Footer().Row(fr =>
                {
                    fr.RelativeItem().Text($"状态：{h["Status"]}    共 {items.Rows.Count} 项材料").FontSize(9);
                    fr.RelativeItem().AlignRight()
                        .Text($"打印时间 {DateTime.Now:yyyy-MM-dd HH:mm}").FontSize(9);
                });
            });
        });
    }

    /// <summary>通用表格 PDF（配件库存清单等；列头用表内列名）。</summary>
    public static string ExportTablePdf(string title, DataTable table, string? qrText = null)
    {
        return Write(title, doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontFamily("微软雅黑").FontSize(9.5f));

                page.Header().Row(r =>
                {
                    r.RelativeItem().Text(title).FontSize(15).Bold().FontColor("#296CCA");
                    if (qrText != null) r.ConstantItem(56).Image(BuildQr(qrText));
                });

                page.Content().PaddingVertical(8).Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        foreach (DataColumn col in table.Columns) c.RelativeColumn();
                    });
                    t.Header(header =>
                    {
                        foreach (DataColumn col in table.Columns)
                            header.Cell().Border(0.5f).Background("#202938")
                                .Padding(3).Text(col.ColumnName).FontColor("#FFFFFF").SemiBold();
                    });
                    foreach (DataRow row in table.Rows)
                        foreach (DataColumn col in table.Columns)
                            t.Cell().Border(0.5f).Padding(3)
                                .Text(row[col] == DBNull.Value ? "" : row[col].ToString()!);
                });

                page.Footer().AlignRight().Text($"共 {table.Rows.Count} 行 · 打印时间 {DateTime.Now:yyyy-MM-dd HH:mm}").FontSize(9);
            });
        });
    }

    /// <summary>批量配件二维码标签 PDF（R6：A4 排版，每行 3 列×5 行 = 15 张/页）。</summary>
    public static string ExportPartsLabelPdf(System.Data.DataTable parts)
    {
        return Write($"配件标签批量_{DateTime.Now:yyyyMMdd_HHmmss}", doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(15);
                page.DefaultTextStyle(t => t.FontFamily("微软雅黑").FontSize(8.5f));

                page.Header().AlignCenter()
                    .Text($"配件二维码标签（共 {parts.Rows.Count} 张）· 打印时间 {DateTime.Now:yyyy-MM-dd HH:mm}")
                    .FontSize(10).SemiBold().FontColor("#296CCA");

                page.Content().PaddingVertical(8).Column(col =>
                {
                    var i = 0;
                    while (i < parts.Rows.Count)
                    {
                        var row = parts.Rows[i];
                        var remaining = parts.Rows.Count - i;
                        var cols = Math.Min(3, remaining);

                        col.Item().Row(r =>
                        {
                            for (int j = 0; j < cols; j++)
                            {
                                var p = parts.Rows[i + j];
                                var code = p["PartCode"].ToString()!;
                                var name = p["PartName"].ToString()!;

                                r.RelativeItem().Border(0.5f).Padding(6).Column(label =>
                                {
                                    label.Item().AlignCenter().Height(80)
                                        .Image(BuildQr($"PART:{code}"));
                                    label.Item().PaddingTop(4).AlignCenter()
                                        .Text($"{code}").FontSize(9).Bold();
                                    label.Item().AlignCenter()
                                        .Text($"{name}").FontSize(8);
                                });
                            }
                            // 填充空列保持行宽一致
                            for (int j = cols; j < 3; j++) r.RelativeItem().Border(0.5f);
                        });
                        col.Item().Height(8);   // 行间距
                        i += cols;
                    }
                });

                page.Footer().AlignRight()
                    .Text($"A4 纵向 · 3×5 标签/页 · 建议使用标签纸打印")
                    .FontSize(8);
            });
        });
    }

    /// <summary>生成二维码 PNG 字节（复用 QRCoder）。</summary>
    private static byte[] BuildQr(string content)
    {
        using var gen = new QRCoder.QRCodeGenerator();
        using var data = gen.CreateQrCode(content, QRCoder.QRCodeGenerator.ECCLevel.M);
        using var qr = new QRCoder.PngByteQRCode(data);
        return qr.GetGraphic(4);
    }

    private static string Write(string title, Action<IDocumentContainer> compose)
    {
        var safe = string.Join("_", title.Split(Path.GetInvalidFileNameChars()));
        var file = Path.Combine(ExportDir, $"{safe}_{DateTime.Now:HHmmss}.pdf");
        Document.Create(compose).GeneratePdf(file);
        return file;
    }
}
