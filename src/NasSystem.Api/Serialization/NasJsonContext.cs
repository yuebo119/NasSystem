// =============================================================================
// NasSystem.Api - Serialization/NasJsonContext.cs
// 热路径 DTO 的 System.Text.Json 源生生成上下文
// =============================================================================

namespace NasSystem.Api.Serialization;

using System.Text.Json.Serialization;
using NasSystem.Application.Files.Dtos;
using NasSystem.Application.FileShares.Dtos;
using NasSystem.Application.FileVersions;
using NasSystem.Application.Storage.Dtos;
using NasSystem.Application.Users.Dtos;
using NasSystem.Shared;

/// <summary>
/// 为高频响应体提供编译期生成的序列化元数据，替代每次请求的反射契约查找。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么还要保留反射回退</b>：SystemEndpoints 有匿名对象、
/// ApiResponse&lt;object&gt; 的 Data 是 object 声明成员（运行期类型未知），
/// 这些进不了源生上下文。因而 TypeInfoResolver 用 Combine 把上下文排在前面、
/// DefaultJsonTypeInfoResolver 兜底，未声明的类型仍能序列化。
/// </para>
/// <para>
/// <b>为什么选项要逐项抄一遍</b>：源生生成的快速路径序列化器会把命名策略与
///  null 忽略条件在生成期固定下来。若这里的选项与 ConfigureHttpJsonOptions
///  里的运行时选项不一致，序列化结果会随"走上下文还是走反射"而不同，
///  出现 camelCase 与 PascalCase 混杂的响应。两边必须同源维护。
/// </para>
/// <para>
/// <b>故意不声明 ApiResponse&lt;object&gt;</b>：object 声明成员的运行期类型
/// 无法在编译期确定，官方文档要求为这类成员显式列出全部可能类型；
/// 交给反射回退处理，它的调用频率远低于本上下文覆盖的类型。
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ApiResponse<DirectoryContentDto>))]
[JsonSerializable(typeof(ApiResponse<FileItemDto>))]
[JsonSerializable(typeof(ApiResponse<IReadOnlyList<FileItemDto>>))]
[JsonSerializable(typeof(ApiResponse<PagedResult<FileItemDto>>))]
[JsonSerializable(typeof(ApiResponse<FileStatisticsDto>))]
[JsonSerializable(typeof(ApiResponse<ScanResultDto>))]
[JsonSerializable(typeof(ApiResponse<DeleteResultDto>))]
[JsonSerializable(typeof(ApiResponse<StorageOverviewDto>))]
[JsonSerializable(typeof(ApiResponse<IReadOnlyList<StoragePoolDto>>))]
[JsonSerializable(typeof(ApiResponse<StoragePoolDto>))]
[JsonSerializable(typeof(ApiResponse<CheckStorageSpaceResponse>))]
[JsonSerializable(typeof(ApiResponse<UserDto>))]
[JsonSerializable(typeof(ApiResponse<PagedResult<UserDto>>))]
[JsonSerializable(typeof(ApiResponse<LoginResultDto>))]
[JsonSerializable(typeof(ApiResponse<FileShareDto>))]
[JsonSerializable(typeof(ApiResponse<IReadOnlyList<FileShareDto>>))]
[JsonSerializable(typeof(ApiResponse<PublicShareInfoDto>))]
[JsonSerializable(typeof(ApiResponse<ShareFolderContentDto>))]
[JsonSerializable(typeof(ApiResponse<IReadOnlyList<FileVersionDto>>))]
[JsonSerializable(typeof(ApiResponse<FileVersionDto>))]
[JsonSerializable(typeof(ApiResponse<VersionComparisonDto>))]
[JsonSerializable(typeof(ApiResponse))]
internal sealed partial class NasJsonContext : JsonSerializerContext;
