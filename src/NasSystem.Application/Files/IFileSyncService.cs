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
    /// <param name="force">物理树为空但库内仍有活跃记录时，是否确认继续删除。
    /// 默认 false：空池更可能是挂载点不可用，不能自动清空元数据（F-101）</param>
    Task<Result<ScanResultDto>> ScanAndSyncAsync(
        Guid userId,
        bool force = false,
        CancellationToken cancellationToken = default);
}
