// =============================================================================
// NasSystem.Api - Extensions/UploadLimit.cs
// 上传体积上限的单一事实源
// =============================================================================

namespace NasSystem.Api.Extensions;

/// <summary>
/// 上传体积上限。Kestrel 的请求体限制与系统设置端点报给前端的数值共用此常量，
/// 避免两边各写一个数字后漂移（历史上一个是 500MB、一个是 1024MB）。
/// </summary>
public sealed class UploadLimit
{
    /// <summary>请求体上限，单位字节。对应 500 MB。</summary>
    public const long MaxRequestBodySize = 500L * 1024 * 1024;

    /// <summary>供 DI 注入的实例，端点在方法参数里直接取。</summary>
    public static UploadLimit Instance { get; } = new();

    /// <summary>上限对应的 MB 数，用于向前端展示。</summary>
    public int MaxUploadSizeMB => (int)(MaxRequestBodySize / 1024 / 1024);
}
