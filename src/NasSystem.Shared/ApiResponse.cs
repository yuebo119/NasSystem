// =============================================================================
// NasSystem.Shared - ApiResponse.cs
// 统一 API 响应格式
// =============================================================================

namespace NasSystem.Shared;

using System.Text.Json.Serialization;

/// <summary>
/// 统一 API 响应格式
/// </summary>
/// <typeparam name="T">数据类型</typeparam>
public sealed record ApiResponse<T>
{
    /// <summary>
    /// 是否成功
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>
    /// 响应数据
    /// </summary>
    [JsonPropertyName("data")]
    public T? Data { get; init; }

    /// <summary>
    /// 错误信息
    /// </summary>
    [JsonPropertyName("error")]
    public ApiError? Error { get; init; }

    /// <summary>
    /// 时间戳
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 创建成功响应
    /// </summary>
    public static ApiResponse<T> Ok(T data) => new()
    {
        Success = true,
        Data = data
    };

    /// <summary>
    /// 创建失败响应
    /// </summary>
    public static ApiResponse<T> Fail(string code, string message) => new()
    {
        Success = false,
        Error = new ApiError(code, message)
    };

    /// <summary>
    /// 创建失败响应
    /// </summary>
    public static ApiResponse<T> Fail(Error error) => new()
    {
        Success = false,
        Error = new ApiError(error.Code, error.Message)
    };
}

/// <summary>
/// 无数据的 API 响应
/// </summary>
public sealed record ApiResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("error")]
    public ApiError? Error { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    public static ApiResponse Ok() => new() { Success = true };

    public static ApiResponse Fail(string code, string message) => new()
    {
        Success = false,
        Error = new ApiError(code, message)
    };

    public static ApiResponse Fail(Error error) => new()
    {
        Success = false,
        Error = new ApiError(error.Code, error.Message)
    };
}

/// <summary>
/// API 错误信息
/// </summary>
public sealed record ApiError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("details")] Dictionary<string, string[]>? Details = null);
