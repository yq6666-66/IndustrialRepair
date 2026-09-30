using System;
using System.Collections.Generic;

namespace IndustrialRepair.BLL;

/// <summary>BOM 规则行：来自 bom 表（QtyPer=单台设备维修常规用量），MinQty/StepQty 为可选约束。</summary>
public record BomRule(int PartId, decimal QtyPer, decimal MinQty = 0m, decimal StepQty = 0m);

/// <summary>算料结果行：向上取整后的建议领用数量。</summary>
public record BomRequirement(int PartId, decimal Qty);

/// <summary>
/// BOM 智能算料——纯计算函数，不访问数据库，可单元测试（规划：BomCalculator 纯逻辑）。
/// 计算规则：领用量 = Ceiling(单台用量 × 冗余系数)，不低于最小领用量，再按领用步长向上取整。
/// MinQty=0、StepQty=0 时与存储过程 usp_GeneratePicking 的 CEILING(QtyPer*@Redundancy)
/// 默认语义完全一致；存储过程仍是事务写入的权威路径，一致性由冒烟测试校验。
/// </summary>
public static class BomCalculator
{
    /// <summary>按规则计算建议领用量；规则为空时返回空列表（不默认为 0）。</summary>
    public static List<BomRequirement> Calculate(IEnumerable<BomRule> rules, decimal redundancy)
    {
        if (redundancy < 0.50m || redundancy > 3.00m)
            throw new ArgumentException("冗余系数应在 0.50 ~ 3.00 之间。", nameof(redundancy));

        var result = new List<BomRequirement>();
        foreach (var rule in rules)
        {
            decimal qty = Math.Ceiling(rule.QtyPer * redundancy);   // 基础语义：与存储过程一致
            if (qty < rule.MinQty) qty = rule.MinQty;               // 不低于最小领用量
            if (rule.StepQty > 0 && qty % rule.StepQty != 0)        // 按领用步长向上取整
                qty = Math.Ceiling(qty / rule.StepQty) * rule.StepQty;
            result.Add(new BomRequirement(rule.PartId, decimal.Round(qty, 2)));
        }
        return result;
    }
}
