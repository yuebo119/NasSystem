// =============================================================================
// NasSystem.Shared - PagedResult.cs
// 分页结果
// =============================================================================

namespace NasSystem.Shared;

/// <summary>
/// 分页结果
/// </summary>
/// <typeparam name="T">数据项类型</typeparam>
public sealed record PagedResult<T>
{
    /// <summary>
    /// 数据项
    /// </summary>
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>
    /// 当前页码 (从 1 开始)
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// 每页大小
    /// </summary>
    public int PageSize { get; init; }

    /// <summary>
    /// 总记录数
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// 总页数
    /// </summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;

    /// <summary>
    /// 是否有上一页
    /// </summary>
    public bool HasPreviousPage => Page > 1;

    /// <summary>
    /// 是否有下一页
    /// </summary>
    public bool HasNextPage => Page < TotalPages;

    /// <summary>
    /// 创建分页结果
    /// </summary>
    public static PagedResult<T> Create(
        IReadOnlyList<T> items,
        int page,
        int pageSize,
        int totalCount) => new()
    {
        Items = items,
        Page = page,
        PageSize = pageSize,
        TotalCount = totalCount
    };

    /// <summary>
    /// 创建空分页结果
    /// </summary>
    public static PagedResult<T> Empty(int page = 1, int pageSize = 10) => new()
    {
        Items = [],
        Page = page,
        PageSize = pageSize,
        TotalCount = 0
    };

    /// <summary>
    /// 映射到另一种类型
    /// </summary>
    public PagedResult<TNew> Map<TNew>(Func<T, TNew> mapper) => new()
    {
        Items = Items.Select(mapper).ToList(),
        Page = Page,
        PageSize = PageSize,
        TotalCount = TotalCount
    };
}

/// <summary>
/// 分页请求
/// </summary>
public record PagedRequest
{
    /// <summary>
    /// 页码 (从 1 开始)
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// 每页大小
    /// </summary>
    public int PageSize { get; init; } = 20;

    /// <summary>
    /// 排序字段
    /// </summary>
    public string? SortBy { get; init; }

    /// <summary>
    /// 是否降序
    /// </summary>
    public bool SortDescending { get; init; }

    /// <summary>
    /// 搜索关键词
    /// </summary>
    public string? SearchTerm { get; init; }

    /// <summary>
    /// 计算跳过的记录数
    /// </summary>
    public int Skip => (Page - 1) * PageSize;
}
