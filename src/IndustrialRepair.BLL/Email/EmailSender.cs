using System;
using System.IO;
using System.Text.Json;

namespace IndustrialRepair.BLL;

/// <summary>SMTP 选项（appsettings.json 的 "Smtp" 节；全部配置齐才启用真实发送）。</summary>
public sealed record SmtpOptions
{
    public string Host { get; init; } = "";
    public int Port { get; init; } = 465;
    public string User { get; init; } = "";
    public string Password { get; init; } = "";
    public string From { get; init; } = "";
    public bool Ssl { get; init; } = true;

    /// <summary>五项核心配置非空即视为已配置（Password 可为空字符串的本地中继除外场景不覆盖）。</summary>
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Host) && Port > 0 &&
        !string.IsNullOrWhiteSpace(User) && !string.IsNullOrWhiteSpace(From);

    /// <summary>从运行目录的 appsettings.json / appsettings.db.json 读取（与连接串同一组候选路径）。</summary>
    public static SmtpOptions? LoadFromConfig()
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
            Path.Combine(AppContext.BaseDirectory, "appsettings.db.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "appsettings.json")
        };
        foreach (var file in candidates)
        {
            if (!File.Exists(file)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (!doc.RootElement.TryGetProperty("Smtp", out var node) || node.ValueKind != JsonValueKind.Object)
                    continue;
                var o = new SmtpOptions
                {
                    Host = node.TryGetProperty("Host", out var h) ? h.GetString() ?? "" : "",
                    Port = node.TryGetProperty("Port", out var p) && p.TryGetInt32(out var port) ? port : 465,
                    User = node.TryGetProperty("User", out var u) ? u.GetString() ?? "" : "",
                    Password = node.TryGetProperty("Password", out var pw) ? pw.GetString() ?? "" : "",
                    From = node.TryGetProperty("From", out var f) ? f.GetString() ?? "" : "",
                    Ssl = !node.TryGetProperty("Ssl", out var s) || s.GetBoolean()
                };
                return o.IsValid ? o : null;
            }
            catch { /* 配置损坏按未配置处理 */ }
        }
        return null;
    }
}

/// <summary>邮件发送契约（R1：真实 SMTP 或演示回退，登录/找回共用）。</summary>
public interface IEmailSender
{
    /// <summary>SmtpOptions 已配置并可用为 true；演示模式恒 false。</summary>
    bool Enabled { get; }

    /// <summary>发送验证码邮件；失败抛异常由调用方按"发送失败回退演示"处理。</summary>
    void SendVerificationCode(string toEmail, string code, string purpose);
}

/// <summary>演示模式发送器：不真实发信（验证码由调用方回显，与历史行为一致）。</summary>
public sealed class DemoEmailSender : IEmailSender
{
    public bool Enabled => false;
    public void SendVerificationCode(string toEmail, string code, string purpose)
        => throw new InvalidOperationException("演示模式不发送邮件。");
}

/// <summary>MailKit 真实 SMTP 发送器（开源库 jstedfast/MailKit，Apache-2.0）。</summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;

    public SmtpEmailSender(SmtpOptions options) => _options = options;

    public bool Enabled => true;

    public void SendVerificationCode(string toEmail, string code, string purpose)
    {
        var message = new MimeKit.MimeMessage();
        message.From.Add(MimeKit.MailboxAddress.Parse(_options.From));
        message.To.Add(MimeKit.MailboxAddress.Parse(toEmail));
        message.Subject = $"【工业维修智能化零件分配平台】{purpose}验证码";
        message.Body = new MimeKit.TextPart("plain")
        {
            Text = $"您的{purpose}验证码：{code}\r\n\r\n验证码 10 分钟内有效，请勿透露给他人。\r\n" +
                   $"（本邮件由系统自动发送，请勿回复）\r\n{DateTime.Now:yyyy-MM-dd HH:mm:ss}"
        };

        using var client = new MailKit.Net.Smtp.SmtpClient();
        client.Timeout = 10_000;
        if (_options.Ssl)
            client.Connect(_options.Host, _options.Port, MailKit.Security.SecureSocketOptions.SslOnConnect);
        else
            client.Connect(_options.Host, _options.Port, MailKit.Security.SecureSocketOptions.StartTls);
        client.Authenticate(_options.User, _options.Password);
        client.Send(message);
        client.Disconnect(true);
    }
}

/// <summary>发送器工厂：Smtp 配置有效 → MailKit 真实发送；否则演示回退。</summary>
public static class EmailSenderFactory
{
    public static IEmailSender Create(SmtpOptions? options = null)
    {
        var o = options ?? SmtpOptions.LoadFromConfig();
        return o == null ? new DemoEmailSender() : new SmtpEmailSender(o);
    }
}
