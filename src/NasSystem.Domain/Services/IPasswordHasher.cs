// =============================================================================
// NasSystem.Domain - IPasswordHasher.cs
// 密码哈希服务接口
// =============================================================================

namespace NasSystem.Domain.Services;

/// <summary>
/// 密码哈希服务接口
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// 生成密码哈希
    /// </summary>
    /// <param name="password">明文密码</param>
    /// <returns>(哈希值, 盐值)</returns>
    (string Hash, string Salt) HashPassword(string password);

    /// <summary>
    /// 验证密码
    /// </summary>
    /// <param name="password">明文密码</param>
    /// <param name="hash">存储的哈希值</param>
    /// <param name="salt">存储的盐值</param>
    /// <returns>是否匹配</returns>
    bool VerifyPassword(string password, string hash, string salt);
}
