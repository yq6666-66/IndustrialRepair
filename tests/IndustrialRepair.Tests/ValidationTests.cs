using FluentValidation;
using IndustrialRepair.BLL;
using Xunit;

namespace IndustrialRepair.Tests;

/* ============ NR6：集中校验器（FluentValidation） ============ */

public class PartInputValidatorTests
{
    private static PartInput Valid() => new()
    { PartCode = "P-ZC-010", PartName = "测试轴承", CategoryName = "轴承类", Unit = "个", Price = 10m, StockQty = 5, MinStock = 2 };

    [Fact]
    public void 合法输入通过() => Assert.Null(new PartInputValidator().FirstError(Valid()));

    [Theory]
    [InlineData("")]                    // 空
    [InlineData("p-zc-001")]            // 小写
    [InlineData("P-ZC-1")]              // 位数不足
    [InlineData("P-Z1-001")]            // 第二位非字母
    [InlineData("X-ZC-001")]            // 前缀错误
    public void 编码格式不合法被拒(string code) =>
        Assert.NotNull(new PartInputValidator().FirstError(Valid() with { PartCode = code }));

    [Fact]
    public void 负库存被拒() =>
        Assert.Contains("库存", new PartInputValidator().FirstError(Valid() with { StockQty = -1 }));

    [Fact]
    public void 未选类别被拒() =>
        Assert.Contains("类别", new PartInputValidator().FirstError(Valid() with { CategoryName = "" }));
}

public class DeviceInputValidatorTests
{
    [Fact]
    public void 合法设备通过() =>
        Assert.Null(new DeviceInputValidator().FirstError(new DeviceInput { DeviceCode = "DEV-009", DeviceName = "新设备" }));

    [Theory]
    [InlineData("DEV-9")]
    [InlineData("dev-009")]
    [InlineData("DEV-0099")]
    [InlineData("")]
    public void 设备编码格式被拒(string code) =>
        Assert.NotNull(new DeviceInputValidator().FirstError(new DeviceInput { DeviceCode = code, DeviceName = "新设备" }));

    [Fact]
    public void 名称必填() =>
        Assert.NotNull(new DeviceInputValidator().FirstError(new DeviceInput { DeviceCode = "DEV-009", DeviceName = "" }));
}

public class RegisterInputValidatorTests
{
    private static RegisterInput Valid() => new()
    { UserName = "stu01", Password = "test1234", RealName = "学生" };

    [Fact]
    public void 合法注册通过() => Assert.Null(new RegisterInputValidator().FirstError(Valid()));

    [Theory]
    [InlineData("ab", "test1234", "名")]                       // 用户名太短
    [InlineData("stu01", "abc123", "名")]                      // 口令弱
    [InlineData("stu01", "test1234", "")]                      // 缺姓名
    [InlineData("stu01", "test1234", "名", "12345")]           // 手机号格式
    [InlineData("stu01", "test1234", "名", null, "bad-email")] // 邮箱格式
    public void 非法注册被拒(string user, string pwd, string real, string? phone = null, string? email = null) =>
        Assert.NotNull(new RegisterInputValidator().FirstError(
            new RegisterInput { UserName = user, Password = pwd, RealName = real, Phone = phone, Email = email }));

    [Fact]
    public void 手机与邮箱留空允许() =>
        Assert.Null(new RegisterInputValidator().FirstError(Valid() with { Phone = null, Email = null }));
}

public class RedundancyValidatorTests
{
    [Theory]
    [InlineData(1.00, true)]
    [InlineData(1.10, true)]
    [InlineData(2.00, true)]
    [InlineData(0.99, false)]
    [InlineData(2.01, false)]
    public void 冗余系数范围(decimal value, bool ok) =>
        Assert.Equal(ok, new RedundancyValidator().FirstError(value) == null);
}
