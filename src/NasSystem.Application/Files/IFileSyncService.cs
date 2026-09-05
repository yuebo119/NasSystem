// =============================================================================
// NasSystem.Application - Files/IFileSyncService.cs
// 扫描同步用例域窄接口（ITM-T3：ISP 接口隔离）
// =============================================================================

namespace NasSystem.Application.Files;

using NasSystem.Application.Files.Dtos;
using NasSystem.Shared;

/// <summary>
/// 物理文件系统扫描同步服务
/// </summary>
public interface IFileSyncService
{
    /// <summary>扫描存储目录并同步到数据库</summary>
    Task<Result<ScanResultDto>> ScanAndSyncAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
