using System;
using IndustrialRepair.Models;

namespace IndustrialRepair.BLL;

/// <summary>
/// 登录会话管理：令牌 + 滑动超时（默认 30 分钟无操作自动登出）。
/// </summary>
public static class AppSession
{
    public const int TimeoutMinutes = 30;

    private static readonly object Lock = new();
    public static UserInfo? Current { get; private set; }
    public static Guid Token { get; private set; }
    public static DateTime LastActive { get; private set; } = DateTime.MinValue;

    public static bool IsLoggedIn => Current != null;

    public static void Begin(UserInfo user)
    {
        lock (Lock)
        {
            Current = user;
            Token = Guid.NewGuid();
            LastActive = DateTime.Now;
        }
    }

    /// <summary>活动心跳：超时返回 false（UI 定时器调用，到期自动登出）</summary>
    public static bool Touch()
    {
        lock (Lock)
        {
            if (Current == null) return false;
            if ((DateTime.Now - LastActive).TotalMinutes > TimeoutMinutes)
            {
                Current = null;
                return false;
            }
            LastActive = DateTime.Now;
            return true;
        }
    }

    public static void Logout()
    {
        lock (Lock) { Current = null; LastActive = DateTime.MinValue; }
    }

    public static bool HasPerm(string perm) => Current?.HasPerm(perm) == true;
}
