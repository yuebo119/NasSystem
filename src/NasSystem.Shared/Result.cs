// =============================================================================
// NasSystem.Shared - Result.cs
// Result 模式 - 统一错误处理
// =============================================================================

namespace NasSystem.Shared;

/// <summary>
/// 操作结果 - 无返回值
/// </summary>
public readonly struct Result
{
    private readonly Error? _error;

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// 是否失败
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// 错误信息
    /// </summary>
    public Error Error => _error ?? Error.None;

    private Result(bool isSuccess, Error? error)
    {
        IsSuccess = isSuccess;
        _error = error;
    }

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static Result Success() => new(true, null);

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>
    /// 创建失败结果 (便捷方法)
    /// </summary>
    public static Result Failure(string code, string message) => 
        new(false, new Error(code, message));

    /// <summary>
    /// 隐式转换 - 从 Error 创建失败结果
    /// </summary>
    public static implicit operator Result(Error error) => Failure(error);

    /// <summary>
    /// 匹配成功或失败
    /// </summary>
    public TResult Match<TResult>(
        Func<TResult> onSuccess,
        Func<Error, TResult> onFailure) =>
        IsSuccess ? onSuccess() : onFailure(Error);

    /// <summary>
    /// 匹配成功或失败 (异步)
    /// </summary>
    public async Task<TResult> MatchAsync<TResult>(
        Func<Task<TResult>> onSuccess,
        Func<Error, Task<TResult>> onFailure) =>
        IsSuccess ? await onSuccess() : await onFailure(Error);
}

/// <summary>
/// 操作结果 - 带返回值
/// </summary>
/// <typeparam name="TValue">返回值类型</typeparam>
public readonly struct Result<TValue>
{
    private readonly TValue? _value;
    private readonly Error? _error;

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// 是否失败
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// 返回值 (失败时抛出异常)
    /// </summary>
    public TValue Value => IsSuccess 
        ? _value! 
        : throw new InvalidOperationException($"无法访问失败结果的值。错误: {Error}");

    /// <summary>
    /// 错误信息
    /// </summary>
    public Error Error => _error ?? Error.None;

    private Result(bool isSuccess, TValue? value, Error? error)
    {
        IsSuccess = isSuccess;
        _value = value;
        _error = error;
    }

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static Result<TValue> Success(TValue value) => new(true, value, null);

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static Result<TValue> Failure(Error error) => new(false, default, error);

    /// <summary>
    /// 创建失败结果 (便捷方法)
    /// </summary>
    public static Result<TValue> Failure(string code, string message) => 
        new(false, default, new Error(code, message));

    /// <summary>
    /// 隐式转换 - 从值创建成功结果
    /// </summary>
    public static implicit operator Result<TValue>(TValue value) => Success(value);

    /// <summary>
    /// 隐式转换 - 从 Error 创建失败结果
    /// </summary>
    public static implicit operator Result<TValue>(Error error) => Failure(error);

    /// <summary>
    /// 获取值或默认值
    /// </summary>
    public TValue? GetValueOrDefault(TValue? defaultValue = default) =>
        IsSuccess ? _value : defaultValue;

    /// <summary>
    /// 匹配成功或失败
    /// </summary>
    public TResult Match<TResult>(
        Func<TValue, TResult> onSuccess,
        Func<Error, TResult> onFailure) =>
        IsSuccess ? onSuccess(_value!) : onFailure(Error);

    /// <summary>
    /// 匹配成功或失败 (异步)
    /// </summary>
    public async Task<TResult> MatchAsync<TResult>(
        Func<TValue, Task<TResult>> onSuccess,
        Func<Error, Task<TResult>> onFailure) =>
        IsSuccess ? await onSuccess(_value!) : await onFailure(Error);

    /// <summary>
    /// 映射值
    /// </summary>
    public Result<TNew> Map<TNew>(Func<TValue, TNew> mapper) =>
        IsSuccess ? Result<TNew>.Success(mapper(_value!)) : Result<TNew>.Failure(Error);

    /// <summary>
    /// 绑定操作
    /// </summary>
    public Result<TNew> Bind<TNew>(Func<TValue, Result<TNew>> binder) =>
        IsSuccess ? binder(_value!) : Result<TNew>.Failure(Error);

    /// <summary>
    /// 转换为无值结果
    /// </summary>
    public Result ToResult() => IsSuccess ? Result.Success() : Result.Failure(Error);
}

/// <summary>
/// 错误信息
/// </summary>
/// <param name="Code">错误代码</param>
/// <param name="Message">错误消息</param>
public readonly record struct Error(string Code, string Message)
{
    /// <summary>
    /// 无错误
    /// </summary>
    public static readonly Error None = new(string.Empty, string.Empty);

    /// <summary>
    /// 空值错误
    /// </summary>
    public static readonly Error NullValue = new("Error.NullValue", "提供的值为空");

    /// <summary>
    /// 验证错误
    /// </summary>
    public static Error Validation(string message) => new("Error.Validation", message);

    /// <summary>
    /// 未找到错误
    /// </summary>
    public static Error NotFound(string entity, object id) => 
        new("Error.NotFound", $"{entity} '{id}' 未找到");

    /// <summary>
    /// 冲突错误
    /// </summary>
    public static Error Conflict(string message) => new("Error.Conflict", message);

    /// <summary>
    /// 未授权错误
    /// </summary>
    public static Error Unauthorized(string message = "未授权访问") => 
        new("Error.Unauthorized", message);

    /// <summary>
    /// 禁止访问错误
    /// </summary>
    public static Error Forbidden(string message = "禁止访问") => 
        new("Error.Forbidden", message);

    /// <summary>
    /// 内部错误
    /// </summary>
    public static Error Internal(string message) => new("Error.Internal", message);

    public override string ToString() => $"[{Code}] {Message}";
}
