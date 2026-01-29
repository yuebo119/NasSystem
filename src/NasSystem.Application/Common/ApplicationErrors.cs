// =============================================================================
// NasSystem.Application - ApplicationErrors.cs
// 应用层错误定义
// =============================================================================

namespace NasSystem.Application.Common;

using NasSystem.Shared;

/// <summary>
/// 用户相关错误
/// </summary>
public static class UserErrors
{
    public static Error NotFound(Guid userId) =>
        Error.NotFound("User", userId);

    public static Error NotFoundByUsername(string username) =>
        new("User.NotFound", $"用户 '{username}' 不存在");

    public static Error UsernameExists(string username) =>
        Error.Conflict($"用户名 '{username}' 已存在");

    public static Error EmailExists(string email) =>
        Error.Conflict($"邮箱 '{email}' 已被使用");

    public static Error InvalidCredentials =>
        new("User.InvalidCredentials", "用户名或密码错误");

    public static Error AccountDisabled =>
        new("User.Disabled", "账户已被禁用");

    public static Error PasswordMismatch =>
        new("User.PasswordMismatch", "当前密码不正确");

    public static Error CannotDeleteSelf =>
        new("User.CannotDeleteSelf", "不能删除自己的账户");

    public static Error CannotDeleteLastAdmin =>
        new("User.CannotDeleteLastAdmin", "不能删除最后一个管理员账户");

    public static Error QuotaExceeded =>
        new("User.QuotaExceeded", "已超出存储配额");
}

/// <summary>
/// 文件相关错误
/// </summary>
public static class FileErrors
{
    public static Error NotFound(string path) =>
        new("File.NotFound", $"文件或文件夹 '{path}' 不存在");

    public static Error NotFoundById(Guid fileId) =>
        Error.NotFound("File", fileId);

    public static Error AlreadyExists(string path) =>
        Error.Conflict($"文件或文件夹 '{path}' 已存在");

    public static Error AccessDenied(string path) =>
        Error.Forbidden($"无权访问 '{path}'");

    public static Error InvalidPath(string path) =>
        new("File.InvalidPath", $"路径 '{path}' 无效");

    public static Error CannotMoveToSubfolder =>
        new("File.CannotMoveToSubfolder", "不能将文件夹移动到其子文件夹中");

    public static Error CannotDeleteSystemFolder =>
        new("File.CannotDeleteSystemFolder", "不能删除系统文件夹");

    public static Error FileTooLarge(long maxSize) =>
        new("File.TooLarge", $"文件大小超过限制 ({maxSize} 字节)");

    public static Error UploadFailed(string reason) =>
        new("File.UploadFailed", $"文件上传失败: {reason}");

    public static Error DownloadFailed(string reason) =>
        new("File.DownloadFailed", $"文件下载失败: {reason}");

    public static Error IsNotFolder(string path) =>
        new("File.NotFolder", $"'{path}' 不是文件夹");

    public static Error IsNotFile(string path) =>
        new("File.NotFile", $"'{path}' 不是文件");
}

/// <summary>
/// 存储相关错误
/// </summary>
public static class StorageErrors
{
    public static Error PoolNotFound(Guid poolId) =>
        Error.NotFound("StoragePool", poolId);

    public static Error PoolNameExists(string name) =>
        Error.Conflict($"存储池名称 '{name}' 已存在");

    public static Error CannotDeletePoolWithFiles =>
        new("Storage.HasFiles", "无法删除包含文件的存储池");

    public static Error CannotDeleteDefaultPool =>
        new("Storage.CannotDeleteDefault", "无法删除默认存储池");

    public static Error InsufficientSpace =>
        new("Storage.InsufficientSpace", "存储空间不足");

    public static Error PathNotAccessible(string path) =>
        new("Storage.PathNotAccessible", $"存储路径 '{path}' 无法访问");
}
