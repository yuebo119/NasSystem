// =============================================================================
// NasSystem.Domain - IFileHasher.cs
// 文件哈希服务接口
// =============================================================================

namespace NasSystem.Domain.Services;

using NasSystem.Domain.ValueObjects;

/// <summary>
/// 文件哈希服务接口
/// </summary>
public interface IFileHasher
{
    /// <summary>
    /// 计算文件哈希 (SHA256)
    /// </summary>
    Task<HashValue> ComputeHashAsync(
        Stream stream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 计算文件哈希 (指定算法)
    /// </summary>
    Task<HashValue> ComputeHashAsync(
        Stream stream,
        HashAlgorithm algorithm,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 计算文件哈希
    /// </summary>
    Task<HashValue> ComputeHashAsync(
        string filePath,
        HashAlgorithm algorithm = HashAlgorithm.Sha256,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证文件哈希
    /// </summary>
    Task<bool> VerifyHashAsync(
        Stream stream,
        HashValue expectedHash,
        CancellationToken cancellationToken = default);
}
