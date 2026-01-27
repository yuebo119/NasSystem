// =============================================================================
// NasSystem.Domain - HashValue.cs
// 哈希值值对象
// =============================================================================

namespace NasSystem.Domain.ValueObjects;

using NasSystem.Domain.Common;
using System.Text.RegularExpressions;

/// <summary>
/// 哈希值值对象
/// </summary>
public sealed partial class HashValue : ValueObject
{
    /// <summary>
    /// 哈希算法
    /// </summary>
    public HashAlgorithm Algorithm { get; }

    /// <summary>
    /// 哈希值 (十六进制字符串)
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// 哈希值 (字节数组)
    /// </summary>
    public byte[] Bytes { get; }

    private HashValue(HashAlgorithm algorithm, string hexValue)
    {
        Algorithm = algorithm;
        Value = hexValue.ToUpperInvariant();
        Bytes = Convert.FromHexString(hexValue);
    }

    private HashValue(HashAlgorithm algorithm, byte[] bytes)
    {
        Algorithm = algorithm;
        Bytes = bytes;
        Value = Convert.ToHexString(bytes);
    }

    /// <summary>
    /// 从十六进制字符串创建
    /// </summary>
    public static HashValue FromHex(HashAlgorithm algorithm, string hexValue)
    {
        if (string.IsNullOrWhiteSpace(hexValue))
            throw new ArgumentException("哈希值不能为空", nameof(hexValue));
        
        if (!HexRegex().IsMatch(hexValue))
            throw new ArgumentException("无效的十六进制字符串", nameof(hexValue));
        
        var expectedLength = algorithm switch
        {
            HashAlgorithm.Md5 => 32,
            HashAlgorithm.Sha1 => 40,
            HashAlgorithm.Sha256 => 64,
            HashAlgorithm.Sha512 => 128,
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
        };
        
        if (hexValue.Length != expectedLength)
            throw new ArgumentException($"{algorithm} 哈希值应为 {expectedLength} 个字符", nameof(hexValue));
        
        return new HashValue(algorithm, hexValue);
    }

    /// <summary>
    /// 从字节数组创建
    /// </summary>
    public static HashValue FromBytes(HashAlgorithm algorithm, byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0)
            throw new ArgumentException("字节数组不能为空", nameof(bytes));
        
        return new HashValue(algorithm, bytes);
    }

    /// <summary>
    /// 创建 MD5 哈希值
    /// </summary>
    public static HashValue Md5(string hexValue) => FromHex(HashAlgorithm.Md5, hexValue);

    /// <summary>
    /// 创建 SHA1 哈希值
    /// </summary>
    public static HashValue Sha1(string hexValue) => FromHex(HashAlgorithm.Sha1, hexValue);

    /// <summary>
    /// 创建 SHA256 哈希值
    /// </summary>
    public static HashValue Sha256(string hexValue) => FromHex(HashAlgorithm.Sha256, hexValue);

    /// <summary>
    /// 创建 SHA512 哈希值
    /// </summary>
    public static HashValue Sha512(string hexValue) => FromHex(HashAlgorithm.Sha512, hexValue);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Algorithm;
        yield return Value;
    }

    public override string ToString() => $"{Algorithm}:{Value}";

    [GeneratedRegex("^[0-9A-Fa-f]+$")]
    private static partial Regex HexRegex();
}

/// <summary>
/// 哈希算法枚举
/// </summary>
public enum HashAlgorithm
{
    Md5,
    Sha1,
    Sha256,
    Sha512
}
