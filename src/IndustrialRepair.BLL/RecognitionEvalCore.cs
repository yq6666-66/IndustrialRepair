using System;
using System.Collections.Generic;
using System.Linq;
using IndustrialRepair.Models;

namespace IndustrialRepair.BLL;

/// <summary>
/// 单图评测结果行。ExpectedPartCode 为空/空白 = 负样本（无登记目标）。
/// Top3Codes 为去重后的候选编码（按分数降序）。
/// </summary>
public record EvalRow(
    string Image, string? ExpectedPartCode, string? Top1PartCode, string? Top1Source,
    int Top1Score, IReadOnlyList<string> Top3Codes, bool Unmatched, double ElapsedMs);

/// <summary>评测汇总指标。分母固定为全部有效正样本（无有效预测也计入分母）。</summary>
public record EvalStats(
    int Positives, int Top1Correct, int Top3Hit, int Unmatched,
    int Negatives, int NegAccepted,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Confusion);

/// <summary>
/// 识别评测的纯计算部分（指标统计 + CSV 规整），供 RecognizeEval 工具与单元测试共用。
/// 当前引擎无拒识阈值：负样本"正确拒识"= 候选为空（Unmatched），有 Top1 即错误接受，如实统计。
/// </summary>
public static class RecognitionEvalCore
{
    /// <summary>由逐图行计算：Top1 正确率分子、Top3 命中分子、未匹配数、负样本错误接受数与混淆矩阵。</summary>
    public static EvalStats Compute(IEnumerable<EvalRow> rows)
    {
        int pos = 0, top1 = 0, top3 = 0, unm = 0, neg = 0, negAcc = 0;
        var conf = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.ExpectedPartCode))
            {
                neg++;
                if (!r.Unmatched && !string.IsNullOrWhiteSpace(r.Top1PartCode)) negAcc++;
                continue;
            }
            pos++;
            if (r.Unmatched || string.IsNullOrWhiteSpace(r.Top1PartCode))
            {
                unm++;
            }
            else
            {
                if (string.Equals(r.Top1PartCode, r.ExpectedPartCode, StringComparison.OrdinalIgnoreCase)) top1++;
                var key = r.ExpectedPartCode!;
                if (!conf.TryGetValue(key, out var row)) conf[key] = row = new(StringComparer.OrdinalIgnoreCase);
                row[r.Top1PartCode!] = row.TryGetValue(r.Top1PartCode!, out var c) ? c + 1 : 1;
            }
            if (r.Top3Codes.Any(c => string.Equals(c, r.ExpectedPartCode, StringComparison.OrdinalIgnoreCase)))
                top3++;
        }

        return new EvalStats(pos, top1, top3, unm, neg, negAcc,
            conf.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<string, int>)kv.Value));
    }

    /// <summary>汇总表行（指标名, 数值/比率），供控制台与 CSV 输出复用。</summary>
    public static List<(string Metric, string Value)> SummaryRows(EvalStats s)
    {
        static string Pct(int part, int whole) => whole == 0 ? "分母为0" : $"{part}/{whole} = {part * 100.0 / whole:F1}%";
        var rows = new List<(string, string)>
        {
            ("正样本数", s.Positives.ToString()),
            ("Top1 正确率", Pct(s.Top1Correct, s.Positives)),
            ("Top3 命中率", Pct(s.Top3Hit, s.Positives)),
            ("未匹配数", s.Unmatched.ToString()),
            ("负样本数", s.Negatives.ToString()),
            ("负样本错误接受数", s.NegAccepted.ToString()),
        };
        foreach (var (expected, row) in s.Confusion.OrderByDescending(kv => kv.Value.Values.Sum()))
            foreach (var (got, count) in row.OrderByDescending(kv => kv.Value))
                rows.Add((expected, $"→{got}×{count}"));
        return rows;
    }

    /// <summary>CSV 字段转义：含逗号/引号/换行时整体加引号，内部引号双写。</summary>
    public static string CsvEscape(string? field)
    {
        field ??= "";
        return field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r')
            ? $"\"{field.Replace("\"", "\"\"")}\""
            : field;
    }

    /// <summary>拼一行 CSV（字段已转义，换行符 \r\n）。</summary>
    public static string CsvLine(params string?[] fields) =>
        string.Join(",", fields.Select(CsvEscape)) + "\r\n";
}
