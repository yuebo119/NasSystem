// =============================================================================
// NasSystem.Domain - FilePath.cs
// 文件路径值对象
// =============================================================================

namespace NasSystem.Domain.ValueObjects;

using NasSystem.Domain.Common;

/// <summary>
/// 文件路径值对象 - 封装路径操作和验证
/// </summary>
public sealed class FilePath : ValueObject
{
    /// <summary>
    /// 路径值
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// 路径分段
    /// </summary>
    public IReadOnlyList<string> Segments { get; }

    /// <summary>
    /// 文件/文件夹名称
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 父路径
    /// </summary>
    public string? ParentPath { get; }

    /// <summary>
    /// 扩展名 (如果是文件)
    /// </summary>
    public string? Extension { get; }

    /// <summary>
    /// 是否为根路径
    /// </summary>
    public bool IsRoot { get; }

    /// <summary>
    /// 路径深度
    /// </summary>
    public int Depth { get; }

    private FilePath(string path)
    {
        // 标准化路径
        Value = Normalize(path);
        IsRoot = Value == "/";
        
        // 解析分段
        Segments = Value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        Depth = Segments.Count;
        
        // 解析名称
        Name = Segments.Count > 0 ? Segments[^1] : string.Empty;
        
        // 解析父路径
        ParentPath = Segments.Count > 1 
            ? "/" + string.Join("/", Segments.Take(Segments.Count - 1)) 
            : (IsRoot ? null : "/");
        
        // 解析扩展名
        var dotIndex = Name.LastIndexOf('.');
        Extension = dotIndex > 0 ? Name[dotIndex..] : null;
    }

    /// <summary>
    /// 创建文件路径
    /// </summary>
    public static FilePath Create(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("路径不能为空", nameof(path));
        
        return new FilePath(path);
    }

    /// <summary>
    /// 尝试创建文件路径
    /// </summary>
    public static bool TryCreate(string path, out FilePath? filePath)
    {
        filePath = null;
        
        if (string.IsNullOrWhiteSpace(path))
            return false;
        
        try
        {
            filePath = new FilePath(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 合并路径
    /// </summary>
    public FilePath Combine(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return this;
        
        var combined = Value.TrimEnd('/') + "/" + relativePath.TrimStart('/');
        return new FilePath(combined);
    }

    /// <summary>
    /// 获取相对路径
    /// </summary>
    public string GetRelativeTo(FilePath basePath)
    {
        if (!Value.StartsWith(basePath.Value, StringComparison.Ordinal))
            throw new ArgumentException("路径不在基路径下", nameof(basePath));
        
        return Value[basePath.Value.Length..].TrimStart('/');
    }

    /// <summary>
    /// 检查是否在指定路径下
    /// </summary>
    public bool IsUnder(FilePath parentPath)
    {
        return Value.StartsWith(parentPath.Value.TrimEnd('/') + "/", StringComparison.Ordinal);
    }

    /// <summary>
    /// 标准化路径
    /// </summary>
    private static string Normalize(string path)
    {
        // 替换反斜杠
        var normalized = path.Replace('\\', '/');
        
        // 移除多余斜杠
        while (normalized.Contains("//"))
            normalized = normalized.Replace("//", "/");
        
        // 移除尾部斜杠 (根路径除外)
        normalized = normalized.TrimEnd('/');
        
        // 确保以斜杠开头
        if (!normalized.StartsWith('/'))
            normalized = "/" + normalized;
        
        // 处理 . 和 ..
        var segments = new Stack<string>();
        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (segment)
            {
                case ".":
                    continue;
                case "..":
                    if (segments.Count > 0) segments.Pop();
                    break;
                default:
                    segments.Push(segment);
                    break;
            }
        }
        
        return segments.Count == 0 ? "/" : "/" + string.Join("/", segments.Reverse());
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value.ToLowerInvariant(); // 路径比较忽略大小写
    }

    public override string ToString() => Value;

    public static implicit operator string(FilePath path) => path.Value;
}
