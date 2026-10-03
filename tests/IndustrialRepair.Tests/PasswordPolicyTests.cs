using IndustrialRepair.BLL;
using Xunit;

namespace IndustrialRepair.Tests;

/// <summary>口令强度策略（R5）：注册/修改/重置统一入口的校验规则。</summary>
public class PasswordPolicyTests
{
    [Theory]
    [InlineData("", false)]          // 空
    [InlineData("abc123", false)]    // 太短
    [InlineData("12345678", false)]  // 纯数字
    [InlineData("abcdefgh", false)]  // 纯字母
    [InlineData("abc12345", true)]   // 字母+数字 ≥8
    [InlineData("A1b2c3d4", true)]   // 混合大小写
    [InlineData("repair2026!", true)] // 含符号也允许（不禁止）
    public void 策略校验(string pwd, bool expected)
    {
        Assert.Equal(expected, PasswordPolicy.Check(pwd).Success);
    }

    [Fact]
    public void 过短时提示位数()
    {
        var r = PasswordPolicy.Check("ab12");
        Assert.Contains("8", r.Message);
    }

    [Fact]
    public void 缺字母或数字时提示组合()
    {
        Assert.Contains("字母", PasswordPolicy.Check("12345678").Message);
        Assert.Contains("字母", PasswordPolicy.Check("abcdefgh").Message);
    }
}
