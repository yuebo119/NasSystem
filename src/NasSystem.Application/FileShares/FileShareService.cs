// =============================================================================
// NasSystem.Application - FileShareService.cs
// 文件分享服务实现
// =============================================================================

namespace NasSystem.Application.FileShares;

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using NasSystem.Application.Common;
using NasSystem.Application.FileShares.Dtos;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using NasSystem.Domain.Services;
using NasSystem.Domain.ValueObjects;
using NasSystem.Shared;
using static NasSystem.Shared.ByteSizeFormatter;

/// <summary>
/// 文件分享服务实现
/// </summary>
public sealed class FileShareService(
    IFileShareRepository fileShareRepository,
    IFileItemRepository fileItemRepository,
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    IOptions<JwtSettings> jwtOptions) : IFileShareService
{
    // 访问令牌有效期（小时）
    private const int AccessTokenExpirationHours = 24;

    // 访问令牌签名密钥（复用 JWT 主密钥，ITM-N1：令牌必须带签名防伪造）
    private readonly byte[] _tokenKey = Encoding.UTF8.GetBytes(jwtOptions.Value.SecretKey);

    #region 管理 API

    public async Task<Result<FileShareDto>> CreateAsync(
        CreateFileShareRequest request,
        Guid userId,
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        // 验证文件是否存在
        var fileItem = await fileItemRepository.GetByIdAsync(request.FileItemId, cancellationToken);
        if (fileItem is null)
            return Result<FileShareDto>.Failure(FileShareErrors.FileNotFound(request.FileItemId));

        // 处理密码
        string? passwordHash = null;
        if (request.AccessType == ShareAccessType.Password && !string.IsNullOrEmpty(request.Password))
        {
            var (hash, salt) = passwordHasher.HashPassword(request.Password);
            passwordHash = $"{hash}:{salt}";
        }

        // 创建分享
        var share = FileShare.Create(
            fileItemId: request.FileItemId,
            createdByUserId: userId,
            accessType: request.AccessType,
            passwordHash: passwordHash,
            permission: request.Permission,
            expiresAt: request.ExpiresAt,
            maxDownloads: request.MaxDownloads);

        // 确保分享码唯一
        while (await fileShareRepository.ShareCodeExistsAsync(share.ShareCode, cancellationToken))
        {
            share.RegenerateShareCode();
        }

        await fileShareRepository.AddAsync(share, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException)
        {
            // ITM-N7：并发生成相同分享码由 ShareCode 唯一索引兜底
            share.RegenerateShareCode();
            await fileShareRepository.UpdateAsync(share, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<FileShareDto>.Success(await MapToDtoAsync(share, fileItem, baseUrl, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<FileShareDto>>> GetMySharesAsync(
        Guid userId,
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        var shares = await fileShareRepository.GetByCreatorAsync(userId, cancellationToken);
        var dtos = new List<FileShareDto>();

        foreach (var share in shares)
        {
            var fileItem = await fileItemRepository.GetByIdAsync(share.FileItemId, cancellationToken);
            if (fileItem != null)
            {
                dtos.Add(await MapToDtoAsync(share, fileItem, baseUrl, cancellationToken));
            }
        }

        return Result<IReadOnlyList<FileShareDto>>.Success(dtos);
    }

    public async Task<Result<FileShareDto>> GetByIdAsync(
        Guid shareId,
        Guid userId,
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        var share = await fileShareRepository.GetByIdAsync(shareId, cancellationToken);
        if (share is null)
            return Result<FileShareDto>.Failure(FileShareErrors.NotFound(shareId));

        // 只有创建者可以查看详情
        if (share.CreatedByUserId != userId)
            return Result<FileShareDto>.Failure(FileShareErrors.AccessDenied());

        var fileItem = await fileItemRepository.GetByIdAsync(share.FileItemId, cancellationToken);
        if (fileItem is null)
            return Result<FileShareDto>.Failure(FileShareErrors.FileNotFound(share.FileItemId));

        return Result<FileShareDto>.Success(await MapToDtoAsync(share, fileItem, baseUrl, cancellationToken));
    }


    public async Task<Result<FileShareDto>> UpdateAsync(
        Guid shareId,
        UpdateFileShareRequest request,
        Guid userId,
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        var share = await fileShareRepository.GetByIdAsync(shareId, cancellationToken);
        if (share is null)
            return Result<FileShareDto>.Failure(FileShareErrors.NotFound(shareId));

        if (share.CreatedByUserId != userId)
            return Result<FileShareDto>.Failure(FileShareErrors.AccessDenied());

        // 处理密码更新
        string? passwordHash = null;
        if (request.ClearPassword == true)
        {
            share.ClearPassword();
        }
        else if (!string.IsNullOrEmpty(request.Password))
        {
            var (hash, salt) = passwordHasher.HashPassword(request.Password);
            passwordHash = $"{hash}:{salt}";
        }

        // 处理过期时间
        DateTime? expiresAt = share.ExpiresAt;
        if (request.ClearExpiresAt == true)
            expiresAt = null;
        else if (request.ExpiresAt.HasValue)
            expiresAt = request.ExpiresAt;

        // 处理下载限制
        int? maxDownloads = share.MaxDownloads;
        if (request.ClearMaxDownloads == true)
            maxDownloads = null;
        else if (request.MaxDownloads.HasValue)
            maxDownloads = request.MaxDownloads;

        share.Update(
            accessType: request.AccessType,
            passwordHash: passwordHash,
            permission: request.Permission,
            expiresAt: expiresAt,
            maxDownloads: maxDownloads);

        if (request.IsEnabled.HasValue)
            share.SetEnabled(request.IsEnabled.Value);

        await fileShareRepository.UpdateAsync(share, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var fileItem = await fileItemRepository.GetByIdAsync(share.FileItemId, cancellationToken);
        return Result<FileShareDto>.Success(await MapToDtoAsync(share, fileItem!, baseUrl, cancellationToken));
    }

    public async Task<Result> DeleteAsync(
        Guid shareId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var share = await fileShareRepository.GetByIdAsync(shareId, cancellationToken);
        if (share is null)
            return Result.Failure(FileShareErrors.NotFound(shareId));

        if (share.CreatedByUserId != userId)
            return Result.Failure(FileShareErrors.AccessDenied());

        await fileShareRepository.DeleteAsync(share, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<FileShareDto>> RegenerateShareCodeAsync(
        Guid shareId,
        Guid userId,
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        var share = await fileShareRepository.GetByIdAsync(shareId, cancellationToken);
        if (share is null)
            return Result<FileShareDto>.Failure(FileShareErrors.NotFound(shareId));

        if (share.CreatedByUserId != userId)
            return Result<FileShareDto>.Failure(FileShareErrors.AccessDenied());

        share.RegenerateShareCode();

        // 确保分享码唯一
        while (await fileShareRepository.ShareCodeExistsAsync(share.ShareCode, cancellationToken))
        {
            share.RegenerateShareCode();
        }

        await fileShareRepository.UpdateAsync(share, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var fileItem = await fileItemRepository.GetByIdAsync(share.FileItemId, cancellationToken);
        return Result<FileShareDto>.Success(await MapToDtoAsync(share, fileItem!, baseUrl, cancellationToken));
    }

    #endregion

    #region 公开访问 API

    public async Task<Result<PublicShareInfoDto>> GetPublicShareInfoAsync(
        string shareCode,
        CancellationToken cancellationToken = default)
    {
        var share = await fileShareRepository.GetByShareCodeAsync(shareCode, cancellationToken);
        if (share is null)
            return Result<PublicShareInfoDto>.Failure(FileShareErrors.ShareCodeNotFound(shareCode));

        var fileItem = await fileItemRepository.GetByIdAsync(share.FileItemId, cancellationToken);
        if (fileItem is null)
            return Result<PublicShareInfoDto>.Failure(FileShareErrors.FileNotFound(share.FileItemId));

        // 获取创建者名称
        string? creatorName = null;
        var creator = await userRepository.GetByIdAsync(share.CreatedByUserId, cancellationToken);
        if (creator != null)
            creatorName = creator.DisplayName ?? creator.Username;

        return Result<PublicShareInfoDto>.Success(new PublicShareInfoDto
        {
            ShareCode = share.ShareCode,
            FileName = fileItem.Name,
            IsFolder = fileItem.IsFolder,
            SizeBytes = fileItem.IsFile ? fileItem.SizeBytes : null,
            SizeDisplay = fileItem.IsFile ? FormatSize(fileItem.SizeBytes) : null,
            AccessType = share.AccessType,
            Permission = share.Permission,
            RequiresPassword = share.RequiresPassword,
            IsExpired = share.IsExpired,
            IsDownloadLimitReached = share.IsDownloadLimitReached,
            IsAccessible = share.IsAccessible,
            CreatedByUserName = creatorName
        });
    }

    public async Task<Result<string>> VerifyPasswordAsync(
        string shareCode,
        string password,
        CancellationToken cancellationToken = default)
    {
        var share = await fileShareRepository.GetByShareCodeAsync(shareCode, cancellationToken);
        if (share is null)
            return Result<string>.Failure(FileShareErrors.ShareCodeNotFound(shareCode));

        if (!share.IsAccessible)
            return Result<string>.Failure(FileShareErrors.ShareNotAccessible());

        if (share.AccessType != ShareAccessType.Password || string.IsNullOrEmpty(share.PasswordHash))
            return Result<string>.Failure(FileShareErrors.PasswordNotRequired());

        // 解析密码哈希
        var parts = share.PasswordHash.Split(':');
        if (parts.Length != 2)
            return Result<string>.Failure(FileShareErrors.InvalidPassword());

        var (hash, salt) = (parts[0], parts[1]);
        if (!passwordHasher.VerifyPassword(password, hash, salt))
            return Result<string>.Failure(FileShareErrors.InvalidPassword());

        // 生成访问令牌
        var token = GenerateAccessToken(shareCode);
        return Result<string>.Success(token);
    }


    public async Task<Result<ShareDownloadTarget>> GetDownloadStreamAsync(
        string shareCode,
        string? accessToken,
        string? subPath,
        bool isAuthenticated = false,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadShareForPublicAccessAsync(shareCode, accessToken, isAuthenticated, cancellationToken);
        if (loaded.IsFailure)
            return Result<ShareDownloadTarget>.Failure(loaded.Error);
        var (share, fileItem) = loaded.Value;

        // 确定要下载的文件
        FileItem targetFile;
        if (fileItem.IsFolder && !string.IsNullOrEmpty(subPath))
        {
            var resolved = await ResolveShareSubPathAsync(fileItem.Path, subPath, cancellationToken);
            if (resolved.IsFailure)
                return Result<ShareDownloadTarget>.Failure(resolved.Error);

            var target = await fileItemRepository.GetByPathAsync(resolved.Value, cancellationToken);
            if (target is null)
                return Result<ShareDownloadTarget>.Failure(FileShareErrors.FileNotFound(Guid.Empty));

            // 如果是子文件夹，也打包下载
            if (target.IsFolder)
            {
                return await CreateZipDownloadAsync(share, target, cancellationToken);
            }
            targetFile = target;
        }
        else if (fileItem.IsFile)
        {
            targetFile = fileItem;
        }
        else
        {
            // 文件夹打包下载
            return await CreateZipDownloadAsync(share, fileItem, cancellationToken);
        }

        // 检查物理文件是否存在
        if (!File.Exists(targetFile.PhysicalPath))
            return Result<ShareDownloadTarget>.Failure(FileShareErrors.PhysicalFileNotFound());

        // 增加下载计数
        share.IncrementDownloadCount();
        await fileShareRepository.UpdateAsync(share, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var stream = new FileStream(targetFile.PhysicalPath, FileMode.Open, FileAccess.Read, System.IO.FileShare.Read);
        var contentType = targetFile.MimeType ?? "application/octet-stream";

        return Result<ShareDownloadTarget>.Success(new ShareDownloadTarget
        {
            Stream = stream,
            FileName = targetFile.Name,
            ContentType = contentType
        });
    }

    /// <summary>
    /// 公开访问前置加载：分享码取记录 + 访问校验 + 关联文件项（下载/浏览/子路径三处共用的序言）
    /// </summary>
    private async Task<Result<(FileShare Share, FileItem FileItem)>> LoadShareForPublicAccessAsync(
        string shareCode,
        string? accessToken,
        bool isAuthenticated,
        CancellationToken cancellationToken)
    {
        var share = await fileShareRepository.GetByShareCodeAsync(shareCode, cancellationToken);
        if (share is null)
            return Result<(FileShare, FileItem)>.Failure(FileShareErrors.ShareCodeNotFound(shareCode));

        var accessResult = ValidateAccess(share, accessToken, isAuthenticated);
        if (!accessResult.IsSuccess)
            return Result<(FileShare, FileItem)>.Failure(accessResult.Error);

        var fileItem = await fileItemRepository.GetByIdAsync(share.FileItemId, cancellationToken);
        if (fileItem is null)
            return Result<(FileShare, FileItem)>.Failure(FileShareErrors.FileNotFound(share.FileItemId));

        return Result<(FileShare, FileItem)>.Success((share, fileItem));
    }

    /// <summary>
    /// ITM-N16：subPath 规范化（解析 ..）后必须仍落在分享目录内，否则拒绝。
    /// 下载与文件夹浏览两条路径共用同一防穿越判定
    /// </summary>
    private static async Task<Result<string>> ResolveShareSubPathAsync(
        string shareRootPath,
        string subPath,
        CancellationToken cancellationToken)
    {
        var shareRoot = FilePath.Create(shareRootPath);
        var resolved = FilePath.Create(shareRoot.Combine(subPath).Value);
        if (!resolved.IsUnder(shareRoot))
            return Result<string>.Failure(FileShareErrors.AccessDenied());

        return Result<string>.Success(resolved.Value);
    }

    /// <summary>
    /// 文件夹 ZIP 下载准备：校验物理目录并登记下载计数。
    /// 压缩流由 Api 层 ZipDirectoryResult 直接写入响应体——原实现先整体打进
    /// MemoryStream 再返回，匿名公开端点被大目录触发时会 OOM
    /// </summary>
    private async Task<Result<ShareDownloadTarget>> CreateZipDownloadAsync(
        FileShare share,
        FileItem folder,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folder.PhysicalPath))
            return Result<ShareDownloadTarget>.Failure(FileShareErrors.PhysicalFileNotFound());

        share.IncrementDownloadCount();
        await fileShareRepository.UpdateAsync(share, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<ShareDownloadTarget>.Success(new ShareDownloadTarget
        {
            ZipDirectory = folder.PhysicalPath,
            FileName = $"{folder.Name}.zip",
            ContentType = "application/zip"
        });
    }

    public async Task<Result<ShareFolderContentDto>> GetFolderContentAsync(
        string shareCode,
        string? accessToken,
        string? subPath,
        bool isAuthenticated = false,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadShareForPublicAccessAsync(shareCode, accessToken, isAuthenticated, cancellationToken);
        if (loaded.IsFailure)
            return Result<ShareFolderContentDto>.Failure(loaded.Error);
        var (share, fileItem) = loaded.Value;

        if (!fileItem.IsFolder)
            return Result<ShareFolderContentDto>.Failure(FileShareErrors.NotAFolder());

        // 确定目标目录
        Guid? targetParentId = fileItem.Id;
        string currentPath = "/";

        if (!string.IsNullOrEmpty(subPath))
        {
            var resolved = await ResolveShareSubPathAsync(fileItem.Path, subPath, cancellationToken);
            if (resolved.IsFailure)
                return Result<ShareFolderContentDto>.Failure(resolved.Error);

            var targetFolder = await fileItemRepository.GetByPathAsync(resolved.Value, cancellationToken);
            if (targetFolder is null || !targetFolder.IsFolder)
                return Result<ShareFolderContentDto>.Failure(FileShareErrors.FolderNotFound());
            targetParentId = targetFolder.Id;
            currentPath = subPath;
        }

        // 获取子项
        var children = await fileItemRepository.GetChildrenAsync(targetParentId, false, cancellationToken);
        var items = children.Select(c => new ShareFileItemDto
        {
            Id = c.Id,
            Name = c.Name,
            IsFolder = c.IsFolder,
            SizeBytes = c.IsFile ? c.SizeBytes : null,
            SizeDisplay = c.IsFile ? FormatSize(c.SizeBytes) : null,
            MimeType = c.MimeType,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt
        }).ToList();

        return Result<ShareFolderContentDto>.Success(new ShareFolderContentDto
        {
            ShareCode = shareCode,
            CurrentPath = currentPath,
            Items = items
        });
    }

    #endregion

    #region 辅助方法

    private Result ValidateAccess(FileShare share, string? accessToken, bool isAuthenticated = false)
    {
        if (!share.IsAccessible)
            return Result.Failure(FileShareErrors.ShareNotAccessible());

        switch (share.AccessType)
        {
            case ShareAccessType.Public:
                return Result.Success();

            case ShareAccessType.Password:
                if (string.IsNullOrEmpty(accessToken))
                    return Result.Failure(FileShareErrors.PasswordRequired());
                if (!ValidateAccessToken(share.ShareCode, accessToken))
                    return Result.Failure(FileShareErrors.InvalidAccessToken());
                return Result.Success();

            case ShareAccessType.Internal:
                // ITM-N17：Internal 分享必须由已认证用户访问——公开匿名端点传 isAuthenticated=false
                // 直接拒绝，防止"仅内部可见"分享经由公开路由全网可下
                if (!isAuthenticated)
                    return Result.Failure(FileShareErrors.LoginRequired());
                return Result.Success();

            default:
                return Result.Failure(FileShareErrors.InvalidAccessType());
        }
    }

    /// <summary>
    /// 生成带 HMAC-SHA256 签名的访问令牌（ITM-N1：Base64 明文拼接可被任意伪造，必须签名）
    /// </summary>
    private string GenerateAccessToken(string shareCode)
    {
        var expiration = DateTime.UtcNow.AddHours(AccessTokenExpirationHours);
        var payload = $"{shareCode}:{expiration:O}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var signature = HMACSHA256.HashData(_tokenKey, payloadBytes);
        return $"{Convert.ToBase64String(payloadBytes)}.{Convert.ToBase64String(signature)}";
    }

    private bool ValidateAccessToken(string shareCode, string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 2) return false;

            var payloadBytes = Convert.FromBase64String(parts[0]);
            var expectedSignature = HMACSHA256.HashData(_tokenKey, payloadBytes);
            var actualSignature = Convert.FromBase64String(parts[1]);
            if (!CryptographicOperations.FixedTimeEquals(expectedSignature, actualSignature))
                return false;

            var data = Encoding.UTF8.GetString(payloadBytes);
            var separatorIndex = data.IndexOf(':');
            if (separatorIndex <= 0) return false;

            var tokenShareCode = data[..separatorIndex];
            var expirationStr = data[(separatorIndex + 1)..];

            if (tokenShareCode != shareCode) return false;
            if (!DateTime.TryParse(expirationStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var expiration))
                return false;

            return expiration > DateTime.UtcNow;
        }
        catch
        {
            return false;
        }
    }

    private async Task<FileShareDto> MapToDtoAsync(
        FileShare share,
        FileItem fileItem,
        string baseUrl,
        CancellationToken cancellationToken)
    {
        string? creatorName = null;
        var creator = await userRepository.GetByIdAsync(share.CreatedByUserId, cancellationToken);
        if (creator != null)
            creatorName = creator.DisplayName ?? creator.Username;

        return new FileShareDto
        {
            Id = share.Id,
            ShareCode = share.ShareCode,
            ShareUrl = $"{baseUrl.TrimEnd('/')}/s/{share.ShareCode}",
            FileItemId = share.FileItemId,
            FileName = fileItem.Name,
            FilePath = fileItem.Path,
            IsFolder = fileItem.IsFolder,
            SizeBytes = fileItem.IsFile ? fileItem.SizeBytes : null,
            SizeDisplay = fileItem.IsFile ? FormatSize(fileItem.SizeBytes) : null,
            AccessType = share.AccessType,
            Permission = share.Permission,
            ExpiresAt = share.ExpiresAt,
            MaxDownloads = share.MaxDownloads,
            DownloadCount = share.DownloadCount,
            IsEnabled = share.IsEnabled,
            IsExpired = share.IsExpired,
            IsDownloadLimitReached = share.IsDownloadLimitReached,
            CreatedByUserId = share.CreatedByUserId,
            CreatedByUserName = creatorName,
            CreatedAt = share.CreatedAt,
            UpdatedAt = share.UpdatedAt
        };
    }


    #endregion
}

/// <summary>
/// 文件分享相关错误
/// </summary>
public static class FileShareErrors
{
    public static Error NotFound(Guid shareId) =>
        Error.NotFound("FileShare", shareId);

    public static Error ShareCodeNotFound(string shareCode) =>
        new("FileShare.NotFound", $"分享链接 '{shareCode}' 不存在或已失效");

    public static Error FileNotFound(Guid fileId) =>
        new("FileShare.FileNotFound", $"分享的文件不存在");

    public static Error AccessDenied() =>
        Error.Forbidden("无权访问此分享");

    public static Error ShareNotAccessible() =>
        new("FileShare.NotAccessible", "分享已过期或已达到下载限制");

    public static Error PasswordRequired() =>
        new("FileShare.PasswordRequired", "此分享需要密码验证");

    public static Error PasswordNotRequired() =>
        new("FileShare.PasswordNotRequired", "此分享不需要密码");

    public static Error InvalidPassword() =>
        new("FileShare.InvalidPassword", "密码错误");

    public static Error InvalidAccessToken() =>
        new("FileShare.InvalidToken", "访问令牌无效或已过期");

    public static Error InvalidAccessType() =>
        new("FileShare.InvalidAccessType", "无效的访问类型");

    public static Error CannotDownloadFolder() =>
        new("FileShare.CannotDownloadFolder", "不能直接下载文件夹，请选择具体文件");

    public static Error NotAFolder() =>
        new("FileShare.NotAFolder", "此分享不是文件夹");

    public static Error FolderNotFound() =>
        new("FileShare.FolderNotFound", "文件夹不存在");

    public static Error PhysicalFileNotFound() =>
        new("FileShare.PhysicalFileNotFound", "文件已被删除或移动");

    public static Error LoginRequired() =>
        new("FileShare.LoginRequired", "此分享仅限登录用户访问，请先登录");
}
