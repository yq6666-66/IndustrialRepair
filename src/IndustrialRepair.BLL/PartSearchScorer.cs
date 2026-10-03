using System;
using System.Collections.Generic;
using System.Linq;
using FuzzySharp;
using IndustrialRepair.Models;

namespace IndustrialRepair.BLL;

/// <summary>
/// 配件模糊搜索（NR10，开源库 FuzzySharp，MIT）：精确 LIKE 无命中时自动回退
/// Levenshtein 编辑距离匹配（编码/名称/规格三字段加权），阈值 60。
/// </summary>
public static class PartSearchScorer
{
    public const int Threshold = 60;

    /// <summary>纯函数评分（可单测）：编码用全串 Ratio，名称/规格用 PartialRatio（子串最优）。</summary>
    public static int Score(string keyword, Part part)
    {
        if (string.IsNullOrWhiteSpace(keyword) || part == null) return 0;
        keyword = keyword.Trim();

        var codeScore = Fuzz.Ratio(keyword, part.PartCode ?? "");
        var nameScore = Fuzz.PartialRatio(keyword, part.PartName ?? "");
        var specScore = string.IsNullOrWhiteSpace(part.Spec) ? 0 : Fuzz.PartialRatio(keyword, part.Spec);

        return Math.Max(codeScore, Math.Max(nameScore, specScore));
    }

    /// <summary>对配件列表评分排序（降序），返回 (分, 配件) 对；仅保留阈值以上。</summary>
    public static List<(int Score, Part Part)> Rank(string keyword, IEnumerable<Part> parts, int threshold = Threshold)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return new List<(int, Part)>();
        return parts.Select(p => (Score(keyword, p), p))
                    .Where(x => x.Item1 >= threshold)
                    .OrderByDescending(x => x.Item1)
                    .ToList();
    }
}
