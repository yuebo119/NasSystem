// =============================================================================
// NasSystem.Infrastructure - FileHasher.cs
// 文件哈希服务实现
// =============================================================================

namespace NasSystem.Infrastructure.Services;

using System.Buffers;
using NasSystem.Domain.Services;
using NasSystem.Domain.ValueObjects;
using HashAlgorithm = NasSystem.Domain.ValueObjects.HashAlgorithm;

/// <summary>
/// 文件哈希服务实现
/// </summary>
public sealed class FileHasher : IFileHasher
{
    private const int BufferSize = 81920; // 80 KB

    public async Task<HashValue> ComputeHashAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        return await ComputeHashAsync(stream, HashAlgorithm.Sha256, cancellationToken);
    }

    public async Task<HashValue> ComputeHashAsync(
        Stream stream,
        HashAlgorithm algorithm,
        CancellationToken cancellationToken = default)
    {
        using var hashAlgorithm = CreateHashAlgorithm(algorithm);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)) > 0)
            {
                hashAlgorithm.TransformBlock(buffer, 0, bytesRead, buffer, 0);
            }

            hashAlgorithm.TransformFinalBlock([], 0, 0);
            var hashBytes = hashAlgorithm.Hash!;

            return HashValue.FromBytes(algorithm, hashBytes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async Task<HashValue> ComputeHashAsync(
        string filePath,
        HashAlgorithm algorithm = HashAlgorithm.Sha256,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return await ComputeHashAsync(stream, algorithm, cancellationToken);
    }

    public async Task<bool> VerifyHashAsync(
        Stream stream,
        HashValue expectedHash,
        CancellationToken cancellationToken = default)
    {
        var computedHash = await ComputeHashAsync(stream, expectedHash.Algorithm, cancellationToken);
        return computedHash.Value == expectedHash.Value;
    }

    private static System.Security.Cryptography.HashAlgorithm CreateHashAlgorithm(HashAlgorithm algorithm)
    {
        return algorithm switch
        {
            HashAlgorithm.Md5 => System.Security.Cryptography.MD5.Create(),
            HashAlgorithm.Sha1 => System.Security.Cryptography.SHA1.Create(),
            HashAlgorithm.Sha256 => System.Security.Cryptography.SHA256.Create(),
            HashAlgorithm.Sha512 => System.Security.Cryptography.SHA512.Create(),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
        };
    }
}
