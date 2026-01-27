// =============================================================================
// NasSystem.Application - UserValidators.cs
// 用户相关请求验证器
// =============================================================================

namespace NasSystem.Application.Users.Validators;

using FluentValidation;
using NasSystem.Application.Users.Dtos;

/// <summary>
/// 创建用户请求验证器
/// </summary>
public sealed class CreateUserDtoValidator : AbstractValidator<CreateUserDto>
{
    public CreateUserDtoValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("用户名不能为空")
            .MinimumLength(3).WithMessage("用户名至少 3 个字符")
            .MaximumLength(50).WithMessage("用户名最多 50 个字符")
            .Matches(@"^[a-zA-Z][a-zA-Z0-9_-]*$")
            .WithMessage("用户名必须以字母开头，只能包含字母、数字、下划线和连字符");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("密码不能为空")
            .MinimumLength(8).WithMessage("密码至少 8 个字符")
            .MaximumLength(100).WithMessage("密码最多 100 个字符")
            .Matches(@"[A-Z]").WithMessage("密码必须包含至少一个大写字母")
            .Matches(@"[a-z]").WithMessage("密码必须包含至少一个小写字母")
            .Matches(@"[0-9]").WithMessage("密码必须包含至少一个数字");

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("邮箱格式不正确")
            .MaximumLength(100).WithMessage("邮箱最多 100 个字符")
            .When(x => !string.IsNullOrEmpty(x.Email));

        RuleFor(x => x.DisplayName)
            .MaximumLength(100).WithMessage("显示名称最多 100 个字符")
            .When(x => !string.IsNullOrEmpty(x.DisplayName));

        RuleFor(x => x.QuotaBytes)
            .GreaterThan(0).WithMessage("配额必须大于 0")
            .When(x => x.QuotaBytes.HasValue);
    }
}

/// <summary>
/// 更新用户请求验证器
/// </summary>
public sealed class UpdateUserDtoValidator : AbstractValidator<UpdateUserDto>
{
    public UpdateUserDtoValidator()
    {
        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("邮箱格式不正确")
            .MaximumLength(100).WithMessage("邮箱最多 100 个字符")
            .When(x => !string.IsNullOrEmpty(x.Email));

        RuleFor(x => x.DisplayName)
            .MaximumLength(100).WithMessage("显示名称最多 100 个字符")
            .When(x => !string.IsNullOrEmpty(x.DisplayName));

        RuleFor(x => x.QuotaBytes)
            .GreaterThan(0).WithMessage("配额必须大于 0")
            .When(x => x.QuotaBytes.HasValue);
    }
}

/// <summary>
/// 登录请求验证器
/// </summary>
public sealed class LoginDtoValidator : AbstractValidator<LoginDto>
{
    public LoginDtoValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("用户名不能为空");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("密码不能为空");
    }
}
