// =============================================================================
// NasSystem.Infrastructure - PasswordHasher.cs
// 密码哈希服务实现
// =============================================================================

namespace NasSystem.Infrastructure.Services;

using System.Security.Cryptography;
using NasSystem.Domain.Services;

/// <summary>
/// 密码哈希服务实现 (使用 PBKDF2)
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16; // 128 bit
    private const int KeySize = 32;  // 256 bit
    private const int Iterations = 100000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public (string Hash, string Salt) HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        // 生成随机盐值
        var salt = RandomNumberGenerator.GetBytes(SaltSize);

        // 使用 PBKDF2 生成哈希
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            Algorithm,
            KeySize);

        return (
            Hash: Convert.ToBase64String(hash),
            Salt: Convert.ToBase64String(salt)
        );
    }

    public bool VerifyPassword(string password, string hash, string salt)
    {
        if (string.IsNullOrWhiteSpace(password))
            return false;

        try
        {
            var saltBytes = Convert.FromBase64String(salt);
            var hashBytes = Convert.FromBase64String(hash);

            // 使用相同参数计算哈希
            var computedHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                saltBytes,
                Iterations,
                Algorithm,
                KeySize);

            // 使用固定时间比较防止时序攻击
            return CryptographicOperations.FixedTimeEquals(computedHash, hashBytes);
        }
        catch
        {
            return false;
        }
    }
}
