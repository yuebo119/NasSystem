// =============================================================================
// NasSystem.Application - Files/DirectoryVersion.cs
// 目录内容版本号：HTTP 条件请求（ETag / 304）的失效依据
// =============================================================================

namespace NasSystem.Application.Files;

using System.Collections.Concurrent;

/// <summary>
/// 按目录路径维护内容版本号，mutation 提交成功后递增。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不能用响应体哈希做 ETag</b>：ApiResponse 的 Timestamp 每次现取，
/// 同一目录的两次响应体必然不同，哈希永远不相等，304 永不命中。必须有一个
/// 独立于响应体、只在内容真的变了才变的量，这就是版本号。
/// </para>
/// <para>
/// <b>为什么由 UnitOfWork 驱动而不是在各用例里手动 bump</b>：会改目录内容的入口有
/// 十几个（建文件夹、上传、重命名、移动、复制、删除、恢复、清空回收站、扫描同步），
/// 逐个枚举漏一个就是陈旧数据。而所有写入都经过 UoW 的 SaveChangesAsync，
/// 且 UoW 已经跟踪了被写实体，在这里统一 bump 一次覆盖全部入口。
/// </para>
/// <para>
/// <b>为什么是 static</b>：版本必须在请求之间共享才有意义。本项目是单实例部署
/// （SQLite 文件库加本地存储目录），ThumbnailService 的 in-flight 字典已有同一先例。
/// 多实例部署需要换成共享存储，届时这里换实现即可，调用方不变。
/// </para>
/// <para>
/// <b>已知边界</b>：不经由本应用的写入（外部进程直接改库、直接在盘上增删文件后
/// 不触发扫描）不会 bump，此时 ETag 不失效。这类变更本来就要靠扫描同步才能被系统
/// 感知，扫描本身会写库从而 bump，所以边界可接受。
/// </para>
/// </remarks>
public static class DirectoryVersion
{
    private static readonly ConcurrentDictionary<string, long> Versions = new(StringComparer.Ordinal);

    /// <summary>取目录的当前版本。从未变更过的目录返回 0。</summary>
    public static long Get(string directoryPath) =>
        Versions.TryGetValue(Normalize(directoryPath), out var version) ? version : 0;

    /// <summary>递增目录版本。</summary>
    public static void Bump(string directoryPath)
    {
        var key = Normalize(directoryPath);
        // AddOrUpdate 的 value factory 在竞争下可能被重复调用，版本号会跳跃。
        // 对 ETag 语义无影响：只要内容变了键值一定变，跳几个号无所谓
        Versions.AddOrUpdate(key, _ => 1, (_, current) => current + 1);
    }

    /// <summary>按文件路径递增其所在目录的版本。</summary>
    public static void BumpForFile(string filePath)
    {
        var parent = GetParentDirectory(filePath);
        if (parent is not null)
            Bump(parent);
    }

    /// <summary>取虚拟路径的父目录；根目录返回 "/"。</summary>
    public static string? GetParentDirectory(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        var lastSlash = path.LastIndexOf('/');
        if (lastSlash < 0)
            return null;

        return lastSlash == 0 ? "/" : path[..lastSlash];
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "/";

        var trimmed = path.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }
}
