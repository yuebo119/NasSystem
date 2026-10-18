// =============================================================================
// NasSystem.IntegrationTests - ExportEndpointTests.cs
// 3-2-1 导出端点测试（ROADMAP Phase 2）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using NasSystem.Application.Users.Dtos;
using NasSystem.Domain.Entities;
using Xunit;

/// <summary>
/// 3-2-1 导出：明文 ZIP round-trip、加密包格式（openssl enc 兼容）、
/// 校验路径（不存在/非目录）、认证要求。
/// </summary>
/// <remarks>
/// 加密验证用 .NET 侧同参数解密（PBKDF2-SHA256-10000 → key32+iv16），
/// 锁的是与 openssl enc -pbkdf2 互操作的算法契约：Salted__ 魔数 +
/// 8 字节 salt 前置 + 派生长度 48。openssl 实机互操作在部署环境
/// 人工验收（本机无 openssl 保证，Git Bash 的 openssl 仅作开发辅助）。
/// </remarks>
public class ExportEndpointTests : ApiTestBase
{
    public ExportEndpointTests(NasSystemWebApplicationFactory factory) : base(factory)
    {
    }

    private string _storageRoot = string.Empty;

    /// <summary>建默认池（挂载点到临时目录）+ 一棵两层目录树（含一个空目录）。
    /// 根路径带唯一后缀：测试共享工厂与数据库，固定路径会撞 FileItems.Path
    /// 唯一索引（IClassFixture 下第二个用例即红）</summary>
    private async Task<string> SeedExportTreeAsync()
    {
        var root = $"/Docs-{Guid.NewGuid():N}";
        _storageRoot = Path.Combine(Path.GetTempPath(), $"ExportTest_{Guid.NewGuid():N}");
        var (scope, users, pools, fileItems, _, unitOfWork) = CreateDataScope();
        using (scope)
        {
            var user = await users.GetByUsernameAsync("admin");
            var pool = StoragePool.Create($"ExportPool_{Guid.NewGuid():N}", _storageRoot);
            pool.SetAsDefault(true);
            await pools.AddAsync(pool);
            await unitOfWork.SaveChangesAsync();

            var docs = FileItem.CreateFolder(
                name: "Docs", path: root, physicalPath: _storageRoot,
                storagePoolId: pool.Id, ownerId: user!.Id);
            await fileItems.AddAsync(docs);
            await unitOfWork.SaveChangesAsync();
            var sub = FileItem.CreateFolder(
                name: "sub", path: $"{root}/sub", physicalPath: Path.Combine(_storageRoot, "sub"),
                storagePoolId: pool.Id, ownerId: user.Id, parentId: docs.Id);
            await fileItems.AddAsync(sub);
            await unitOfWork.SaveChangesAsync();
            var empty = FileItem.CreateFolder(
                name: "empty", path: $"{root}/empty", physicalPath: Path.Combine(_storageRoot, "empty"),
                storagePoolId: pool.Id, ownerId: user.Id, parentId: docs.Id);
            await fileItems.AddAsync(empty);
            var readme = FileItem.CreateFile(
                name: "readme.txt", path: $"{root}/readme.txt", physicalPath: Path.Combine(_storageRoot, "readme.txt"),
                sizeBytes: 12, mimeType: "text/plain", storagePoolId: pool.Id, ownerId: user.Id,
                parentId: docs.Id, hash: "h-readme");
            var data = FileItem.CreateFile(
                name: "data.bin", path: $"{root}/sub/data.bin", physicalPath: Path.Combine(_storageRoot, "sub", "data.bin"),
                sizeBytes: 2048, mimeType: "application/octet-stream", storagePoolId: pool.Id, ownerId: user.Id,
                parentId: sub.Id, hash: "h-data");
            await fileItems.AddAsync(readme);
            await fileItems.AddAsync(data);
            await unitOfWork.SaveChangesAsync();

            // 物理文件（导出扫的是物理目录，库记录只用于校验与取路径）。
            // 池挂载点即 Docs 自身的物理路径，子树相对其下
            Directory.CreateDirectory(Path.Combine(_storageRoot, "sub"));
            Directory.CreateDirectory(Path.Combine(_storageRoot, "empty"));
            File.WriteAllText(Path.Combine(_storageRoot, "readme.txt"), "hello export");
            File.WriteAllBytes(Path.Combine(_storageRoot, "sub", "data.bin"), new byte[2048]);
        }
        return root;
    }

    private async Task<string> AdminTokenAsync()
    {
        var loginRequest = new { Username = "admin", Password = "admin123" };
        var response = await Client.PostAsJsonAsync("/api/v1/users/login", loginRequest);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResultDto>>(JsonOptions);
        return result?.Data?.Token ?? throw new InvalidOperationException("admin 登录失败");
    }

    private HttpRequestMessage CreateExportRequest(string token, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/exports")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    [Fact]
    public async Task Export_PlainZip_ContainsTreeWithEmptyDir()
    {
        var root = await SeedExportTreeAsync();
        var token = await AdminTokenAsync();

        var response = await Client.SendAsync(CreateExportRequest(token, new { rootPath = root }));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/zip");
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var names = archive.Entries.Select(e => e.FullName).OrderBy(n => n).ToList();
        // 口径：非空目录不写目录条目（ZIP 规范允许，解压时按需创建），
        // 空目录写斜杠结尾条目（否则空目录结构丢失）
        names.Should().BeEquivalentTo(new[]
        {
            "Docs/empty/",
            "Docs/readme.txt",
            "Docs/sub/data.bin"
        });

        var readme = archive.GetEntry("Docs/readme.txt")!;
        using var reader = new StreamReader(readme.Open(), Encoding.UTF8);
        (await reader.ReadToEndAsync()).Should().Be("hello export");
    }

    [Fact]
    public async Task Export_Encrypted_HasSaltedHeaderAndRoundTrips()
    {
        var root = await SeedExportTreeAsync();
        var token = await AdminTokenAsync();
        const string Password = "correct horse battery staple";

        var response = await Client.SendAsync(CreateExportRequest(
            token, new { rootPath = root, password = Password }));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/octet-stream");
        response.Content.Headers.ContentDisposition?.FileName?.ToString()
            .Should().Contain(".zip.enc", "加密产物的文件名后缀");

        var bytes = await response.Content.ReadAsByteArrayAsync();

        // Salted__ 魔数 + 8 字节 salt（openssl enc 输出同构）
        Encoding.ASCII.GetString(bytes, 0, 8).Should().Be("Salted__");
        var salt = bytes[8..16];

        // .NET 侧同参数解密：PBKDF2-SHA256-10000 → key32 + iv16。
        // 参数与 ExportResult 的加密端逐项对齐，round-trip 通过即证明
        // 派生/分块/填充三项契约自洽
        var derived = Rfc2898DeriveBytes.Pbkdf2(
            Password, salt, 10000, HashAlgorithmName.SHA256, 48);
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var decryptor = aes.CreateDecryptor(derived[..32], derived[32..48]);
        using var plain = new MemoryStream();
        await using (var crypto = new CryptoStream(
            new MemoryStream(bytes[16..]), decryptor, CryptoStreamMode.Read))
        {
            await crypto.CopyToAsync(plain);
        }
        plain.Position = 0;

        using var archive = new ZipArchive(plain, ZipArchiveMode.Read);
        var names = archive.Entries.Select(e => e.FullName).OrderBy(n => n).ToList();
        names.Should().BeEquivalentTo(new[]
        {
            "Docs/empty/", "Docs/readme.txt", "Docs/sub/data.bin"
        }, "解密后的明文 zip 应与明文导出同构");

        var data = archive.GetEntry("Docs/sub/data.bin")!;
        (data.Length).Should().Be(2048, "二进制条目内容完整");
    }

    [Fact]
    public async Task Export_NonExistentPath_Returns404()
    {
        var token = await AdminTokenAsync();
        var response = await Client.SendAsync(CreateExportRequest(token, new { rootPath = "/NoSuchDir" }));
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Export_FilePathInsteadOfDirectory_Returns400()
    {
        var root = await SeedExportTreeAsync();
        var token = await AdminTokenAsync();
        var response = await Client.SendAsync(CreateExportRequest(token, new { rootPath = $"{root}/readme.txt" }));
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Export_WithoutToken_Returns401()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/exports")
        {
            Content = JsonContent.Create(new { rootPath = "/Docs" })
        };
        var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }
}
