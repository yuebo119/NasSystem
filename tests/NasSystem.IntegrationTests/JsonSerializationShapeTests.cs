// =============================================================================
// NasSystem.IntegrationTests - JsonSerializationShapeTests.cs
// 响应体 JSON 形状锁定：camelCase 命名与 null 省略
// =============================================================================

namespace NasSystem.IntegrationTests;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// 锁住源生生成上下文接入后的序列化形状。
/// </summary>
/// <remarks>
/// 为什么值得单独立一个文件：System.Text.Json 的源生生成快速路径会把命名策略
/// 在编译期固定。一旦 NasJsonContext 的 [JsonSourceGenerationOptions] 与
/// ServiceCollectionExtensions 里的运行时选项漂移，症状是部分端点回 camelCase、
/// 部分回 PascalCase，功能测试照样通过（反序列化侧大小写不敏感），只有前端炸。
/// 这条测试直接读原始响应体字符串，对两种形态都零容忍。
/// </remarks>
public class JsonSerializationShapeTests : ApiTestBase
{
    public JsonSerializationShapeTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    [Fact]
    public async Task BrowseResponse_UsesCamelCasePropertyNames()
    {
        // Arrange
        var token = await GetAuthTokenAsync("shapeuser");
        SetAuthToken(token);
        await SeedDefaultStoragePoolAsync();
        await SeedTestFolderAsync("/documents", "shapeuser");

        // Act
        var response = await Client.GetAsync("/api/v1/files/browse/");
        var raw = await response.Content.ReadAsStringAsync();

        // Assert - camelCase 必须在场
        raw.Should().Contain("\"success\"");
        raw.Should().Contain("\"data\"");
        raw.Should().Contain("\"items\"");
        raw.Should().Contain("\"folderCount\"");
        raw.Should().Contain("\"fileCount\"");
        raw.Should().Contain("\"totalSizeBytes\"");
        raw.Should().Contain("\"breadcrumbs\"");

        // Assert - PascalCase 必须不在场（源生上下文选项漂移的直接症状）
        raw.Should().NotContain("\"Success\"");
        raw.Should().NotContain("\"Items\"");
        raw.Should().NotContain("\"FolderCount\"");
        raw.Should().NotContain("\"TotalSizeBytes\"");

        // Assert - null 属性按 DefaultIgnoreCondition 省略（mimeType 为 null 时不出现键）
        raw.Should().NotContain("\"mimeType\":null");
    }

    [Fact]
    public async Task StatisticsResponse_UsesCamelCasePropertyNames()
    {
        // Arrange
        var token = await GetAuthTokenAsync("shapestatsuser");
        SetAuthToken(token);
        await SeedDefaultStoragePoolAsync();

        // Act
        var response = await Client.GetAsync("/api/v1/files/statistics");
        var raw = await response.Content.ReadAsStringAsync();

        // Assert
        raw.Should().Contain("\"totalFiles\"");
        raw.Should().Contain("\"totalFolders\"");
        raw.Should().Contain("\"recentFilesCount\"");
        raw.Should().NotContain("\"TotalFiles\"");
        raw.Should().NotContain("\"RecentFilesCount\"");
    }

    [Fact]
    public void HotDtoTypeInfo_OriginatesFromSourceGeneratedContext()
    {
        // 验证验证者：证明源生生成真的接上了，而不是 ConfigureHttpJsonOptions 里
        // 写了 Combine 但运行时仍走反射。若这条挂，上面的形状测试可能照样通过
        // （两种解析器的输出一致），优化实际是 no-op。
        var jsonOptions = Factory.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
            .Value.SerializerOptions;

        jsonOptions.GetTypeInfo(typeof(NasSystem.Shared.ApiResponse<NasSystem.Application.Files.Dtos.DirectoryContentDto>))
            .OriginatingResolver.Should().BeSameAs(NasSystem.Api.Serialization.NasJsonContext.Default);

        jsonOptions.GetTypeInfo(typeof(NasSystem.Shared.ApiResponse<NasSystem.Application.Files.Dtos.FileStatisticsDto>))
            .OriginatingResolver.Should().BeSameAs(NasSystem.Api.Serialization.NasJsonContext.Default);

        // 未声明的类型（object 声明成员）必须仍能取到元数据，否则匿名对象端点会 500
        jsonOptions.GetTypeInfo(typeof(NasSystem.Shared.ApiResponse<object>))
            .Should().NotBeNull();
    }
}
