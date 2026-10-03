using System;
using System.Data.SqlClient;
using System.IO;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;

namespace IndustrialRepair.BLL;

/// <summary>
/// 认证与账号安全：登录（验证码→口令→可选MFA→锁定策略）、注册、
/// 找回密码（模拟邮箱验证码）、修改密码、会话令牌。
/// 说明：CSRF 主要针对 Web 会话；C/S 场景以“会话令牌+操作日志”等价落实，
/// 所有口令 bcrypt 哈希存储，登录失败 5 次锁定 10 分钟。
/// </summary>
public class AuthService
{
    private readonly UserRepository _users = new();
    private readonly IEmailSender _mailer;

    public const int MaxFail = 5;
    public const int LockMinutes = 10;

    /// <summary>默认构造（生产）：按 appsettings 的 Smtp 节选择真实/演示发送器；测试可注入替代。</summary>
    public AuthService(IEmailSender? mailer = null) => _mailer = mailer ?? EmailSenderFactory.Create();

    /// <summary>发送验证码：SMTP 启用且邮箱非空 → 真实发信返回 null（不回显）；否则演示回退返回验证码文本。</summary>
    private string? SendCode(string? email, string code, string purpose)
    {
        if (_mailer.Enabled && !string.IsNullOrWhiteSpace(email))
        {
            try
            {
                _mailer.SendVerificationCode(email, code, purpose);
                return null;
            }
            catch (Exception ex)
            {
                FileLog.Write($"邮件发送失败（回退演示回显）：{ex.Message}");
            }
        }
        return code;
    }

    /// <summary>登录第一步：用户名口令（前置条件：图形验证码已通过）</summary>
    public Result<UserInfo> Login(string userName, string password, string ip = "127.0.0.1")
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            return Result<UserInfo>.Fail("请输入用户名和密码。");

        var user = _users.GetByUserName(userName);
        if (user == null)
        {
            _users.WriteLoginLog(userName, null, false, "用户不存在", ip);
            return Result<UserInfo>.Fail("用户名或密码错误。");
        }
        if (!user.Status)
        {
            _users.WriteLoginLog(userName, user.UserId, false, "账号已禁用", ip);
            return Result<UserInfo>.Fail("账号已被禁用，请联系管理员。");
        }
        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.Now)
        {
            var left = (int)Math.Ceiling((user.LockedUntil.Value - DateTime.Now).TotalMinutes);
            _users.WriteLoginLog(userName, user.UserId, false, "账号锁定中", ip);
            return Result<UserInfo>.Fail($"失败次数过多，账号锁定中，请约 {left} 分钟后重试。");
        }

        var hash = _users.GetPasswordHash(user.UserId) ?? "";
        if (!PasswordHasher.Verify(password, hash))
        {
            _users.UpdateLoginState(user.UserId, false);
            _users.WriteLoginLog(userName, user.UserId, false, "密码错误", ip);
            var failLeft = MaxFail - (user.FailCount + 1);
            return Result<UserInfo>.Fail(failLeft > 0
                ? $"用户名或密码错误，剩余尝试 {failLeft} 次。"
                : $"连续失败 {MaxFail} 次，账号锁定 {LockMinutes} 分钟。");
        }

        if (user.MfaEnabled)
        {
            // 多因素认证：生成 6 位验证码发送到邮箱（SMTP 未配置时演示回退直接回传显示）
            var code = new Random().Next(100000, 999999).ToString();
            _users.SetMfa(user.UserId, true, code);
            _users.WriteLoginLog(userName, user.UserId, false, "等待二次验证", ip);
            var echo = SendCode(user.Email, code, "登录二次验证");
            return Result<UserInfo>.Fail($"MFA_REQUIRED|{echo}|{user.Email ?? "user@example.com"}");
        }

        return FinishLogin(user, ip);
    }

    /// <summary>MFA 二次验证</summary>
    public Result<UserInfo> VerifyMfa(string userName, string code, string ip = "127.0.0.1")
    {
        var user = _users.GetByUserName(userName);
        if (user == null) return Result<UserInfo>.Fail("用户不存在。");
        if (string.IsNullOrWhiteSpace(user.MfaCode) || user.MfaCode != code.Trim())
        {
            _users.WriteLoginLog(userName, user.UserId, false, "二次验证码错误", ip);
            return Result<UserInfo>.Fail("验证码错误。");
        }
        _users.SetMfa(user.UserId, true, null); // 验证码一次性作废
        return FinishLogin(user, ip);
    }

    private Result<UserInfo> FinishLogin(UserInfo user, string ip)
    {
        _users.UpdateLoginState(user.UserId, true);
        user.FailCount = 0;
        user.Permissions = _users.GetPermissions(user.UserId);
        user.LastLoginTime = DateTime.Now;
        _users.WriteLoginLog(user.UserName, user.UserId, true, "登录成功", ip);
        AppSession.Begin(user);
        _users.SaveSession(user.UserId, AppSession.Token, DateTime.Now);
        _users.WriteOpLog(user.UserId, user.UserName, "系统", "登录", $"登录成功，会话令牌 {AppSession.Token:N}");
        return Result<UserInfo>.Ok(user, $"欢迎，{user.RealName}（{user.RoleName}）");
    }

    /// <summary>注册（默认“报修人”角色；用户+日志在同一 C# 事务中写入）</summary>
    public Result Register(string userName, string password, string realName, string? phone, string? email)
    {
        // NR6：FluentValidation 集中校验（含口令策略/手机号/邮箱格式，UI 与服务共用）
        var invalid = new RegisterInputValidator().FirstError(
            new RegisterInput { UserName = userName, Password = password, RealName = realName, Phone = phone, Email = email });
        if (invalid != null) return Result.Fail(invalid);
        if (_users.GetByUserName(userName) != null) return Result.Fail("用户名已存在。");

        var roles = _users.ListRoles();
        var reporter = roles.Find(r => r.RoleCode == "Reporter")
                      ?? throw new InvalidOperationException("角色数据未初始化");
        try
        {
            _users.InsertUserWithLog(new UserInfo
            {
                UserName = userName, RealName = realName, Phone = phone, Email = email, RoleId = reporter.RoleId
            }, PasswordHasher.Hash(password), "127.0.0.1");
            return Result.Ok("注册成功，请使用新账号登录。");
        }
        catch (Exception ex) { return Result.Fail(DbHelper.FriendlyError(ex)); }
    }

    /// <summary>找回密码第一步：校验用户名+邮箱，生成验证码（模拟发送）</summary>
    public Result<string> RequestResetCode(string userName, string email)
    {
        var user = _users.GetByUserName(userName);
        if (user == null || !string.Equals(user.Email?.Trim(), email?.Trim(), StringComparison.OrdinalIgnoreCase))
            return Result<string>.Fail("用户名与邮箱不匹配。");
        var code = new Random().Next(100000, 999999).ToString();
        _users.SetMfa(user.UserId, user.MfaEnabled, code);
        var echo = SendCode(email, code, "找回密码");
        return Result<string>.Ok(echo ?? "",
            echo == null ? $"验证码已发送至 {email}" : $"验证码已生成（演示环境直接显示）");
    }

    /// <summary>找回密码第二步：验证码 + 新密码</summary>
    public Result ResetPassword(string userName, string code, string newPassword)
    {
        var user = _users.GetByUserName(userName);
        if (user == null) return Result.Fail("用户不存在。");
        if (string.IsNullOrWhiteSpace(user.MfaCode) || user.MfaCode != code.Trim())
            return Result.Fail("验证码错误或已失效。");
        var resetPolicy = PasswordPolicy.Check(newPassword);
        if (!resetPolicy.Success) return resetPolicy;
        _users.UpdatePassword(user.UserId, PasswordHasher.Hash(newPassword));
        _users.SetMfa(user.UserId, user.MfaEnabled, null);
        _users.WriteOpLog(user.UserId, userName, "系统", "找回密码", "通过邮箱验证码重置");
        return Result.Ok("密码已重置，请使用新密码登录。");
    }

    /// <summary>修改密码</summary>
    public Result ChangePassword(int userId, string oldPassword, string newPassword)
    {
        var hash = _users.GetPasswordHash(userId) ?? "";
        if (!PasswordHasher.Verify(oldPassword, hash)) return Result.Fail("原密码错误。");
        var changePolicy = PasswordPolicy.Check(newPassword);
        if (!changePolicy.Success) return changePolicy;
        _users.UpdatePassword(userId, PasswordHasher.Hash(newPassword));
        return Result.Ok("密码修改成功。");
    }

    /// <summary>开启/关闭 MFA</summary>
    public Result ToggleMfa(int userId, bool enabled)
    {
        _users.SetMfa(userId, enabled, null);
        return Result.Ok(enabled ? "多因素认证已开启：登录时需输入邮箱验证码。" : "多因素认证已关闭。");
    }

    public void Logout()
    {
        if (AppSession.Current != null)
        {
            _users.ClearSession(AppSession.Current.UserId);
            _users.WriteOpLog(AppSession.Current.UserId, AppSession.Current.UserName, "系统", "登出", "会话结束");
        }
        AppSession.Logout();
    }
}
