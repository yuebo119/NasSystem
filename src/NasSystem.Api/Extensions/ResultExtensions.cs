// =============================================================================
// NasSystem.Api - ResultExtensions.cs
// Result 到 IResult 的转换扩展
// =============================================================================

namespace NasSystem.Api.Extensions;

using System.Net;
using NasSystem.Shared;

/// <summary>
/// Result 模式到 HTTP 响应的转换扩展
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// 客户端错误码到 HTTP 状态码的显式表。ITM-N21 的意图完整保留：
    /// 只有表内的已知客户端错误拿 4xx/507，其余（Scan.Failed 等运行时
    /// 故障）一律落 500。替代此前的子串匹配：Contains("Exists") 这类
    /// 启发式漏掉大半客户端错误码（FileShare.PasswordRequired 之类落 500，
    /// 用户看到"服务器错误"而非真实原因），且子串命中面不可审查。
    /// 新错误码默认 500 是特性：逼着加码时显式决定它的状态语义
    /// </summary>
    private static readonly Dictionary<string, HttpStatusCode> ClientErrorStatuses = new(StringComparer.Ordinal)
    {
        // ─── 404：引用了不存在的东西 ───
        ["File.NotFound"] = HttpStatusCode.NotFound,
        ["FileShare.NotFound"] = HttpStatusCode.NotFound,
        ["FileShare.FileNotFound"] = HttpStatusCode.NotFound,
        ["FileShare.FolderNotFound"] = HttpStatusCode.NotFound,
        ["FileShare.PhysicalFileNotFound"] = HttpStatusCode.NotFound,
        ["FileVersion.NotFound"] = HttpStatusCode.NotFound,
        ["FileVersion.StorageNotFound"] = HttpStatusCode.NotFound,
        ["Backup.DeviceNotFound"] = HttpStatusCode.NotFound,
        // 模型未就位是"引用的东西不存在"（四件套未下载）：
        // SemanticEndpoints 注释承诺的 404 语义，此前漏登记落 500
        ["Semantic.ModelNotReady"] = HttpStatusCode.NotFound,

        // ─── 409：与现有状态冲突 ───
        ["File.AlreadyExists"] = HttpStatusCode.Conflict,
        ["FileVersion.CurrentVersionConflict"] = HttpStatusCode.Conflict,
        ["StoragePool.NameExists"] = HttpStatusCode.Conflict,
        ["StoragePool.MountPathExists"] = HttpStatusCode.Conflict,

        // ─── 507：容量/配额（RFC 4918 语义；WebDAV 层本就特判这两个码）───
        ["Storage.InsufficientSpace"] = HttpStatusCode.InsufficientStorage,
        ["User.QuotaExceeded"] = HttpStatusCode.InsufficientStorage,

        // ─── 400：请求本身不成立（参数、类型、前提状态）───
        ["Config.ValidationFailed"] = HttpStatusCode.BadRequest,
        ["Semantic.Disabled"] = HttpStatusCode.BadRequest,
        ["File.InvalidName"] = HttpStatusCode.BadRequest,
        ["File.NotFile"] = HttpStatusCode.BadRequest,
        ["File.NotFolder"] = HttpStatusCode.BadRequest,
        ["Backup.InvalidName"] = HttpStatusCode.BadRequest,
        ["FileShare.CannotDownloadFolder"] = HttpStatusCode.BadRequest,
        ["FileShare.InvalidAccessType"] = HttpStatusCode.BadRequest,
        ["FileShare.NotAFolder"] = HttpStatusCode.BadRequest,
        ["FileShare.NotAccessible"] = HttpStatusCode.BadRequest,
        ["FileShare.PasswordNotRequired"] = HttpStatusCode.BadRequest,
        ["FileShare.PasswordRequired"] = HttpStatusCode.BadRequest,
        ["FileShare.InvalidPassword"] = HttpStatusCode.BadRequest,
        ["FileShare.InvalidToken"] = HttpStatusCode.BadRequest,
        ["FileShare.LoginRequired"] = HttpStatusCode.BadRequest,
        ["User.PasswordMismatch"] = HttpStatusCode.BadRequest,
        ["User.CannotDeleteSuperAdmin"] = HttpStatusCode.BadRequest,
        ["StoragePool.CannotDeleteDefault"] = HttpStatusCode.BadRequest,
        ["Thumbnail.NotSupported"] = HttpStatusCode.BadRequest,

        // ─── 403：认证了但没权限 ───
        ["File.ProtectedForbidden"] = HttpStatusCode.Forbidden,
        ["User.Disabled"] = HttpStatusCode.Forbidden,

        // ─── 401：认证失败（登录链路口径，拦截器据此清 token 跳登录）───
        ["User.InvalidCredentials"] = HttpStatusCode.Unauthorized
    };

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
    /// 将 Result&lt;T&gt; 转换为 IResult
    /// </summary>
    public static IResult ToHttpResult<T>(this Result<T> result)
    {
        return result.IsSuccess
            ? Results.Ok(ApiResponse<T>.Ok(result.Value))
            : ToErrorResult(result.Error);
    }

    /// <summary>
    /// 将 Result&lt;T&gt; 转换为 Created IResult
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

        // Error.* 家族（Shared 工厂产出，含消息模板）与模块码表并列判定；
        // EndsWith("NotFound") 保留给未来的新实体：模块.实体NotFound 是
        // 既定的命名约定，漏进 500 会把"引用了不存在的东西"演成服务端故障
        var statusCode = error.Code switch
        {
            "Error.NotFound" => HttpStatusCode.NotFound,
            "Error.Validation" => HttpStatusCode.BadRequest,
            "Error.Conflict" => HttpStatusCode.Conflict,
            "Error.Unauthorized" => HttpStatusCode.Unauthorized,
            "Error.Forbidden" => HttpStatusCode.Forbidden,
            var code when ClientErrorStatuses.TryGetValue(code, out var mapped) => mapped,
            var code when code.EndsWith("NotFound") => HttpStatusCode.NotFound,
            _ => HttpStatusCode.InternalServerError
        };

        return statusCode switch
        {
            HttpStatusCode.NotFound => Results.NotFound(response),
            HttpStatusCode.BadRequest => Results.BadRequest(response),
            HttpStatusCode.Conflict => Results.Conflict(response),
            HttpStatusCode.Unauthorized => Results.Unauthorized(),
            HttpStatusCode.Forbidden => Results.Forbid(),
            HttpStatusCode.InsufficientStorage => Results.Json(
                response, statusCode: (int)HttpStatusCode.InsufficientStorage),
            _ => Results.InternalServerError(response)
        };
    }
}
