// =============================================================================
// NasSystem.Shared - JsonSerializerContext.cs
// JSON Source Generator 配置 (AOT 兼容)
// =============================================================================

namespace NasSystem.Shared;

using System.Text.Json.Serialization;

/// <summary>
/// JSON 序列化上下文 (使用 Source Generator 提高性能)
/// </summary>
[JsonSerializable(typeof(ApiResponse))]
[JsonSerializable(typeof(ApiResponse<object>))]
[JsonSerializable(typeof(ApiError))]
[JsonSerializable(typeof(Error))]
[JsonSerializable(typeof(PagedResult<object>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, string[]>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
public partial class NasJsonSerializerContext : JsonSerializerContext
{
}
