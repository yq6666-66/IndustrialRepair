using System;
using System.Security.Cryptography;
using System.Text;

namespace IndustrialRepair.BLL;

/// <summary>
/// 口令安全：bcrypt 哈希存库（任务书要求 bcrypt/SHA256）。
/// SHA-256 仅用于会话令牌指纹等辅助场景，登录口令一律 bcrypt。
/// </summary>
public static class PasswordHasher
{
    /// <summary>生成 bcrypt 哈希（cost=11，登录耗时约 100ms，安全与体验兼顾）</summary>
    public static string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, workFactor: 11);

    public static bool Verify(string password, string hash) =>
        BCrypt.Net.BCrypt.Verify(password, hash);

    /// <summary>SHA-256 十六进制（令牌/校验摘要用）</summary>
    public static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
