// =============================================================================
// NasSystem.Domain - BackupDevice.cs
// 备份设备实体（ROADMAP Phase 1 移动备份）
// =============================================================================

namespace NasSystem.Domain.Entities;

using NasSystem.Domain.Common;

/// <summary>
/// 备份设备：手机/电脑等端点的注册记录。设备本身不存文件——
/// 备份文件就是存储池里的普通文件（位于 /Backups/{设备名}/ 下），
/// 配额检查、软删除、SignalR 推送全部复用现有管道，
/// 本实体只回答"哪些设备在往我这备份、上次同步是什么时候"
/// </summary>
public sealed class BackupDevice : Entity
{
    /// <summary>所属用户</summary>
    public Guid UserId { get; private set; }

    /// <summary>设备名称（同时决定备份目录名 /Backups/{Name}）</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>设备型号/描述（客户端自报，仅展示）</summary>
    public string? DeviceModel { get; private set; }

    /// <summary>最近一次成功同步时间</summary>
    public DateTime? LastSyncAt { get; private set; }

    /// <summary>最近一次同步上传的文件数</summary>
    public int LastSyncFileCount { get; private set; }

    /// <summary>是否已吊销。吊销保留记录（同步历史可查），但不再接受新计划</summary>
    public bool IsRevoked { get; private set; }

    /// <summary>
    /// 备份目录的虚拟路径。设备名与目录名同源：改设备名等于换备份目录，
    /// 不做目录迁移（历史文件留在旧目录，由用户自行处置）
    /// </summary>
    public string BackupPath => $"/Backups/{Name}";

    public static BackupDevice Create(
        Guid userId,
        string name,
        string? deviceModel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (userId == Guid.Empty)
            throw new ArgumentException("用户 ID 不能为空", nameof(userId));

        return new BackupDevice
        {
            UserId = userId,
            Name = name,
            DeviceModel = deviceModel,
            IsRevoked = false,
            LastSyncFileCount = 0
        };
    }

    /// <summary>记录一次成功同步</summary>
    public void RecordSync(int uploadedFileCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(uploadedFileCount);
        LastSyncAt = DateTime.UtcNow;
        LastSyncFileCount = uploadedFileCount;
        MarkAsModified();
    }

    /// <summary>更新设备型号（客户端版本升级后自报信息会变）</summary>
    public void UpdateModel(string? deviceModel)
    {
        DeviceModel = deviceModel;
        MarkAsModified();
    }

    /// <summary>吊销设备</summary>
    public void Revoke()
    {
        IsRevoked = true;
        MarkAsModified();
    }

    /// <summary>从持久化状态恢复实体（供基础设施层影子模型映射使用）</summary>
    internal static BackupDevice Restore(
        Guid id,
        Guid userId,
        string name,
        string? deviceModel,
        DateTime? lastSyncAt,
        int lastSyncFileCount,
        bool isRevoked,
        DateTime createdAt,
        DateTime? updatedAt)
    {
        return new BackupDevice
        {
            Id = id,
            UserId = userId,
            Name = name,
            DeviceModel = deviceModel,
            LastSyncAt = lastSyncAt,
            LastSyncFileCount = lastSyncFileCount,
            IsRevoked = isRevoked,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }
}
