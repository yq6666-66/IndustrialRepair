using System.Text.RegularExpressions;
using FluentValidation;
using IndustrialRepair.Models;

namespace IndustrialRepair.BLL;

/* =====================================================================
   NR6：集中输入校验（开源库 FluentValidation，Apache-2.0）
   UI 表单与 BLL 服务共用同一套规则，消除两边各写一份的漂移。
   ===================================================================== */

/// <summary>配件新增/编辑输入。</summary>
public sealed record PartInput
{
    public string PartCode { get; init; } = "";
    public string PartName { get; init; } = "";
    public string? CategoryName { get; init; }
    public string? Spec { get; init; }
    public string Unit { get; init; } = "个";
    public decimal Price { get; init; }
    public int StockQty { get; init; }
    public int MinStock { get; init; }
}

/// <summary>设备新增/编辑输入。</summary>
public sealed record DeviceInput
{
    public string DeviceCode { get; init; } = "";
    public string DeviceName { get; init; } = "";
    public string? Location { get; init; }
    public bool Status { get; init; } = true;
}

/// <summary>注册输入（与 PasswordPolicy 组合）。</summary>
public sealed record RegisterInput
{
    public string UserName { get; init; } = "";
    public string Password { get; init; } = "";
    public string RealName { get; init; } = "";
    public string? Phone { get; init; }
    public string? Email { get; init; }
}

public sealed class PartInputValidator : AbstractValidator<PartInput>
{
    private static readonly Regex CodePattern = new(@"^P-[A-Z]{2}-\d{3}$", RegexOptions.Compiled);

    public PartInputValidator()
    {
        RuleFor(x => x.PartCode).NotEmpty().WithMessage("配件编码不能为空。")
            .Matches(CodePattern).WithMessage("配件编码格式应为 P-XX-000（XX 两位大写字母，000 三位数字）。")
            .MaximumLength(30);
        RuleFor(x => x.PartName).NotEmpty().WithMessage("配件名称不能为空。").MaximumLength(60);
        RuleFor(x => x.CategoryName).NotEmpty().WithMessage("请选择配件类别。");
        RuleFor(x => x.Unit).NotEmpty().WithMessage("单位不能为空。").MaximumLength(10);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0).WithMessage("单价不能为负数。");
        RuleFor(x => x.StockQty).GreaterThanOrEqualTo(0).WithMessage("库存不能为负数。");
        RuleFor(x => x.MinStock).GreaterThanOrEqualTo(0).WithMessage("库存下限不能为负数。");
    }
}

public sealed class DeviceInputValidator : AbstractValidator<DeviceInput>
{
    private static readonly Regex CodePattern = new(@"^DEV-\d{3}$", RegexOptions.Compiled);

    public DeviceInputValidator()
    {
        RuleFor(x => x.DeviceCode).NotEmpty().WithMessage("设备编码不能为空。")
            .Matches(CodePattern).WithMessage("设备编码格式应为 DEV-000（三位数字）。")
            .MaximumLength(30);
        RuleFor(x => x.DeviceName).NotEmpty().WithMessage("设备名称不能为空。").MaximumLength(60);
        RuleFor(x => x.Location).MaximumLength(60).WithMessage("安装位置不能超过 60 字。");
    }
}

public sealed class RegisterInputValidator : AbstractValidator<RegisterInput>
{
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
    private static readonly Regex PhonePattern = new(@"^1\d{10}$", RegexOptions.Compiled);

    public RegisterInputValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().WithMessage("用户名不能为空。")
            .MinimumLength(3).WithMessage("用户名至少 3 个字符。")
            .MaximumLength(30);
        RuleFor(x => x.Password).NotEmpty().WithMessage("密码不能为空。")
            .Must(p => PasswordPolicy.Check(p).Success)
            .WithMessage("密码至少 8 位且同时包含字母和数字。");
        RuleFor(x => x.RealName).NotEmpty().WithMessage("请填写真实姓名。").MaximumLength(30);
        RuleFor(x => x.Phone).Must(p => string.IsNullOrWhiteSpace(p) || PhonePattern.IsMatch(p.Trim()))
            .WithMessage("手机号格式应为 11 位数字（可留空）。");
        RuleFor(x => x.Email).Must(e => string.IsNullOrWhiteSpace(e) || EmailPattern.IsMatch(e.Trim()))
            .WithMessage("邮箱格式不正确（如 name@example.com，可留空）。");
    }
}

/// <summary>冗余系数（算料对话框）。</summary>
public sealed class RedundancyValidator : AbstractValidator<decimal>
{
    public RedundancyValidator()
    {
        RuleFor(x => x).InclusiveBetween(1.00m, 2.00m)
            .WithMessage("冗余系数应在 1.00 ~ 2.00 之间（任务书要求\"留有一定冗余\"）。");
    }
}

public static class ValidatorExtensions
{
    /// <summary>返回第一条错误消息；合法返回 null（UI 单错误提示足够，全部错误可走 Errors）。</summary>
    public static string? FirstError<T>(this IValidator<T> validator, T instance)
    {
        var result = validator.Validate(instance);
        return result.IsValid ? null : result.Errors[0].ErrorMessage;
    }
}
