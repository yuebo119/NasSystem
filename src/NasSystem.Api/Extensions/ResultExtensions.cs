// =============================================================================
// NasSystem.Api - ResultExtensions.cs
// Result 到 IResult 的转换扩展
// =============================================================================

namespace NasSystem.Api.Extensions;

using NasSystem.Shared;

/// <summary>
/// Result 模式到 HTTP 响应的转换扩展
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// 将 Result 转换为 IResult
    /// </summary>
    public static IResult ToHttpResult(this Result result)
    {
        return result.IsSuccess
            ? Results.Ok(ApiResponse.Ok())
            : ToErrorResult(result.Error);
    }

    /// <summary>
    /// 将 Result<T> 转换为 IResult
    /// </summary>
    public static IResult ToHttpResult<T>(this Result<T> result)
    {
        return result.IsSuccess
            ? Results.Ok(ApiResponse<T>.Ok(result.Value))
            : ToErrorResult(result.Error);
    }

    /// <summary>
    /// 将 Result<T> 转换为 Created IResult
    /// </summary>
    public static IResult ToCreatedResult<T>(this Result<T> result, string location)
    {
        return result.IsSuccess
            ? Results.Created(location, ApiResponse<T>.Ok(result.Value))
            : ToErrorResult(result.Error);
    }

    /// <summary>
    /// 将 Result 转换为 NoContent IResult
    /// </summary>
    public static IResult ToNoContentResult(this Result result)
    {
        return result.IsSuccess
            ? Results.NoContent()
            : ToErrorResult(result.Error);
    }

    /// <summary>
    /// 根据错误代码返回适当的 HTTP 状态码
    /// </summary>
    private static IResult ToErrorResult(Error error)
    {
        var response = ApiResponse.Fail(error);

        return error.Code switch
        {
            "Error.NotFound" => Results.NotFound(response),
            "Error.Validation" => Results.BadRequest(response),
            "Error.Conflict" => Results.Conflict(response),
            "Error.Unauthorized" => Results.Unauthorized(),
            "Error.Forbidden" => Results.Forbid(),
            var code when code.EndsWith(".NotFound") => Results.NotFound(response),
            var code when code.Contains("Validation") => Results.BadRequest(response),
            var code when code.Contains("Conflict") || code.Contains("Exists") => Results.Conflict(response),
            var code when code.Contains("Unauthorized") || code.Contains("Credentials") => Results.Unauthorized(),
            var code when code.Contains("Forbidden") || code.Contains("AccessDenied") => Results.Forbid(),
            _ => Results.BadRequest(response)
        };
    }
}
