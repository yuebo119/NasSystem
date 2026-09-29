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

    public static Error IsNotFile(string path) =>
        new("File.NotFile", $"'{path}' 不是文件");
}

/// <summary>
/// 存储相关错误（全项目唯一副本：本类取代 StorageService 内的同名重复定义，
/// 收敛时保留活的错误码字符串，原 Common 版 CannotDeleteDefault 码已并入）
/// </summary>
public static class StorageErrors
{
    public static Error PoolNotFound(Guid poolId) =>
        Error.NotFound("StoragePool", poolId);

    public static Error PoolNameExists(string name) =>
        new("StoragePool.NameExists", $"存储池名称 '{name}' 已存在");

    public static Error MountPathExists(string path) =>
        new("StoragePool.MountPathExists", $"挂载路径 '{path}' 已被使用");

    public static Error CannotDeleteDefaultPool =>
        new("StoragePool.CannotDeleteDefault", "不能删除默认存储池");
}
