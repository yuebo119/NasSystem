// =============================================================================
// NasSystem.IntegrationTests - ScanRobustnessRegressionTests.cs
// 审计 B4 批（扫描健壮性）锁定测试：排除名作用域/深度上限
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using NasSystem.Domain.Enums;
using Xunit;

/// <summary>
/// 锁 2026-10-22 审计 B4 批修复：
/// F-014 扫描排除用名字/子串匹配：用户建 /docs/.versions/ 或叫
///       report.uploading-2024.pdf 的合法文件，记录会被判"已消失"删掉
/// F-013 目录扫描无深度上限、跟随 junction，深树爆栈/环状链接无限递归
/// </summary>
public class ScanRobustnessRegressionTests : ApiTestBase
{
    public ScanRobustnessRegressionTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    [Fact]
    public async Task Scan_UserFileNamedLikeTempArtifact_IsNotExcluded()
    {
        var token = await GetAuthTokenAsync("scanrob" + Guid.NewGuid().ToString("N")[..6], UserRole.Admin);
        SetAuthToken(token);
        await SeedDefaultStoragePoolAsync();

        // 用户合法文件：名字含 .uploading- 但不是 32 位 hex 后缀的临时件。
        // 修复前按子串匹配被跳过 → 不进 physicalPaths → 库记录被判"已消失"删除
        var legitName = "report.uploading-2024.pdf";
        var (scope, _, pools, fileItems, _, _) = CreateDataScope();
        using (scope)
        {
            var pool = await pools.GetDefaultAsync();
            var existing = await fileItems.GetByPathAsync($"/{legitName}");
            if (existing is null)
            {
                // 物理文件必须真实存在：扫描的删除集是"库里有、物理上没了"，
                // 不建物理文件的话记录被删与排除规则无关，测不出靶心
                var physicalPath = System.IO.Path.Combine(pool!.MountPath, legitName);
                await System.IO.File.WriteAllTextAsync(physicalPath, "legit pdf");

                var file = NasSystem.Domain.Entities.FileItem.CreateFile(
                    name: legitName,
                    path: $"/{legitName}",
                    physicalPath: physicalPath,
                    sizeBytes: 10,
                    mimeType: "application/pdf",
                    storagePoolId: pool.Id,
                    ownerId: await GetAnyUserIdAsync(),
                    hash: "legit");
                await fileItems.AddAsync(file);
                await SaveAsync();
            }
        }

        var scan = await Client.PostAsJsonAsync("/api/v1/files/scan", new { });
        scan.StatusCode.Should().Be(HttpStatusCode.OK);

        var (scope2, _, _, fileItems2, _, _) = CreateDataScope();
        using (scope2)
        {
            var after = await fileItems2.GetByPathAsync($"/{legitName}");
            after.Should().NotBeNull(
                "用户合法文件不得因名字像临时工件而被扫描判消失（排除规则必须锚定精确形态）");
        }
    }

    [Fact]
    public async Task Scan_UserFolderNamedVersionsInSubdir_IsNotExcluded()
    {
        var token = await GetAuthTokenAsync("scanrobv" + Guid.NewGuid().ToString("N")[..6], UserRole.Admin);
        SetAuthToken(token);
        await SeedDefaultStoragePoolAsync();
        var userId = await GetAnyUserIdAsync();

        // 用户在子目录建合法 .versions 文件夹（修复前按目录名一刀切跳过）
        var folderPath = "/docs/.versions";
        var filePath = "/docs/.versions/keep.txt";
        var (scope, _, pools, fileItems, _, _) = CreateDataScope();
        using (scope)
        {
            var pool = await pools.GetDefaultAsync();
            if (await fileItems.GetByPathAsync(filePath) is null)
            {
                // 物理目录与文件都要真实存在（删除集语义同上）
                var physicalDir = System.IO.Path.Combine(pool!.MountPath, "docs", ".versions");
                System.IO.Directory.CreateDirectory(physicalDir);
                await System.IO.File.WriteAllTextAsync(
                    System.IO.Path.Combine(physicalDir, "keep.txt"), "keep");

                var folder = NasSystem.Domain.Entities.FileItem.CreateFolder(
                    name: ".versions",
                    path: folderPath,
                    physicalPath: physicalDir,
                    ownerId: userId,
                    storagePoolId: pool.Id,
                    parentId: null);
                await fileItems.AddAsync(folder);

                var file = NasSystem.Domain.Entities.FileItem.CreateFile(
                    name: "keep.txt",
                    path: filePath,
                    physicalPath: System.IO.Path.Combine(physicalDir, "keep.txt"),
                    sizeBytes: 5,
                    mimeType: "text/plain",
                    storagePoolId: pool.Id,
                    ownerId: userId,
                    parentId: folder.Id,
                    hash: "keep");
                await fileItems.AddAsync(file);
                await SaveAsync();
            }
        }

        var scan = await Client.PostAsJsonAsync("/api/v1/files/scan", new { });
        scan.StatusCode.Should().Be(HttpStatusCode.OK);

        var (scope2, _, _, fileItems2, _, _) = CreateDataScope();
        using (scope2)
        {
            var after = await fileItems2.GetByPathAsync(filePath);
            after.Should().NotBeNull(
                "只有池挂载点顶层的 .versions 是版本库，用户子目录里的同名文件夹是合法内容");
        }
    }

    [Fact]
    public async Task Scan_DirectoryDeeperThanLimit_DoesNotCrashAndReportsSkip()
    {
        var token = await GetAuthTokenAsync("scanrobd" + Guid.NewGuid().ToString("N")[..6], UserRole.Admin);
        SetAuthToken(token);
        await SeedDefaultStoragePoolAsync();
        var pool = await GetDefaultPoolAsync();

        // 造 40 层深目录（上限 32）：修复前无限递归爆栈；修复后跳过并计数
        var deep = pool!.MountPath;
        for (var i = 0; i < 40; i++)
            deep = System.IO.Path.Combine(deep, $"L{i}");
        System.IO.Directory.CreateDirectory(deep);
        await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(deep, "bottom.txt"), "deep");

        try
        {
            var scan = await Client.PostAsJsonAsync("/api/v1/files/scan", new { });
            scan.StatusCode.Should().Be(HttpStatusCode.OK, "超深目录不得让扫描崩溃");
            var body = await scan.Content.ReadAsStringAsync();
            body.Should().Contain("超过深度上限", "跳过的目录数必须上报，不能静默");
        }
        finally
        {
            try { System.IO.Directory.Delete(pool.MountPath, recursive: true); } catch { }
        }
    }

    // ─── 辅助 ───

    private async Task<NasSystem.Domain.Entities.StoragePool?> GetDefaultPoolAsync()
    {
        var (scope, _, pools, _, _, _) = CreateDataScope();
        using (scope)
        {
            return await pools.GetDefaultAsync();
        }
    }

    private async Task<Guid> GetAnyUserIdAsync()
    {
        var (scope, users, _, _, _, _) = CreateDataScope();
        using (scope)
        {
            var all = await users.GetAllAsync();
            return all.First().Id;
        }
    }

    private async Task SaveAsync()
    {
        var (scope, _, _, _, _, unitOfWork) = CreateDataScope();
        using (scope)
        {
            await unitOfWork.SaveChangesAsync();
        }
    }
}
