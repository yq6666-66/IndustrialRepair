using System.Text.Json;
using IndustrialRepair.BLL;
using Xunit;

namespace IndustrialRepair.Tests;

/// <summary>邮件发送器选择逻辑（R1）：Smtp 配置有效性 / 工厂回退演示 / 演示模式不可发送。</summary>
public class EmailSenderTests
{
    [Theory]
    [InlineData("smtp.qq.com", 465, "user@qq.com", "from@qq.com", true)]
    [InlineData("smtp.exmail.com", 587, "svc@ex.com", "svc@ex.com", true)]
    public void 配置完整视为有效(string host, int port, string user, string from, bool expected)
    {
        var o = new SmtpOptions { Host = host, Port = port, User = user, From = from };
        Assert.Equal(expected, o.IsValid);
    }

    [Theory]
    [InlineData("", 465, "u@x.com", "f@x.com", false)]   // 缺 Host
    [InlineData("h", 0, "u@x.com", "f@x.com", false)]    // 端口非法
    [InlineData("h", 465, "", "f@x.com", false)]         // 缺 User
    [InlineData("h", 465, "u@x.com", "", false)]         // 缺 From
    public void 配置缺失视为无效(string host, int port, string user, string from, bool expected)
    {
        var o = new SmtpOptions { Host = host, Port = port, User = user, From = from };
        Assert.Equal(expected, o.IsValid);
    }

    [Fact]
    public void 未配置回退演示发送器()
    {
        var sender = EmailSenderFactory.Create(null);   // 无配置 → 演示
        Assert.False(sender.Enabled);
        Assert.IsType<DemoEmailSender>(sender);
    }

    [Fact]
    public void 有效配置创建SMTP发送器()
    {
        var sender = EmailSenderFactory.Create(new SmtpOptions { Host = "smtp.qq.com", Port = 465, User = "a@qq.com", From = "a@qq.com" });
        Assert.True(sender.Enabled);
        Assert.IsType<SmtpEmailSender>(sender);
    }

    [Fact]
    public void 演示发送器拒绝真实发送()
    {
        var sender = new DemoEmailSender();
        Assert.Throws<InvalidOperationException>(() => sender.SendVerificationCode("x@x.com", "123456", "测试"));
    }
}
