// =============================================================================
// NasSystem.Application - FileDtos.cs
// 文件相关 DTO
// =============================================================================

namespace NasSystem.Application.Files.Dtos;

using NasSystem.Domain.Enums;

/// <summary>
/// 文件项 DTO
/// </summary>
public sealed record FileItemDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required FileItemType ItemType { get; init; }
    public required long SizeBytes { get; init; }
    public string? SizeDisplay { get; init; }
    public string? MimeType { get; init; }
    public string? Extension { get; init; }
    public string? Hash { get; init; }
    public Guid? ParentId { get; init; }
    public required Guid OwnerId { get; init; }
    public required bool IsHidden { get; init; }
    public required bool IsReadOnly { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    
    /// <summary>
    /// 是否为文件夹
    /// </summary>
    public bool IsFolder => ItemType == FileItemType.Folder;
    
    /// <summary>
    /// 是否为文件
    /// </summary>
    public bool IsFile => ItemType == FileItemType.File;
}

/// <summary>
/// 目录内容 DTO
/// </summary>
public sealed record DirectoryContentDto
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public Guid? ParentId { get; init; }
    public string? ParentPath { get; init; }
    public required IReadOnlyList<FileItemDto> Items { get; init; }
    public required int FolderCount { get; init; }
    public required int FileCount { get; init; }
    public required long TotalSizeBytes { get; init; }
    public string? TotalSizeDisplay { get; init; }
    public required IReadOnlyList<BreadcrumbDto> Breadcrumbs { get; init; }
}

/// <summary>
/// 面包屑导航 DTO
/// </summary>
public sealed record BreadcrumbDto
{
    public required string Name { get; init; }
    public required string Path { get; init; }
}

/// <summary>
/// 创建文件夹请求
/// </summary>
public sealed record CreateFolderRequest
{
    public required string ParentPath { get; init; }
    public required string Name { get; init; }
}

/// <summary>
/// 重命名请求
/// </summary>
public sealed record RenameItemRequest
{
    public required string Path { get; init; }
    public required string NewName { get; init; }
}

/// <summary>
/// 移动/复制请求
/// </summary>
public sealed record MoveOrCopyRequest
{
    public required IReadOnlyList<string> SourcePaths { get; init; }
    public required string DestinationPath { get; init; }
}

/// <summary>
/// 删除请求
/// </summary>
public sealed record DeleteItemsRequest
{
    public required IReadOnlyList<string> Paths { get; init; }
    public bool Permanent { get; init; }
}

/// <summary>
/// 上传文件元数据
/// </summary>
public sealed record UploadFileMetadata
{
    public required string FileName { get; init; }
    public required string DestinationPath { get; init; }
    public required long FileSize { get; init; }
    public string? ContentType { get; init; }
    public bool Overwrite { get; init; }
}

/// <summary>
/// 搜索请求
/// </summary>
public sealed record SearchFilesRequest
{
    public required string Query { get; init; }
    public string? Path { get; init; }
    public FileItemType? ItemType { get; init; }
    public string? Extension { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

/// <summary>
/// 文件统计 DTO
/// </summary>
public sealed record FileStatisticsDto
{
    public int TotalFiles { get; init; }
    public int TotalFolders { get; init; }
    public long TotalSizeBytes { get; init; }
    public string? TotalSizeDisplay { get; init; }
    public int ImageCount { get; init; }
    public int VideoCount { get; init; }
    public int AudioCount { get; init; }
    public int DocumentCount { get; init; }
    public int OtherCount { get; init; }
    public int RecentFilesCount { get; init; }
}

/// <summary>
/// 扫描结果 DTO
/// </summary>
public sealed record ScanResultDto
{
    public int AddedFiles { get; init; }
    public int AddedFolders { get; init; }
    public int RemovedFiles { get; init; }
    public int RemovedFolders { get; init; }
    public int UpdatedFiles { get; init; }
    public long TotalSizeBytes { get; init; }
    public string? TotalSizeDisplay { get; init; }
    public TimeSpan Duration { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// 删除结果 DTO（ITM-N14：per-item 结果收集，禁止吞异常部分成功）
/// </summary>
public sealed class DeleteResultDto
{
    /// <summary>成功删除（含软删除）的项目数</summary>
    public int DeletedCount { get; set; }

    /// <summary>失败项描述列表（物理删除失败/路径不存在等）</summary>
    public List<string> FailedItems { get; set; } = [];
}
