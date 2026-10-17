// =============================================================================
// NasSystem.IntegrationTests - SubtreeOperationTests.cs
// 子树操作（重命名/移动/软删/恢复）的路径一致性与版本号语义
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using NasSystem.Domain.Entities;
using Xunit;

/// <summary>
/// 锁住子树操作的三类行为：
/// 一、重命名/移动文件夹时后代路径必须跟随重写（含物理路径语义）；
/// 二、软删文件夹再恢复，后代必须一并恢复；
/// 三、子树软删后受影响目录的 ETag 必须变（否则客户端缓存陈旧）。
/// </summary>
/// <remarks>
/// 2026-10-17 S3：子树操作从逐条实体 UPDATE 改单条 SQL 前的行为基线。
/// 其中重命名与恢复两条在当前实现下失败：
/// 重命名：RenameAsync 调用 RewriteDescendantPathsAsync 传的旧基路径是
/// GetParentPath(path)（父目录）而非文件夹自身旧路径，后代路径被拼出多一段
/// （/a/b/x.txt 随 /a/b→/a/c 变成 /a/c/b/x.txt）；MoveAsync 传的是完整旧路径。
/// 两条调用同一方法却不同参，本测试把正确语义固定下来。
/// 恢复：RestoreAsync 按 Id 取后代但 GetDescendantsAsync 带 IsDeleted = 0 过滤，
/// 而软删子树的后代全是 1，实际只恢复根、后代留在回收站。
/// 本类的默认池挂载点是每类一个的真实临时目录（重命名/移动要真的移物理目录），
/// 池 Id 与挂载点在类内恒定，FileService 的 30s 池投影缓存不会串味。
/// </remarks>
public class SubtreeOperationTests : ApiTestBase, IDisposable
{
    /// <summary>本类共享的真实存储根：每个工厂一个独立库，池在类内只建一次</summary>
    private static readonly string ClassStorageRoot =
        Path.Combine(Path.GetTempPath(), $"subtree-{Guid.NewGuid():N}");

    /// <summary>
    /// 每进程唯一的路径前缀。物理移动的目标路径由默认池挂载点派生
    /// （测试工厂的种子池挂在构建目录 storage 下，跨运行残留），
    /// 固定名字会撞上上一轮遗留的物理目录导致 Directory.Move 抛
    /// "already exists" 被 MoveAsync 静默跳过
    /// </summary>
    private static readonly string PathPrefix = "st" + Guid.NewGuid().ToString("N")[..6];

    static SubtreeOperationTests()
    {
        Directory.CreateDirectory(ClassStorageRoot);
    }

    public SubtreeOperationTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    public new void Dispose()
    {
        try
        {
            Directory.Delete(ClassStorageRoot, recursive: true);
        }
        catch
        {
            // 句柄未释放时留下，由系统临时目录清理
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task RenameFolder_DescendantPathsFollowRename()
    {
        // Arrange：/{prefix}-sub-old/child.txt（DB 记录 + 真实物理目录）
        var username = "subrename" + Guid.NewGuid().ToString("N")[..6];
        var token = await GetAuthTokenAsync(username);
        SetAuthToken(token);
        await EnsureClassPoolAsync();
        var oldFolder = $"/{PathPrefix}-sub-old";
        var newFolder = $"/{PathPrefix}-sub-new";
        await SeedFolderWithFileAsync(username, oldFolder, "child.txt");

        // Act：重命名
        var rename = await Client.PutAsJsonAsync("/api/v1/files/rename",
            new { path = oldFolder, newName = $"{PathPrefix}-sub-new" });
        rename.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert①：新路径下必须直接看到 child.txt（不是 sub-new/sub-old/child.txt）
        var browseNew = await Client.GetAsync($"/api/v1/files/browse{newFolder}");
        browseNew.StatusCode.Should().Be(HttpStatusCode.OK);
        var newRaw = await browseNew.Content.ReadAsStringAsync();
        newRaw.Should().Contain("child.txt", "重命名后子文件必须直接位于新目录下");

        // Assert②：子项的 Path 字段必须真的被重写。只断言"能看到 child.txt"不够：
        // 浏览按 ParentId 联动，Path 字段陈旧时照样能列出，但按路径下载/移动/扫描
        // 同步全都会拿到旧路径。原实现旧基路径传父目录导致 StartsWith("//") 不成立，
        // 后代被整体跳过、Path 停留在旧位置
        newRaw.Should().Contain($"\"{newFolder}/child.txt\"", "后代 Path 必须跟随重命名");

        // Assert③：旧路径不应再可寻
        (await PathExistsAsync($"{oldFolder}/child.txt")).Should().BeFalse("旧路径记录必须已被重写");
    }

    [Fact]
    public async Task MoveFolder_DescendantPathsFollowMove()
    {
        // Arrange：/{prefix}-mv-src/inner.txt 与目标 /{prefix}-mv-dst
        var username = "submove" + Guid.NewGuid().ToString("N")[..6];
        var token = await GetAuthTokenAsync(username);
        SetAuthToken(token);
        await EnsureClassPoolAsync();
        var srcFolder = $"/{PathPrefix}-mv-src";
        var dstFolder = $"/{PathPrefix}-mv-dst";
        await SeedFolderWithFileAsync(username, srcFolder, "inner.txt");
        await SeedEmptyFolderAsync(username, dstFolder);

        // 诊断断言：物理源目录与库记录都必须真实存在
        // （MoveAsync 的物理失败被 catch 静默跳过，库记录缺失则直接 continue）
        Directory.Exists(Path.Combine(ClassStorageRoot, srcFolder.TrimStart('/')))
            .Should().BeTrue("物理源目录必须已创建");
        (await PathExistsAsync(srcFolder)).Should().BeTrue("库记录必须已种子化");

        // Act：移动
        var move = await Client.PostAsJsonAsync("/api/v1/files/move",
            new { sourcePaths = new[] { srcFolder }, destinationPath = dstFolder });
        move.StatusCode.Should().Be(HttpStatusCode.OK);
        var moveRaw = await move.Content.ReadAsStringAsync();
        moveRaw.Should().Contain("mv-src", "移动结果必须包含被移项（为空说明物理移动抛了异常被 catch 跳过）");

        // Assert
        var browse = await Client.GetAsync($"/api/v1/files/browse{dstFolder}{srcFolder}");
        browse.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await browse.Content.ReadAsStringAsync();
        raw.Should().Contain("inner.txt", "移动后子文件必须位于新位置下");
        raw.Should().Contain($"\"{dstFolder}{srcFolder}/inner.txt\"", "后代 Path 必须跟随移动重写");
    }

    [Fact]
    public async Task SoftDeleteFolderThenRestore_DescendantsRestoreToo()
    {
        // Arrange：/{prefix}-del-tree/keep.txt
        var username = "subdel" + Guid.NewGuid().ToString("N")[..6];
        var token = await GetAuthTokenAsync(username);
        SetAuthToken(token);
        await EnsureClassPoolAsync();
        var delFolder = $"/{PathPrefix}-del-tree";
        await SeedFolderWithFileAsync(username, delFolder, "keep.txt");

        // Act①：软删文件夹（整棵子树进回收站）
        var delete = await Client.PostAsJsonAsync("/api/v1/files/delete",
            new { paths = new[] { delFolder }, permanent = false });
        delete.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act②：从回收站恢复该文件夹
        var recycleRaw = await Client.GetStringAsync("/api/v1/files/recycle-bin");
        var id = ExtractRecycleId(recycleRaw, delFolder);
        var restore = await Client.PostAsync($"/api/v1/files/restore/{id}", null);
        restore.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert：子文件必须跟着恢复（原实现按 IsDeleted = 0 过滤取后代，
        // 软删子树的后代全是 1，实际只恢复根，子文件留在回收站）
        var browse = await Client.GetAsync($"/api/v1/files/browse{delFolder}");
        browse.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await browse.Content.ReadAsStringAsync();
        raw.Should().Contain("keep.txt", "恢复文件夹时后代必须一并恢复，否则目录内容为空");
    }

    [Fact]
    public async Task SoftDeleteFolder_BumpsETagOfAffectedDirectories()
    {
        // Arrange
        var username = "subetag" + Guid.NewGuid().ToString("N")[..6];
        var token = await GetAuthTokenAsync(username);
        SetAuthToken(token);
        await EnsureClassPoolAsync();
        var etagFolder = $"/{PathPrefix}-etag-tree";
        await SeedFolderWithFileAsync(username, etagFolder, "f.txt");

        var before = await Client.GetAsync("/api/v1/files/browse/");
        var etagBefore = before.Headers.ETag?.Tag;
        etagBefore.Should().NotBeNull();

        // Act：软删（根目录列表少一项）
        var delete = await Client.PostAsJsonAsync("/api/v1/files/delete",
            new { paths = new[] { etagFolder }, permanent = false });
        delete.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert：根目录 ETag 必须变（批量 SQL 不跟踪实体，版本号改由服务显式递增）
        var after = await Client.GetAsync("/api/v1/files/browse/");
        after.Headers.ETag?.Tag.Should().NotBe(etagBefore, "软删提交后根目录版本号必须递增");
    }

    // ─── 辅助 ───

    /// <summary>建本类的默认池（挂载点为真实临时目录）。已在则直接返回</summary>
    private async Task EnsureClassPoolAsync()
    {
        var (scope, _, pools, _, _, unitOfWork) = CreateDataScope();
        using (scope)
        {
            if (await pools.GetDefaultAsync() is not null)
                return;

            var pool = StoragePool.Create("SubtreePool", ClassStorageRoot);
            pool.SetAsDefault(true);
            await pools.AddAsync(pool);
            await unitOfWork.SaveChangesAsync();
        }
    }

    private async Task SeedFolderWithFileAsync(string ownerUsername, string folderPath, string fileName)
    {
        await SeedEmptyFolderAsync(ownerUsername, folderPath);

        var (scope, users, pools, fileItems, _, unitOfWork) = CreateDataScope();
        using (scope)
        {
            var user = (await users.GetAllAsync()).First(u => u.Username == ownerUsername);
            var pool = await pools.GetDefaultAsync();
            var parent = await fileItems.GetByPathAsync(folderPath);

            var file = FileItem.CreateFile(
                name: fileName,
                path: $"{folderPath}/{fileName}",
                physicalPath: Path.Combine(ClassStorageRoot, folderPath.TrimStart('/'), fileName),
                sizeBytes: 2048,
                mimeType: "text/plain",
                storagePoolId: pool!.Id,
                ownerId: user.Id,
                parentId: parent?.Id);
            await fileItems.AddAsync(file);
            await unitOfWork.SaveChangesAsync();
        }
    }

    private async Task SeedEmptyFolderAsync(string ownerUsername, string folderPath)
    {
        var (scope, users, pools, fileItems, _, unitOfWork) = CreateDataScope();
        using (scope)
        {
            var user = (await users.GetAllAsync()).First(u => u.Username == ownerUsername);
            var pool = await pools.GetDefaultAsync();
            if (pool is null) return;

            if (await fileItems.GetByPathAsync(folderPath) is not null)
                return;

            var physicalDir = Path.Combine(ClassStorageRoot, folderPath.TrimStart('/'));
            Directory.CreateDirectory(physicalDir);

            var folder = FileItem.CreateFolder(
                name: folderPath.TrimStart('/'),
                path: folderPath,
                physicalPath: physicalDir,
                storagePoolId: pool.Id,
                ownerId: user.Id);
            await fileItems.AddAsync(folder);
            await unitOfWork.SaveChangesAsync();
        }
    }

    private async Task<bool> PathExistsAsync(string path)
    {
        var (scope, _, _, fileItems, _, _) = CreateDataScope();
        using (scope)
        {
            return await fileItems.GetByPathAsync(path) is not null;
        }
    }

    private static Guid ExtractRecycleId(string json, string folderPath)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            if (item.TryGetProperty("path", out var p) &&
                p.GetString() == folderPath)
            {
                return item.GetProperty("id").GetGuid();
            }
        }
        throw new InvalidOperationException($"回收站中未找到 {folderPath}");
    }
}
