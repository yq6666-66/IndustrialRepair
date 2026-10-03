using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace IndustrialRepair.BLL;

/// <summary>
/// CSV 读写统一入口（NR7，开源库 CsvHelper，MS-PL/Apache-2.0 双许可）。
/// 写：UTF-8 带 BOM（Excel/山海鲸直接打开）；读：支持 # 注释与缺列报错（含行号）。
/// 替换此前手拼转义——手拼实现已由 CsvEscape 单测覆盖，但统一走本类后由库保证 RFC4180。
/// </summary>
public static class CsvIo
{
    /// <summary>写记录集到文件（表头=字段名），返回文件路径。</summary>
    public static string WriteAll<T>(IEnumerable<T> rows, string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        using var writer = new StreamWriter(filePath, append: false, new UTF8Encoding(true));
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            NewLine = "\r\n",
        });
        csv.WriteRecords(rows);
        return filePath;
    }

    /// <summary>写字典行（列名→值），用于异构键的汇总表。</summary>
    public static string WriteRows(IEnumerable<IReadOnlyDictionary<string, object?>> rows, string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        using var writer = new StreamWriter(filePath, append: false, new UTF8Encoding(true));
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture) { NewLine = "\r\n" });

        bool headerWritten = false;
        foreach (var row in rows)
        {
            if (!headerWritten)
            {
                foreach (var key in row.Keys) csv.WriteField(key);
                csv.NextRecord();
                headerWritten = true;
            }
            foreach (var value in row.Values) csv.WriteField(value);
            csv.NextRecord();
        }
        return filePath;
    }

    /// <summary>
    /// 读标签清单（评测）：两列 image,expectedPartCode；支持 # 注释行；
    /// 缺表头/列数不对抛 CsvFormatException（含行号）；expected 空 = 负样本。
    /// </summary>
    public static List<(string Image, string? Expected)> ReadLabels(string filePath)
    {
        using var reader = new StreamReader(filePath, Encoding.UTF8);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Comment = '#',
            AllowComments = true,
            MissingFieldFound = null,
            HeaderValidated = null,
            TrimOptions = TrimOptions.Trim,
        });

        var labels = new List<(string, string?)>();
        csv.Read(); csv.ReadHeader();
        var header = csv.HeaderRecord!;
        if (!Array.Exists(header, h => h.Equals("image", StringComparison.OrdinalIgnoreCase)) ||
            !Array.Exists(header, h => h.StartsWith("expected", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("标签清单表头必须为 image,expectedPartCode");

        var expectedCol = Array.Find(header, h => h.StartsWith("expected", StringComparison.OrdinalIgnoreCase))!;
        while (csv.Read())
        {
            var img = csv.GetField("image")?.Trim() ?? "";
            if (img.Length == 0) continue;
            var exp = csv.GetField(expectedCol)?.Trim();
            labels.Add((img, string.IsNullOrEmpty(exp) ? null : exp));
        }
        return labels;
    }
}
