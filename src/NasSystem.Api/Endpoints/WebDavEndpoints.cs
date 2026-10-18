// =============================================================================
// NasSystem.Api - WebDavEndpoints.cs
// WebDAV 协议端点（ROADMAP Phase 0 协议层）
// =============================================================================

namespace NasSystem.Api.Extensions;

using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Http.Features;
using NasSystem.Api.Security;
using NasSystem.Application.Files;
using NasSystem.Application.Files.Dtos;
using NasSystem.Domain.Common;
using NasSystem.Domain.Repositories;
using NasSystem.Domain.Services;
using NasSystem.Shared;
/// <summary>
/// WebDAV 协议端点：把应用层的文件用例映射为 RFC 4918 方法，
/// 使 Windows 资源管理器 / macOS Finder / Linux davfs2 / iOS 文件 App
/// 能直接挂载 NAS 目录。
/// </summary>
/// <remarks>
/// 设计红线（ROADMAP §2）：所有写操作经 FileService，配额检查、软删除、
/// SignalR 推送不被旁路。Basic 认证在此独立完成：WebDAV 客户端无法携带
/// Authorization: Bearer 头，JWT 中间件对 /dav 不适用。
/// </remarks>
public static class WebDavEndpoints
{
    private const string DavPrefix = "/dav";

    // LOCK/UNLOCK/PROPPATCH 注册在路由里但返回 405：不注册的话这些动词会落进
    // SPA fallback 返回 index.html，客户端把 HTML 当 XML 解析直接报协议错误。
    // 不支持 class 2 锁的后果是 Office 类客户端只能只读打开，资源管理器与
    // Finder 不依赖锁，挂载场景不受影响
    private static readonly string[] SupportedMethods =
    [
        "OPTIONS", "PROPFIND", "GET", "HEAD", "PUT", "MKCOL", "DELETE", "MOVE", "COPY", "PROPPATCH", "LOCK", "UNLOCK"
    ];

    /// <summary>
    /// 注册 WebDAV 端点。挂在 /dav 下，与 /api/v1 平级
    /// </summary>
    public static IEndpointRouteBuilder MapWebDavEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods(DavPrefix, SupportedMethods, HandleDavRequest)
                 .DisableAntiforgery();
        endpoints.MapMethods($"{DavPrefix}/{{**path}}", SupportedMethods, HandleDavRequest)
                 .DisableAntiforgery();
        return endpoints;
    }

    private static async Task<IResult> HandleDavRequest(
        HttpContext context,
        string? path,
        IFileBrowseService browseService,
        IFileTransferService transferService,
        IFileRecycleService recycleService,
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IUnitOfWork unitOfWork,
        IWebDavAuthGuard authGuard,
        CancellationToken cancellationToken)
    {
        var method = context.Request.Method;

        // OPTIONS 免认证：部分客户端握手探测不带凭证，RFC 4918 实践要求
        // 未认证也能发现服务器能力
        if (method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            return HandleOptions(context);
        }

        var (principal, authFailure) = await AuthenticateAsync(
            context, userRepository, passwordHasher, unitOfWork, authGuard, cancellationToken);
        if (authFailure is not null)
        {
            return authFailure;
        }

        var userIdClaim = principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Unauthorized();
        }

        var virtualPath = NormalizeDavPath(path);

        return method.ToUpperInvariant() switch
        {
            "PROPFIND" => await PropFindAsync(context, virtualPath, userId, browseService, cancellationToken),
            "GET" => await GetAsync(context, virtualPath, userId, browseService, transferService, cancellationToken),
            "HEAD" => await HeadAsync(context, virtualPath, userId, browseService, cancellationToken),
            "PUT" => await PutAsync(context, virtualPath, userId, browseService, transferService, cancellationToken),
            "MKCOL" => await MkColAsync(context, virtualPath, userId, transferService, cancellationToken),
            "DELETE" => await DeleteAsync(virtualPath, userId, recycleService, cancellationToken),
            "MOVE" => await MoveAsync(context, virtualPath, userId, browseService, transferService, cancellationToken),
            "COPY" => await CopyAsync(context, virtualPath, userId, browseService, transferService, cancellationToken),
            "PROPPATCH" => Results.StatusCode(StatusCodes.Status403Forbidden),
            _ => MethodNotAllowed(context)
        };
    }

    // -------------------------------------------------------------------------
    // 认证
    // -------------------------------------------------------------------------

    private static async Task<(ClaimsPrincipal? Principal, IResult? Failure)> AuthenticateAsync(
        HttpContext context,
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IUnitOfWork unitOfWork,
        IWebDavAuthGuard authGuard,
        CancellationToken cancellationToken)
    {
        // 测试与反代场景下 RemoteIpAddress 可能为空，统一归到 unknown 键
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (authGuard.IsLockedOut(ip))
        {
            return (null, Results.StatusCode(StatusCodes.Status429TooManyRequests));
        }

        if (!context.Request.Headers.TryGetValue("Authorization", out var authHeaderValues)
            || !System.Net.Http.Headers.AuthenticationHeaderValue.TryParse(authHeaderValues.ToString(), out var auth)
            || !string.Equals(auth.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(auth.Parameter))
        {
            return (null, Challenge());
        }

        string username;
        string password;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(auth.Parameter));
            var separator = decoded.IndexOf(':');
            if (separator <= 0)
            {
                return (null, Challenge());
            }
            username = decoded[..separator];
            password = decoded[(separator + 1)..];
        }
        catch (FormatException)
        {
            return (null, Challenge());
        }

        var user = await userRepository.GetByUsernameAsync(username, cancellationToken);
        // 用户不存在与密码错误同回 401，防用户名枚举
        if (user is null || !user.IsEnabled
            || !passwordHasher.VerifyPassword(password, user.PasswordHash, user.PasswordSalt))
        {
            authGuard.RecordFailure(ip);
            return (null, Challenge());
        }

        authGuard.Reset(ip);

        // 最后登录时间节流更新：WebDAV 每个目录浏览都带认证，不节流就是每请求
        // 一次 User 写。超过一小时未记录才写，既保"最后登录"语义又不放大写
        if (user.LastLoginAt is null || DateTime.UtcNow - user.LastLoginAt.Value > TimeSpan.FromHours(1))
        {
            user.RecordLogin();
            userRepository.UpdateAsync(user, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role.ToString())
            ],
            "Basic");
        return (new ClaimsPrincipal(identity), null);
    }

    private static IResult Challenge()
    {
        return new WwwAuthenticateResult("Basic realm=\"NAS WebDAV\", charset=\"UTF-8\"");
    }

    private sealed class WwwAuthenticateResult(string headerValue) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            httpContext.Response.Headers["WWW-Authenticate"] = headerValue;
            return Task.CompletedTask;
        }
    }

    // -------------------------------------------------------------------------
    // OPTIONS / 405
    // -------------------------------------------------------------------------

    private static IResult HandleOptions(HttpContext context)
    {
        context.Response.Headers["Allow"] = string.Join(", ", SupportedMethods);
        // DAV class 1：PROPFIND/LOCK 之外的 class 2 能力（正式锁）不支持
        context.Response.Headers.Append("DAV", "1");
        context.Response.Headers.Append("MS-Author-Via", "MS-FP");
        return Results.StatusCode(StatusCodes.Status200OK);
    }

    private static IResult MethodNotAllowed(HttpContext context)
    {
        context.Response.Headers["Allow"] = string.Join(", ", SupportedMethods);
        return Results.StatusCode(StatusCodes.Status405MethodNotAllowed);
    }

    // -------------------------------------------------------------------------
    // PROPFIND
    // -------------------------------------------------------------------------

    private static async Task<IResult> PropFindAsync(
        HttpContext context,
        string path,
        Guid userId,
        IFileBrowseService browseService,
        CancellationToken cancellationToken)
    {
        var depth = context.Request.Headers["Depth"].ToString();

        // Depth: infinity 钳成 1：全树展开对万级目录是响应灾难，
        // 主流客户端列目录一律显式发 Depth: 1
        var includeChildren = depth.Equals("1", StringComparison.OrdinalIgnoreCase)
            || depth.Equals("infinity", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(depth);

        // 非根路径先验证存在性与类型：GetDirectoryContentsAsync 对不存在路径
        // 会把父 id 当 null 查，静默返回根目录内容（网页端可容忍，PROPFIND
        // 不行——客户端必须拿到 404 而不是错把根目录当目标）
        if (path != "/")
        {
            var info = await browseService.GetFileInfoAsync(path, userId, cancellationToken);
            if (info.IsFailure)
            {
                return BuildDavXml(Array.Empty<FileItemDto>(), notFound: true);
            }
            if (!info.Value.IsFolder)
            {
                // 单文件：Depth 对非集合无意义，只回自身
                return BuildDavXml([info.Value]);
            }
        }

        var dirResult = await browseService.GetDirectoryContentsAsync(path, userId, includeHidden: false, cancellationToken);
        if (dirResult.IsSuccess)
        {
            var content = dirResult.Value;
            // 目录自身没有 UpdatedAt 载荷（DirectoryContentDto 不含该字段），
            // 取子项最新时间戳聚合：空目录退化为当前时间。子项列表已物化，
            // 这次聚合不产生额外查询
            var latest = content.Items.Count > 0
                ? content.Items.Max(i => i.UpdatedAt ?? i.CreatedAt)
                : DateTime.UtcNow;
            var entries = new List<FileItemDto> { ToCollectionItem(content.Path, content.Name, latest) };
            if (includeChildren)
            {
                entries.AddRange(content.Items);
            }
            return BuildDavXml(entries);
        }

        return BuildDavXml(Array.Empty<FileItemDto>(), notFound: true);
    }

    /// <summary>
    /// 目录自报项：集合目录在 PROPFIND 里也要有一条 response。
    /// Id/OwnerId 置空：XML 输出只用名称、类型、时间戳
    /// </summary>
    private static FileItemDto ToCollectionItem(string path, string name, DateTime updatedAt)
    {
        return new FileItemDto
        {
            Id = Guid.Empty,
            Name = name,
            Path = path,
            ItemType = NasSystem.Domain.Enums.FileItemType.Folder,
            SizeBytes = 0,
            MimeType = null,
            Extension = null,
            ParentId = null,
            OwnerId = Guid.Empty,
            IsHidden = false,
            IsReadOnly = false,
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt
        };
    }

    // -------------------------------------------------------------------------
    // GET / HEAD
    // -------------------------------------------------------------------------

    private static async Task<IResult> GetAsync(
        HttpContext context,
        string path,
        Guid userId,
        IFileBrowseService browseService,
        IFileTransferService transferService,
        CancellationToken cancellationToken)
    {
        var info = await browseService.GetFileInfoAsync(path, userId, cancellationToken);
        if (info.IsFailure)
        {
            return Results.NotFound();
        }
        if (info.Value.IsFolder)
        {
            // 集合目录上的 GET 未定义，客户端不应对目录发 GET
            return MethodNotAllowed(context);
        }

        var result = await transferService.DownloadFileAsync(path, userId, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToHttpResult();
        }

        var (stream, fileName, contentType) = result.Value;
        // enableRangeProcessing：资源管理器大文件拖动、断点续传、视频拖动
        return Results.File(stream, contentType, fileName, enableRangeProcessing: true);
    }

    private static async Task<IResult> HeadAsync(
        HttpContext context,
        string path,
        Guid userId,
        IFileBrowseService browseService,
        CancellationToken cancellationToken)
    {
        var info = await browseService.GetFileInfoAsync(path, userId, cancellationToken);
        if (info.IsFailure)
        {
            return Results.NotFound();
        }
        if (info.Value.IsFolder)
        {
            return MethodNotAllowed(context);
        }

        var item = info.Value;
        context.Response.ContentType = item.MimeType ?? "application/octet-stream";
        context.Response.ContentLength = item.SizeBytes;
        context.Response.Headers.ETag = $"\"{EtagValue(item)}\"";
        context.Response.Headers["Last-Modified"] = FormatRfc1123(item.UpdatedAt ?? item.CreatedAt);
        return Results.StatusCode(StatusCodes.Status200OK);
    }

    // -------------------------------------------------------------------------
    // PUT
    // -------------------------------------------------------------------------

    private static async Task<IResult> PutAsync(
        HttpContext context,
        string path,
        Guid userId,
        IFileBrowseService browseService,
        IFileTransferService transferService,
        CancellationToken cancellationToken)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            // 对集合根 PUT 无意义
            return MethodNotAllowed(context);
        }

        var fileName = segments[^1];
        var parentPath = segments.Length > 1 ? "/" + string.Join('/', segments[..^1]) : "/";

        // 放开 Kestrel 全局 500MB 限制：资源管理器/Finder 不知道网页上传的
        // 500MB 约定，大文件直传是挂载场景的核心诉求。配额检查在服务层
        // UploadFileAsync 内仍然生效，不受此处影响。
        // 空值判定：TestServer 不挂 Kestrel 的该 feature，生产 Kestrel 必有
        context.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize = null;

        var existing = await browseService.GetFileInfoAsync(path, userId, cancellationToken);

        var metadata = new UploadFileMetadata
        {
            FileName = fileName,
            DestinationPath = parentPath,
            // 流式上传拿不到声明大小：容量预检走余量模式（同网页上传管道）
            FileSize = 0,
            ContentType = context.Request.ContentType,
            Overwrite = true
        };

        var result = await transferService.UploadFileAsync(metadata, context.Request.Body, userId, cancellationToken);
        if (result.IsFailure)
        {
            // RFC 4918：配额/容量耗尽回 507 Insufficient Storage。
            // 客户端（尤其资源管理器）据此把文件留在原地重试，
            // 回 400/500 会被当成协议错误直接放弃
            return result.Error.Code is "User.QuotaExceeded" or "Storage.InsufficientSpace"
                ? Results.StatusCode(StatusCodes.Status507InsufficientStorage)
                : result.ToHttpResult();
        }

        // RFC 4918：新资源 201，覆盖已存在资源 204
        return Results.StatusCode(existing.IsSuccess
            ? StatusCodes.Status204NoContent
            : StatusCodes.Status201Created);
    }

    // -------------------------------------------------------------------------
    // MKCOL
    // -------------------------------------------------------------------------

    private static async Task<IResult> MkColAsync(
        HttpContext context,
        string path,
        Guid userId,
        IFileTransferService transferService,
        CancellationToken cancellationToken)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return MethodNotAllowed(context);
        }

        // MKCOL 不允许带 body
        if (context.Request.ContentLength is > 0)
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        var request = new CreateFolderRequest
        {
            ParentPath = segments.Length > 1 ? "/" + string.Join('/', segments[..^1]) : "/",
            Name = segments[^1]
        };
        var result = await transferService.CreateFolderAsync(request, userId, cancellationToken);
        if (result.IsFailure)
        {
            // 目标已存在按 RFC 回 405（而非 409），Finder 据此走"已存在"分支
            return result.Error.Code.Contains("Exists", StringComparison.OrdinalIgnoreCase)
                || result.Error.Code.Contains("Conflict", StringComparison.OrdinalIgnoreCase)
                ? MethodNotAllowed(context)
                : result.ToHttpResult();
        }
        return Results.StatusCode(StatusCodes.Status201Created);
    }

    // -------------------------------------------------------------------------
    // DELETE（软删除进回收站，与网页端口径一致）
    // -------------------------------------------------------------------------

    private static async Task<IResult> DeleteAsync(
        string path,
        Guid userId,
        IFileRecycleService recycleService,
        CancellationToken cancellationToken)
    {
        var request = new DeleteItemsRequest
        {
            Paths = [path],
            Permanent = false
        };
        var result = await recycleService.DeleteAsync(request, userId, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToHttpResult();
        }
        return Results.StatusCode(StatusCodes.Status204NoContent);
    }

    // -------------------------------------------------------------------------
    // MOVE / COPY
    // -------------------------------------------------------------------------

    private static async Task<IResult> MoveAsync(
        HttpContext context,
        string sourcePath,
        Guid userId,
        IFileBrowseService browseService,
        IFileTransferService transferService,
        CancellationToken cancellationToken)
    {
        var destinationPath = ParseDestination(context);
        if (destinationPath is null)
        {
            return Results.BadRequest();
        }

        var overwrite = context.Request.Headers["Overwrite"].ToString()
            .Equals("T", StringComparison.OrdinalIgnoreCase);

        var destinationExists = await browseService.GetFileInfoAsync(destinationPath, userId, cancellationToken);
        if (destinationExists.IsSuccess && !overwrite)
        {
            // RFC 4918 412：目标已存在且未声明覆盖
            return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        var result = await ApplyMoveOrCopyAsync(sourcePath, destinationPath, userId, transferService, isMove: true, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToHttpResult();
        }

        return Results.StatusCode(destinationExists.IsSuccess
            ? StatusCodes.Status204NoContent
            : StatusCodes.Status201Created);
    }

    private static async Task<IResult> CopyAsync(
        HttpContext context,
        string sourcePath,
        Guid userId,
        IFileBrowseService browseService,
        IFileTransferService transferService,
        CancellationToken cancellationToken)
    {
        var destinationPath = ParseDestination(context);
        if (destinationPath is null)
        {
            return Results.BadRequest();
        }

        var overwrite = context.Request.Headers["Overwrite"].ToString()
            .Equals("T", StringComparison.OrdinalIgnoreCase);

        var destinationExists = await browseService.GetFileInfoAsync(destinationPath, userId, cancellationToken);
        if (destinationExists.IsSuccess && !overwrite)
        {
            return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        var result = await ApplyMoveOrCopyAsync(sourcePath, destinationPath, userId, transferService, isMove: false, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToHttpResult();
        }

        return Results.StatusCode(destinationExists.IsSuccess
            ? StatusCodes.Status204NoContent
            : StatusCodes.Status201Created);
    }

    /// <summary>
    /// 同目录换名走 RenameAsync（目标路径即新文件全路径）；
    /// 跨目录走 Move/Copy（DestinationPath 语义是目标目录）
    /// </summary>
    private static async Task<Result> ApplyMoveOrCopyAsync(
        string sourcePath,
        string destinationPath,
        Guid userId,
        IFileTransferService transferService,
        bool isMove,
        CancellationToken cancellationToken)
    {
        var sourceParent = ParentOf(sourcePath);
        var destinationParent = ParentOf(destinationPath);

        if (string.Equals(sourceParent, destinationParent, StringComparison.Ordinal))
        {
            var renameResult = await transferService.RenameAsync(
                new RenameItemRequest { Path = sourcePath, NewName = NameOf(destinationPath) },
                userId,
                cancellationToken);
            return renameResult.ToResult();
        }

        var request = new MoveOrCopyRequest
        {
            SourcePaths = [sourcePath],
            DestinationPath = destinationParent
        };
        var result = isMove
            ? await transferService.MoveAsync(request, userId, cancellationToken)
            : await transferService.CopyAsync(request, userId, cancellationToken);
        return result.ToResult();
    }

    // -------------------------------------------------------------------------
    // 路径与 XML 辅助
    // -------------------------------------------------------------------------

    /// <summary>
    /// WebDAV href 路径规范化：逐段 URL 解码（整条解码会把 %2F 解成真斜杠，
    /// 制造伪目录层级），折叠 "." 与 ".."，.. 不允许越过根
    /// </summary>
    private static string NormalizeDavPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "/";
        }

        var stack = new List<string>();
        foreach (var rawSegment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var segment = Uri.UnescapeDataString(rawSegment);
            if (segment == ".")
            {
                continue;
            }
            if (segment == "..")
            {
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }
                continue;
            }
            stack.Add(segment);
        }

        return "/" + string.Join('/', stack);
    }

    /// <summary>
    /// 解析 Destination 头：允许完整 URL（http://host/dav/...）或绝对路径（/dav/...）。
    /// 非本服务前缀的目标拒绝，防止客户端把文件 MOVE 到服务外路径
    /// </summary>
    private static string? ParseDestination(HttpContext context)
    {
        var header = context.Request.Headers["Destination"].ToString();
        if (string.IsNullOrEmpty(header))
        {
            return null;
        }

        string rawPath;
        if (Uri.TryCreate(header, UriKind.Absolute, out var absolute))
        {
            rawPath = absolute.AbsolutePath;
        }
        else
        {
            rawPath = header;
        }

        if (!rawPath.StartsWith(DavPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var virtualPath = NormalizeDavPath(rawPath[DavPrefix.Length..]);
        return virtualPath == "/" ? null : virtualPath;
    }

    private static string ParentOf(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 1 ? "/" + string.Join('/', segments[..^1]) : "/";
    }

    private static string NameOf(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 ? segments[^1] : string.Empty;
    }

    private static string EtagValue(FileItemDto item)
    {
        var stamp = item.UpdatedAt?.Ticks.ToString(CultureInfo.InvariantCulture) ?? item.Id.ToString();
        return stamp;
    }

    /// <summary>
    /// RFC 1123 时间。DB 时间戳按 UTC 存取（全应用 DateTime.UtcNow 口径），
    /// Kind 为 Unspecified，显式指定为 Utc 避免本地时间被误标 GMT
    /// </summary>
    private static string FormatRfc1123(DateTime dateTime)
    {
        var utc = dateTime.Kind == DateTimeKind.Utc
            ? dateTime
            : DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
        return utc.ToString("ddd, dd MMM yyyy HH':'mm':'ss 'GMT'", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 生成 207 Multi-Status XML。notFound 时生成 RFC 4918 规定的
    /// 单条目 404 multistatus（客户端按 multistatus 解析，不回裸 404）
    /// </summary>
    private static IResult BuildDavXml(IReadOnlyList<FileItemDto> items, bool notFound = false)
    {
        var stream = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            Async = false
        };

        using (var writer = XmlWriter.Create(stream, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("D", "multistatus", "DAV:");

            if (notFound)
            {
                writer.WriteStartElement("D", "response", "DAV:");
                writer.WriteElementString("D", "status", "DAV:", "HTTP/1.1 404 Not Found");
                writer.WriteEndElement();
            }
            else
            {
                foreach (var item in items)
                {
                    WriteResponseElement(writer, item);
                }
            }

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return new DavXmlResult(stream.ToArray());
    }

    private static void WriteResponseElement(XmlWriter writer, FileItemDto item)
    {
        writer.WriteStartElement("D", "response", "DAV:");
        writer.WriteElementString("D", "href", "DAV:", BuildHref(item));
        writer.WriteStartElement("D", "propstat", "DAV:");
        writer.WriteStartElement("D", "prop", "DAV:");
        writer.WriteElementString("D", "displayname", "DAV:", item.Name);
        writer.WriteElementString("D", "getcontentlength", "DAV:", item.SizeBytes.ToString(CultureInfo.InvariantCulture));
        if (!item.IsFolder)
        {
            writer.WriteElementString("D", "getcontenttype", "DAV:", item.MimeType ?? "application/octet-stream");
        }
        writer.WriteElementString("D", "getlastmodified", "DAV:", FormatRfc1123(item.UpdatedAt ?? item.CreatedAt));
        writer.WriteElementString("D", "getetag", "DAV:", $"\"{EtagValue(item)}\"");
        writer.WriteStartElement("D", "resourcetype", "DAV:");
        if (item.IsFolder)
        {
            writer.WriteElementString("D", "collection", "DAV:", string.Empty);
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteElementString("D", "status", "DAV:", "HTTP/1.1 200 OK");
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    /// <summary>
    /// 构造 href：逐段转义（文件名含空格、中文、# 等字符时客户端才能正确还原）。
    /// 根目录特判：encode 后为空段，直接拼会出 /dav// 双斜杠
    /// </summary>
    private static string BuildHref(FileItemDto item)
    {
        if (item.Path == "/")
        {
            return $"{DavPrefix}/";
        }

        var encoded = string.Join('/', item.Path
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));
        return item.IsFolder ? $"{DavPrefix}/{encoded}/" : $"{DavPrefix}/{encoded}";
    }

    private sealed class DavXmlResult(byte[] payload) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = StatusCodes.Status207MultiStatus;
            httpContext.Response.ContentType = "application/xml; charset=utf-8";
            return httpContext.Response.Body.WriteAsync(payload).AsTask();
        }
    }
}
