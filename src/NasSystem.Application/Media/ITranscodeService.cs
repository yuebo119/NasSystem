// =============================================================================
// NasSystem.Application - Media/ITranscodeService.cs
// 视频 HLS 按需转码服务（ROADMAP Phase 3）
// =============================================================================

namespace NasSystem.Application.Media;

using NasSystem.Shared;

/// <summary>
/// 视频转码：FFmpeg 子进程把库内视频转为 HLS（m3u8 + ts 分片），
/// 移动端播放 1080p 子流。按需触发、幂等缓存。
/// </summary>
/// <remarks>
/// 诚实降级：ffmpeg 不在位（开发机常见）时 <see cref="IsAvailable"/> 为
/// false，转码与视频缩略图抽帧都走"不可用"路径（501 / 1x1 占位），
/// 不假装能转。生产镜像由 Dockerfile 装 ffmpeg（约 100MB 级）。
/// </remarks>
public interface ITranscodeService
{
    /// <summary>ffmpeg 可执行是否可用</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 取得视频的 HLS 播放列表路径（不存在则转码）。缓存键取物理路径的
    /// SHA256：内容变化（覆盖上传会产新物理路径或同路径不同内容——
    /// 同路径覆盖时缓存不失效是已知口径，与缩略图缓存同款取舍）
    /// </summary>
    Task<Result<HlsPlaylist>> GetOrCreateHlsAsync(
        string physicalPath, CancellationToken cancellationToken = default);

    /// <summary>清空转码缓存（对齐缩略图缓存清理的管理入口）</summary>
    Task<long> ClearCacheAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 抽取视频代表帧（第 1 秒处单帧，原始尺寸字节）。供缩略图服务
    /// 生成视频封面：抽帧是转码域能力（ffmpeg 子进程），消费方经
    /// SkiaSharp 缩放。ffmpeg 不可用或抽帧失败返回 null（调用方回落
    /// 占位图）
    /// </summary>
    Task<byte[]?> ExtractVideoFrameAsync(
        string physicalPath, CancellationToken cancellationToken = default);
}

/// <summary>HLS 播放列表定位</summary>
public sealed record HlsPlaylist(
    string Directory,
    string PlaylistFileName,
    long TotalSizeBytes);
