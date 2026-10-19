// =============================================================================
// NasSystem.Api - FileEndpoints.cs
// 文件管理 API 端点
// =============================================================================

namespace NasSystem.Api.Extensions;

using System.Security.Claims;
using Microsoft.AspNetCore.WebUtilities;
using NasSystem.Application.Files;
using NasSystem.Application.Files.Dtos;
using NasSystem.Application.Ocr;
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

        // 最近文件（Dashboard 面板）
        files.MapGet("/recent", GetRecentFiles)
             .WithName("GetRecentFiles")
             .WithSummary("获取最近文件")
             .WithDescription("按创建时间倒序取当前用户最近的文件与文件夹，供 Dashboard 最近文件面板使用")
             .RequireAuthorization();

        // 扫描同步
        files.MapPost("/scan", ScanAndSync)
             .WithName("ScanAndSync")
             .WithSummary("扫描并同步文件")
             .WithDescription("扫描存储目录下的物理文件，并同步到数据库。以物理文件为准，数据库为辅。")
             .RequireAuthorization();

        // 识别图片文字（ROADMAP Phase 2）
        files.MapPost("/ocr/{*path}", RecognizeImage)
             .WithName("RecognizeImageText")
             .WithSummary("识别图片文字")
             .WithDescription("对图片执行 OCR 并落库，识别结果可被搜索命中。重复识别覆盖旧结果")
             .RequireAuthorization();

        // 查询已识别的文字
        files.MapGet("/ocr/{*path}", GetImageText)
             .WithName("GetImageText")
             .WithSummary("查询图片识别文字")
             .WithDescription("取图片已识别的文字；未识别过返回 data 为 null")
             .RequireAuthorization();

        // 批量识别目录
        files.MapPost("/ocr-batch", RecognizeDirectory)
             .WithName("RecognizeDirectoryText")
             .WithSummary("批量识别目录图片")
             .WithDescription("识别目录下全部图片。已有文字的文件默认跳过，force=true 强制重识别")
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
        // 路由参数是去掉前导斜杠的裸路径（"123"、"docs/sub"，根目录是空串），
        // 而 DirectoryVersion 的键一律是带前导斜杠的规范形态（"/123"、"docs/sub"
        // 的父键等）。直接拿裸路径查版本号永远 miss（恒 0），子目录的条件请求
        // 会无限 304 重放陈旧列表——E2E 走查实证：移入 /123 的文件夹在前端
        // 不显示，而根目录（两种形态下都是 "/"）一直正常。这里对齐键形态
        var targetPath = string.IsNullOrEmpty(path) ? "/" : "/" + path.Trim('/');

        // 条件请求：目录内容版本号未变则回 304，省掉整趟查询、DTO 映射与 JSON 序列化。
        // 版本号由 UnitOfWork 在写提交成功后递增（DirectoryVersion），
        // 覆盖建文件夹/上传/重命名/移动/复制/删除/恢复/扫描等全部写入口，
        // 不依赖在这里枚举用例。
        // ApiResponse.Timestamp 每次现取，所以校验值不能用响应体哈希，必须用版本号。
        // ETag 必须含 userId：响应体按归属过滤（多用户隔离），同一目录不同用户
        // 内容不同——2026-10-19 E2E 走查实证，路径+版本的 ETag 会让浏览器
        // 把他人（或修复前的泄漏响应）的缓存体 304 重放给当前用户
        // 路径经 EscapeDataString 转 ASCII：ETag 是 HTTP 头，头值不允许
        // 非 ASCII（中文目录名直传会让 Kestrel 抛 Invalid non-ASCII
        // character，整个浏览 500——2026-10-19 E2E 走查实证）
        var version = NasSystem.Application.Files.DirectoryVersion.Get(targetPath);
        var etag = $"\"{Uri.EscapeDataString(targetPath)}:{userId}:{version}\"";

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
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);

        // S5：MultipartReader 流式解析。原实现 ReadFormAsync 会把超过 8MB 的
        // 文件 part 落临时盘（FormOptions.MemoryBufferThreshold），大文件上传的
        // 磁盘路径是 网络→临时文件→读回→写目标 三趟；流式下 section.Body 直接
        // 接进服务的写入加哈希流水线，单趟完成。代价：拿不到 part 声明大小，
        // 容量预检改"余量判定 + 边写边检"（FileService.PreflightUploadAsync）。
        // 注意：MultipartReader 是单向的，文件 part 的流必须在其出现时立即消费，
        // 因此 path/overwrite 字段必须先于 file 字段发送（前端已按此顺序）
        if (!Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !string.Equals(mediaType.MediaType.ToString(), "multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(ApiResponse<object>.Fail("Validation.ContentType", "请求必须是 multipart/form-data"));
        }

        var boundary = mediaType.Parameters
            .FirstOrDefault(p => string.Equals(p.Name.Value, "boundary", StringComparison.OrdinalIgnoreCase))
            ?.Value.Value?.Trim('"');
        if (string.IsNullOrEmpty(boundary))
        {
            return Results.BadRequest(ApiResponse<object>.Fail("Validation.ContentType", "multipart 请求缺少 boundary"));
        }

        var destinationPath = "/";
        var overwrite = false;
        string? fileName = null;
        string? contentType = null;

        var reader = new MultipartReader(boundary, request.Body);
        while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
        {
            if (!Microsoft.Net.Http.Headers.ContentDispositionHeaderValue.TryParse(
                    section.ContentDisposition, out var disposition))
            {
                continue;
            }

            var fieldName = disposition.Name.Value;
            if (fieldName is null)
                continue;

            if (fieldName.Equals("file", StringComparison.OrdinalIgnoreCase))
            {
                // 文件 part：不读流，直接交给服务边收边写。出现即消费，
                // 因此它必须排在 path 之后（path 有默认值 "/"，缺失时
                // 上传会落到根目录，与旧实现的 form["path"] ?? "/" 一致）
                fileName = disposition.FileNameStar.Value ?? disposition.FileName.Value;
                if (string.IsNullOrEmpty(fileName))
                {
                    return Results.BadRequest(ApiResponse<object>.Fail("Validation.NoFile", "未提供文件"));
                }

                // MultipartSection.Headers 是 Dictionary<string, StringValues>，
                // 没有强类型的 ContentType 访问器
                section.Headers.TryGetValue("Content-Type", out var contentTypeValue);
                contentType = contentTypeValue.ToString();

                var metadata = new UploadFileMetadata
                {
                    FileName = fileName,
                    DestinationPath = destinationPath,
                    // 流式解析拿不到声明大小：预检与容量判定改余量模式，见服务端注释
                    FileSize = 0,
                    ContentType = contentType,
                    Overwrite = overwrite
                };

                await using var stream = section.Body;
                var result = await fileService.UploadFileAsync(metadata, stream, userId, cancellationToken);
                return result.ToHttpResult();
            }

            if (fieldName.Equals("path", StringComparison.OrdinalIgnoreCase))
            {
                var value = await section.ReadAsStringAsync(cancellationToken);
                if (!string.IsNullOrEmpty(value))
                    destinationPath = value;
            }
            else if (fieldName.Equals("overwrite", StringComparison.OrdinalIgnoreCase))
            {
                var value = await section.ReadAsStringAsync(cancellationToken);
                overwrite = bool.TryParse(value, out var ow) && ow;
            }
        }

        // 走到这里说明整个 body 里没有 file part（file part 出现时上面直接返回）
        return Results.BadRequest(ApiResponse<object>.Fail("Validation.NoFile", "未提供文件"));
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

    private static async Task<IResult> GetRecentFiles(
        int count,
        IFileBrowseService fileService,
        ClaimsPrincipal user)
    {
        var userId = GetUserId(user);
        // 防御性夹取：查询串直传，无上限时一个超大 count 会把整表拖回来。
        // 仓储层也夹一次，这里是入口侧早退
        if (count < 1) count = 6;
        if (count > 50) count = 50;
        var result = await fileService.GetRecentFilesAsync(userId, count);
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

    private static async Task<IResult> RecognizeImage(
        string path,
        IOcrService ocrService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        var result = await ocrService.RecognizeAsync(path, userId, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetImageText(
        string path,
        IOcrService ocrService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        var result = await ocrService.GetTextAsync(path, userId, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RecognizeDirectory(
        OcrBatchRequest request,
        IOcrService ocrService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        var result = await ocrService.RecognizeDirectoryAsync(
            request.Path, userId, request.Force, cancellationToken);
        return result.ToHttpResult();
    }

    /// <summary>批量识别请求</summary>
    private sealed record OcrBatchRequest(string Path, bool Force = false);

    private static Guid GetUserId(ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}
