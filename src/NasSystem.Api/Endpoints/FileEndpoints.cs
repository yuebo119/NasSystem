// =============================================================================
// NasSystem.Api - FileEndpoints.cs
// 文件管理 API 端点
// =============================================================================

namespace NasSystem.Api.Extensions;

using System.Security.Claims;
using NasSystem.Application.Files;
using NasSystem.Application.Files.Dtos;
using NasSystem.Shared;

/// <summary>
/// 文件管理端点扩展
/// </summary>
public static class FileEndpoints
{
    /// <summary>
    /// 注册文件管理相关端点
    /// </summary>
    public static RouteGroupBuilder MapFileEndpoints(this RouteGroupBuilder group)
    {
        var files = group.MapGroup("/files").WithTags("文件管理");

        // 获取目录内容
        files.MapGet("/browse/{*path}", GetDirectoryContents)
             .WithName("GetDirectoryContents")
             .WithSummary("获取目录内容")
             .WithDescription("浏览指定路径下的文件和文件夹列表，支持隐藏文件过滤")
             .RequireAuthorization();

        // 获取文件信息
        files.MapGet("/info/{*path}", GetFileInfo)
             .WithName("GetFileInfo")
             .WithSummary("获取文件信息")
             .WithDescription("获取指定文件或文件夹的详细信息，包括大小、类型、修改时间等")
             .RequireAuthorization();

        // 上传文件
        files.MapPost("/upload", UploadFile)
             .WithName("UploadFile")
             .WithSummary("上传文件")
             .WithDescription("上传文件到指定目录，支持覆盖已存在的文件")
             .Accepts<IFormFile>("multipart/form-data")
             .RequireAuthorization()
             .DisableAntiforgery();

        // 下载文件
        files.MapGet("/download/{*path}", DownloadFile)
             .WithName("DownloadFile")
             .WithSummary("下载文件")
             .WithDescription("下载指定路径的文件")
             .RequireAuthorization();

        // 创建文件夹
        files.MapPost("/folder", CreateFolder)
             .WithName("CreateFolder")
             .WithSummary("创建文件夹")
             .WithDescription("在指定父目录下创建新文件夹")
             .RequireAuthorization();

        // 重命名
        files.MapPut("/rename", RenameItem)
             .WithName("RenameItem")
             .WithSummary("重命名文件或文件夹")
             .WithDescription("重命名指定的文件或文件夹")
             .RequireAuthorization();

        // 删除
        files.MapPost("/delete", DeleteItems)
             .WithName("DeleteItems")
             .WithSummary("删除文件或文件夹")
             .WithDescription("删除一个或多个文件/文件夹，支持软删除（移至回收站）或永久删除")
             .RequireAuthorization();

        // 移动
        files.MapPost("/move", MoveItems)
             .WithName("MoveItems")
             .WithSummary("移动文件或文件夹")
             .WithDescription("将一个或多个文件/文件夹移动到目标目录")
             .RequireAuthorization();

        // 复制
        files.MapPost("/copy", CopyItems)
             .WithName("CopyItems")
             .WithSummary("复制文件或文件夹")
             .WithDescription("将一个或多个文件/文件夹复制到目标目录")
             .RequireAuthorization();

        // 搜索
        files.MapGet("/search", SearchFiles)
             .WithName("SearchFiles")
             .WithSummary("搜索文件")
             .WithDescription("根据关键词搜索文件，支持按类型、大小、日期等条件过滤")
             .RequireAuthorization();

        // 回收站
        files.MapGet("/recycle-bin", GetRecycleBin)
             .WithName("GetRecycleBin")
             .WithSummary("获取回收站内容")
             .WithDescription("获取当前用户回收站中的所有已删除文件")
             .RequireAuthorization();

        files.MapPost("/restore/{id:guid}", RestoreItem)
             .WithName("RestoreItem")
             .WithSummary("恢复删除的文件")
             .WithDescription("从回收站恢复指定的文件或文件夹")
             .RequireAuthorization();

        files.MapDelete("/recycle-bin", EmptyRecycleBin)
             .WithName("EmptyRecycleBin")
             .WithSummary("清空回收站")
             .WithDescription("永久删除回收站中的所有文件")
             .RequireAuthorization();

        // 文件统计
        files.MapGet("/statistics", GetFileStatistics)
             .WithName("GetFileStatistics")
             .WithSummary("获取文件统计")
             .WithDescription("获取当前用户的文件统计信息，包括文件数量、类型分布等")
             .RequireAuthorization();

        // 扫描同步
        files.MapPost("/scan", ScanAndSync)
             .WithName("ScanAndSync")
             .WithSummary("扫描并同步文件")
             .WithDescription("扫描存储目录下的物理文件，并同步到数据库。以物理文件为准，数据库为辅。")
             .RequireAuthorization();

        return group;
    }

    private static async Task<IResult> GetDirectoryContents(
        string? path,
        bool includeHidden = false,
        IFileBrowseService fileService = null!,
        ClaimsPrincipal user = null!,
        HttpContext httpContext = null!)
    {
        var userId = GetUserId(user);
        var targetPath = path ?? "/";

        // 条件请求：目录内容版本号未变则回 304，省掉整趟查询、DTO 映射与 JSON 序列化。
        // 版本号由 UnitOfWork 在写提交成功后递增（DirectoryVersion），
        // 覆盖建文件夹/上传/重命名/移动/复制/删除/恢复/扫描等全部写入口，
        // 不依赖在这里枚举用例。
        // ApiResponse.Timestamp 每次现取，所以校验值不能用响应体哈希，必须用版本号
        var version = NasSystem.Application.Files.DirectoryVersion.Get(targetPath);
        var etag = $"\"{targetPath}:{version}\"";

        if (httpContext.Request.Headers.IfNoneMatch.ToString().Contains(etag, StringComparison.Ordinal))
        {
            httpContext.Response.Headers.ETag = etag;
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        var result = await fileService.GetDirectoryContentsAsync(targetPath, userId, includeHidden);
        if (result.IsFailure)
            return result.ToHttpResult();

        httpContext.Response.Headers.ETag = etag;
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetFileInfo(
        string path,
        IFileBrowseService fileService,
        ClaimsPrincipal user)
    {
        var userId = GetUserId(user);
        var result = await fileService.GetFileInfoAsync(path, userId);
        return result.ToHttpResult();
    }

    private static async Task<IResult> UploadFile(
        HttpRequest request,
        IFileTransferService fileService,
        ClaimsPrincipal user)
    {
        var userId = GetUserId(user);
        var form = await request.ReadFormAsync();
        var file = form.Files.FirstOrDefault();

        if (file is null)
            return Results.BadRequest(ApiResponse<object>.Fail("Validation.NoFile", "未提供文件"));

        var destinationPath = form["path"].FirstOrDefault() ?? "/";
        var overwrite = bool.TryParse(form["overwrite"].FirstOrDefault(), out var ow) && ow;

        var metadata = new UploadFileMetadata
        {
            FileName = file.FileName,
            DestinationPath = destinationPath,
            FileSize = file.Length,
            ContentType = file.ContentType,
            Overwrite = overwrite
        };

        await using var stream = file.OpenReadStream();
        var result = await fileService.UploadFileAsync(metadata, stream, userId);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DownloadFile(
        string path,
        IFileTransferService fileService,
        ClaimsPrincipal user)
    {
        var userId = GetUserId(user);
        var result = await fileService.DownloadFileAsync(path, userId);

        if (!result.IsSuccess)
            return result.ToHttpResult();

        var (stream, fileName, contentType) = result.Value;
        // enableRangeProcessing：支持 Range 请求，大文件断点续传与视频拖动不再全量重下
        return Results.File(stream, contentType, fileName, enableRangeProcessing: true);
    }

    private static async Task<IResult> CreateFolder(
        CreateFolderRequest request,
        IFileTransferService fileService,
        ClaimsPrincipal user)
    {
        var userId = GetUserId(user);
        var result = await fileService.CreateFolderAsync(request, userId);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RenameItem(
        RenameItemRequest request,
        IFileTransferService fileService,
        ClaimsPrincipal user)
    {
        var userId = GetUserId(user);
        var result = await fileService.RenameAsync(request, userId);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DeleteItems(
        DeleteItemsRequest request,
        IFileRecycleService fileService,
        ClaimsPrincipal user)
    {
        var userId = GetUserId(user);
        var result = await fileService.DeleteAsync(request, userId);
        return result.ToHttpResult();
    }

    private static async Task<IResult> MoveItems(
        MoveOrCopyRequest request,
        IFileTransferService fileService,
        ClaimsPrincipal user)
    {
        var userId = GetUserId(user);
        var result = await fileService.MoveAsync(request, userId);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CopyItems(
        MoveOrCopyRequest request,
        IFileTransferService fileService,
        ClaimsPrincipal user)
    {
        var userId = GetUserId(user);
        var result = await fileService.CopyAsync(request, userId);
        return result.ToHttpResult();
    }

    private static async Task<IResult> SearchFiles(
        string query,
        string? path,
        string? extension,
        string? itemType,
        int page = 1,
        int pageSize = 50,
        IFileBrowseService fileService = null!,
        ClaimsPrincipal user = null!)
    {
        var userId = GetUserId(user);
        
        // 解析文件类型
        NasSystem.Domain.Enums.FileItemType? fileItemType = itemType?.ToLower() switch
        {
            "file" => NasSystem.Domain.Enums.FileItemType.File,
            "folder" => NasSystem.Domain.Enums.FileItemType.Folder,
            _ => null
        };
        
        var request = new SearchFilesRequest
        {
            Query = query,
            Path = path,
            Extension = extension,
            ItemType = fileItemType,
            Page = page > 0 ? page : 1,
            PageSize = pageSize > 0 ? pageSize : 50
        };
        var result = await fileService.SearchAsync(request, userId);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetRecycleBin(
        IFileRecycleService fileService,
        ClaimsPrincipal user)
    {
        var userId = GetUserId(user);
        var result = await fileService.GetRecycleBinAsync(userId);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RestoreItem(
        Guid id,
        IFileRecycleService fileService,
        ClaimsPrincipal user)
    {
        var userId = GetUserId(user);
        var result = await fileService.RestoreAsync(id, userId);
        return result.ToHttpResult();
    }

    private static async Task<IResult> EmptyRecycleBin(
        IFileRecycleService fileService,
        ClaimsPrincipal user)
    {
        var userId = GetUserId(user);
        var result = await fileService.EmptyRecycleBinAsync(userId);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetFileStatistics(
        IFileBrowseService fileService,
        ClaimsPrincipal user)
    {
        var userId = GetUserId(user);
        var result = await fileService.GetStatisticsAsync(userId);
        return result.ToHttpResult();
    }

    private static async Task<IResult> ScanAndSync(
        IFileSyncService fileService,
        ClaimsPrincipal user)
    {
        var userId = GetUserId(user);
        var result = await fileService.ScanAndSyncAsync(userId);
        return result.ToHttpResult();
    }

    private static Guid GetUserId(ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}
