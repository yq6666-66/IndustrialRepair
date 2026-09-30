using IndustrialRepair.Models;

namespace IndustrialRepair.BLL;

/// <summary>
/// 服务层操作守卫（M0：写入入口统一校验）。
/// 规则：界面按钮显隐只是第一道防线，所有写操作在业务层再校验
/// 「有效登录会话 + 对应权限 + 操作者与当前会话一致」；
/// 工程师（有 order.handle 而无 order.assign 者）只能操作获派给自己的工单，
/// 管理员/调度（order.assign）与库管按各自权限放行。
/// </summary>
internal static class Guard
{
    /// <summary>要求已登录且拥有指定权限</summary>
    public static Result Session(string perm)
    {
        var s = AppSession.Current;
        if (s == null) return Result.Fail("未登录或会话已过期，请重新登录。");
        if (!s.HasPerm(perm)) return Result.Fail($"权限不足：该操作需要 {perm}。");
        return Result.Ok();
    }

    /// <summary>页面传入的操作者必须与当前会话一致（不信任界面传参的身份）</summary>
    public static Result SameOperator(int operatorId)
    {
        var s = AppSession.Current;
        if (s == null) return Result.Fail("未登录或会话已过期，请重新登录。");
        if (s.UserId != operatorId) return Result.Fail("操作人与当前登录会话不一致，已拒绝。");
        return Result.Ok();
    }

    /// <summary>页面传入的操作人名必须与当前会话一致</summary>
    public static Result SameOperator(string operatorName)
    {
        var s = AppSession.Current;
        if (s == null) return Result.Fail("未登录或会话已过期，请重新登录。");
        if (s.UserName != operatorName) return Result.Fail("操作人与当前登录会话不一致，已拒绝。");
        return Result.Ok();
    }

    /// <summary>
    /// 工单归属校验：工程师（order.handle 且无 order.assign）只能操作获派工单；
    /// 携带 order.assign（管理员/调度）或其他岗位（库管）按其权限放行。
    /// </summary>
    public static Result OrderOperator(WorkOrder order)
    {
        var s = AppSession.Current!;
        if (s.HasPerm("order.handle") && !s.HasPerm("order.assign") && order.EngineerId != s.UserId)
            return Result.Fail("仅获派工程师或管理员可操作该工单。");
        return Result.Ok();
    }
}
