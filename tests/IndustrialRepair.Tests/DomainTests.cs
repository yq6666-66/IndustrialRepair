using IndustrialRepair.BLL;
using Xunit;

namespace IndustrialRepair.Tests;

/// <summary>口令哈希测试：bcrypt 随机盐、可验证、错口令拒绝。</summary>
public class PasswordHasherTests
{
    [Fact]
    public void 哈希不等于明文()
    {
        var hash = PasswordHasher.Hash("123456");
        Assert.NotEqual("123456", hash);
        Assert.StartsWith("$2", hash);          // bcrypt 格式
    }

    [Fact]
    public void 正确口令可验证_错误口令拒绝()
    {
        var hash = PasswordHasher.Hash("S3cret!");
        Assert.True(PasswordHasher.Verify("S3cret!", hash));
        Assert.False(PasswordHasher.Verify("wrong", hash));
    }

    [Fact]
    public void 同一口令两次哈希不同_随机盐生效()
    {
        Assert.NotEqual(PasswordHasher.Hash("abc123"), PasswordHasher.Hash("abc123"));
    }
}

/// <summary>扫码协议解析测试：串口/模拟器/手动录入共用的帧 → 编码规则。</summary>
public class ScanProtocolTests
{
    [Theory]
    [InlineData("PART:P-ZC-001", "P-ZC-001")]
    [InlineData("part:p-mf-001", "p-mf-001")]     // 前缀大小写不敏感
    [InlineData("  PART:P-DQ-001  ", "P-DQ-001")] // 容忍首尾空白
    public void 标准帧解析(string frame, string expected)
    {
        Assert.Equal(expected, SerialPortService.ParsePartCode(frame));
    }

    [Theory]
    [InlineData("HELLO")]
    [InlineData("")]
    public void 非编码帧忽略(string frame)
    {
        Assert.Null(SerialPortService.ParsePartCode(frame));
    }
}
