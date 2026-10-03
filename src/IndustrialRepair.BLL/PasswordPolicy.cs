using IndustrialRepair.Models;

namespace IndustrialRepair.BLL;

/// <summary>
/// 口令强度策略（R5）：注册/修改/重置统一校验——至少 8 位且同时包含字母与数字。
/// 已有账号与演示播种不受影响（仅约束新设口令）。
/// </summary>
public static class PasswordPolicy
{
    public static Result Check(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            return Result.Fail("口令至少 8 位。");
        if (!ContainsLetter(password))
            return Result.Fail("口令必须同时包含字母和数字。");
        if (!ContainsDigit(password))
            return Result.Fail("口令必须同时包含字母和数字。");
        return Result.Ok();
    }

    private static bool ContainsLetter(string s)
    {
        foreach (var c in s) if (char.IsLetter(c)) return true;
        return false;
    }

    private static bool ContainsDigit(string s)
    {
        foreach (var c in s) if (char.IsDigit(c)) return true;
        return false;
    }
}
