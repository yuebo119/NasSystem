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
    public string? OwnerName { get; init; }
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
/// 上传进度 DTO
/// </summary>
public sealed record UploadProgressDto
{
    public required Guid UploadId { get; init; }
    public required string FileName { get; init; }
    public required long TotalBytes { get; init; }
    public required long UploadedBytes { get; init; }
    public required double Percentage { get; init; }
    public required string Status { get; init; }
    public string? ErrorMessage { get; init; }
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
    public long? MinSize { get; init; }
    public long? MaxSize { get; init; }
    public DateTime? ModifiedAfter { get; init; }
    public DateTime? ModifiedBefore { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}
