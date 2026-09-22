// =============================================================================
// NasSystem.Domain - StorageSize.cs
// 存储大小值对象
// =============================================================================

namespace NasSystem.Domain.ValueObjects;

using NasSystem.Domain.Common;

/// <summary>
/// 存储大小值对象 - 封装字节大小和格式化
/// </summary>

/// <remarks>
/// 【当前状态】零引用——容量数值当前由 FileItem.SizeBytes / StoragePool
/// 等实体上的 long 属性承载。这是一个有意识的保留决策：当容量计算用例
/// （四舍五入策略、单位换算、比较语义）的复杂度上升到需要类型安全时，
/// 直接启用本值对象即可。现在启用属于无消费方的过度设计。
/// </remarks>
public sealed class StorageSize : ValueObject, IComparable<StorageSize>
{
    private static readonly string[] SizeUnits = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];
    
    /// <summary>
    /// 字节数
    /// </summary>
    public long Bytes { get; }

    private StorageSize(long bytes)
    {
        if (bytes < 0)
            throw new ArgumentOutOfRangeException(nameof(bytes), "字节数不能为负数");
        
        Bytes = bytes;
    }

    /// <summary>
    /// 从字节创建
    /// </summary>
    public static StorageSize FromBytes(long bytes) => new(bytes);

    /// <summary>
    /// 从 KB 创建
    /// </summary>
    public static StorageSize FromKilobytes(double kb) => new((long)(kb * 1024));

    /// <summary>
    /// 从 MB 创建
    /// </summary>
    public static StorageSize FromMegabytes(double mb) => new((long)(mb * 1024 * 1024));

    /// <summary>
    /// 从 GB 创建
    /// </summary>
    public static StorageSize FromGigabytes(double gb) => new((long)(gb * 1024 * 1024 * 1024));

    /// <summary>
    /// 从 TB 创建
    /// </summary>
    public static StorageSize FromTerabytes(double tb) => new((long)(tb * 1024L * 1024 * 1024 * 1024));

    /// <summary>
    /// 零大小
    /// </summary>
    public static StorageSize Zero => new(0);

    /// <summary>
    /// 转换为 KB
    /// </summary>
    public double ToKilobytes() => Bytes / 1024.0;

    /// <summary>
    /// 转换为 MB
    /// </summary>
    public double ToMegabytes() => Bytes / (1024.0 * 1024);

    /// <summary>
    /// 转换为 GB
    /// </summary>
    public double ToGigabytes() => Bytes / (1024.0 * 1024 * 1024);

    /// <summary>
    /// 转换为 TB
    /// </summary>
    public double ToTerabytes() => Bytes / (1024.0 * 1024 * 1024 * 1024);

    /// <summary>
    /// 格式化为人类可读字符串
    /// </summary>
    public string ToHumanReadable(int decimals = 2)
    {
        if (Bytes == 0) return "0 B";
        
        var magnitude = (int)Math.Floor(Math.Log(Bytes, 1024));
        magnitude = Math.Min(magnitude, SizeUnits.Length - 1);
        
        var adjustedSize = Bytes / Math.Pow(1024, magnitude);
        return $"{Math.Round(adjustedSize, decimals)} {SizeUnits[magnitude]}";
    }

    /// <summary>
    /// 解析人类可读字符串
    /// </summary>
    public static StorageSize Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("值不能为空", nameof(value));

        value = value.Trim().ToUpperInvariant();
        
        // 查找单位
        var unitIndex = -1;
        var unit = "B";
        
        for (int i = SizeUnits.Length - 1; i >= 0; i--)
        {
            if (value.EndsWith(SizeUnits[i]))
            {
                unitIndex = i;
                unit = SizeUnits[i];
                break;
            }
        }
        
        // 解析数值
        var numberPart = unitIndex >= 0 
            ? value[..^unit.Length].Trim() 
            : value;
        
        if (!double.TryParse(numberPart, out var number))
            throw new FormatException($"无法解析数值: {numberPart}");
        
        // 计算字节数
        var bytes = (long)(number * Math.Pow(1024, unitIndex >= 0 ? unitIndex : 0));
        return new StorageSize(bytes);
    }

    /// <summary>
    /// 尝试解析
    /// </summary>
    public static bool TryParse(string value, out StorageSize? size)
    {
        size = null;
        try
        {
            size = Parse(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // 运算符重载
    public static StorageSize operator +(StorageSize a, StorageSize b) => 
        new(a.Bytes + b.Bytes);
    
    public static StorageSize operator -(StorageSize a, StorageSize b) => 
        new(Math.Max(0, a.Bytes - b.Bytes));
    
    public static StorageSize operator *(StorageSize size, int multiplier) => 
        new(size.Bytes * multiplier);
    
    public static StorageSize operator /(StorageSize size, int divisor) => 
        new(size.Bytes / divisor);
    
    public static bool operator >(StorageSize a, StorageSize b) => a.Bytes > b.Bytes;
    public static bool operator <(StorageSize a, StorageSize b) => a.Bytes < b.Bytes;
    public static bool operator >=(StorageSize a, StorageSize b) => a.Bytes >= b.Bytes;
    public static bool operator <=(StorageSize a, StorageSize b) => a.Bytes <= b.Bytes;

    public int CompareTo(StorageSize? other) => 
        other is null ? 1 : Bytes.CompareTo(other.Bytes);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Bytes;
    }

    public override string ToString() => ToHumanReadable();

    public static implicit operator long(StorageSize size) => size.Bytes;
    public static implicit operator StorageSize(long bytes) => FromBytes(bytes);
}
