// =============================================================================
// NasSystem.Application - FileValidators.cs
// 文件相关请求验证器
// =============================================================================

namespace NasSystem.Application.Files.Validators;

using FluentValidation;
using NasSystem.Application.Files.Dtos;

/// <summary>
/// 创建文件夹请求验证器
/// </summary>
public sealed class CreateFolderRequestValidator : AbstractValidator<CreateFolderRequest>
{
    // 文件名非法字符
    private static readonly char[] InvalidChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public CreateFolderRequestValidator()
    {
        RuleFor(x => x.ParentPath)
            .NotEmpty().WithMessage("父路径不能为空");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("文件夹名称不能为空")
            .MaximumLength(255).WithMessage("文件夹名称最多 255 个字符")
            .Must(NotContainInvalidChars).WithMessage($"文件夹名称不能包含以下字符: {string.Join(" ", InvalidChars)}")
            .Must(NotBeReservedName).WithMessage("文件夹名称不能使用系统保留名称");
    }

    private static bool NotContainInvalidChars(string name) =>
        !string.IsNullOrEmpty(name) && !name.Any(c => InvalidChars.Contains(c));

    private static bool NotBeReservedName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        
        var reservedNames = new[]
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };
        
        var nameWithoutExtension = name.Split('.')[0];
        return !reservedNames.Contains(nameWithoutExtension.ToUpperInvariant());
    }
}

/// <summary>
/// 重命名请求验证器
/// </summary>
public sealed class RenameItemRequestValidator : AbstractValidator<RenameItemRequest>
{
    private static readonly char[] InvalidChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public RenameItemRequestValidator()
    {
        RuleFor(x => x.Path)
            .NotEmpty().WithMessage("路径不能为空");

        RuleFor(x => x.NewName)
            .NotEmpty().WithMessage("新名称不能为空")
            .MaximumLength(255).WithMessage("名称最多 255 个字符")
            .Must(name => !string.IsNullOrEmpty(name) && !name.Any(c => InvalidChars.Contains(c)))
            .WithMessage($"名称不能包含以下字符: {string.Join(" ", InvalidChars)}");
    }
}

/// <summary>
/// 移动/复制请求验证器
/// </summary>
public sealed class MoveOrCopyRequestValidator : AbstractValidator<MoveOrCopyRequest>
{
    public MoveOrCopyRequestValidator()
    {
        RuleFor(x => x.SourcePaths)
            .NotEmpty().WithMessage("源路径不能为空")
            .Must(paths => paths.All(p => !string.IsNullOrWhiteSpace(p)))
            .WithMessage("源路径列表包含无效路径");

        RuleFor(x => x.DestinationPath)
            .NotEmpty().WithMessage("目标路径不能为空");
    }
}

/// <summary>
/// 删除请求验证器
/// </summary>
public sealed class DeleteItemsRequestValidator : AbstractValidator<DeleteItemsRequest>
{
    public DeleteItemsRequestValidator()
    {
        RuleFor(x => x.Paths)
            .NotEmpty().WithMessage("路径列表不能为空")
            .Must(paths => paths.All(p => !string.IsNullOrWhiteSpace(p)))
            .WithMessage("路径列表包含无效路径");
    }
}

/// <summary>
/// 搜索请求验证器
/// </summary>
public sealed class SearchFilesRequestValidator : AbstractValidator<SearchFilesRequest>
{
    public SearchFilesRequestValidator()
    {
        RuleFor(x => x.Query)
            .NotEmpty().WithMessage("搜索关键词不能为空")
            .MinimumLength(2).WithMessage("搜索关键词至少 2 个字符")
            .MaximumLength(100).WithMessage("搜索关键词最多 100 个字符");

        RuleFor(x => x.Page)
            .GreaterThan(0).WithMessage("页码必须大于 0");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("每页大小必须在 1-100 之间");

        RuleFor(x => x.MinSize)
            .GreaterThanOrEqualTo(0).WithMessage("最小大小不能为负数")
            .When(x => x.MinSize.HasValue);

        RuleFor(x => x.MaxSize)
            .GreaterThan(x => x.MinSize ?? 0).WithMessage("最大大小必须大于最小大小")
            .When(x => x.MaxSize.HasValue && x.MinSize.HasValue);
    }
}
