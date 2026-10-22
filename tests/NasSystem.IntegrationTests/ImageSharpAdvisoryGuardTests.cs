// =============================================================================
// NasSystem.IntegrationTests - ImageSharpAdvisoryGuardTests.cs
// ImageSharp 已知漏洞的包级守护与恶意样本解码验证（审计 F-074）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Diagnostics;
using System.Reflection;
using System.Text;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

/// <summary>
/// 锁 2026-10-22 审计 F-074 的处置结论。
///
/// 背景：ImageSharp 3.1.12（经 ElBruno.LocalEmbeddings 的 CLIP 预处理传递引入）
/// 带 5 条 advisory，全部在 3.2.0 修复：
///   - ICC CLUT 解析用未校验的通道/网格维度做分配（越界读与超大分配）
///   - HistogramEqualization 用未校验亮度作直方图下标
///   - BigTIFF IFD 计数可让解码线程陷入非推进循环（DoS）
///   - TIFF CCITT T6 编码器写越界（编码器侧）
/// 处置：Directory.Packages.props 钉 3.2.0 + 开启 CPM 传递钉住（见该文件注释）。
///
/// 本测试做三件事：
/// 一、版本守护：解析到的 ImageSharp 必须 ≥ 3.2.0（防传递依赖降级回漏洞版）
/// 二、BigTIFF 恶意计数样本：解码必须在有界时间内以可捕获异常失败，
///     不得挂死（这是本项目唯一真正可达的解码侧攻击面之一）
/// 三、ICC 恶意维度样本：嵌入带超大 CLUT 网格声明的 ICC profile 的 JPEG，
///     解码不得崩溃或耗尽内存（3.2.0 的校验应拒绝）
///
/// 说明：HistogramEqualization 与 CCITT T6 编码器两条 advisory 落在本项目
/// 从不调用的代码路径上（只解码，不做直方图均衡与 TIFF 编码），代码级
/// 不可达；包级修复已覆盖，无需单独样本。
/// </summary>
public class ImageSharpAdvisoryGuardTests
{
    [Fact]
    public void ImageSharp_ResolvedVersion_IsPatched()
    {
        // 注意：SixLabors 的 3.x 系列 AssemblyVersion 固定为 3.0.0（不随包版本走），
        // 所以不能用 Assembly.GetName().Version 判包版本；改用 InformationalVersion
        //（形如 "3.2.0+<commit>"）或 FileVersionInfo 的 FileVersion
        var assembly = typeof(Image).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var fileVersion = string.IsNullOrEmpty(assembly.Location)
            ? null
            : FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion;

        var raw = informational ?? fileVersion ?? string.Empty;
        var numeric = new string(raw.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        Version.TryParse(numeric, out var parsed).Should().BeTrue($"应能从版本串解析出版本号，实际: {raw}");

        // 3.2.0 曾是 GitHub 元数据里的 3.x 修复版，但 NuGet 审计实测仍把它
        // 判为受 5 条 advisory 影响（--force restore 复现）；4.x 线的修复版是
        // 4.1.3，且零警告。守护线取 4.1.3：任何降级都会让这条先红
        parsed!.Should().BeGreaterThanOrEqualTo(new Version(4, 1, 3),
            "ImageSharp 必须 ≥ 4.1.3：3.2.0 在 NuGet 审计下仍带 5 条 advisory，4.1.3 是零警告的修复版");
    }
    [Fact]
    public void ElBrunoImageEmbeddings_LoadsAgainstImageSharp4_AndPixelApiStillWorks()
    {
        // 为什么需要这条：ImageSharp 的包级修复在 3.x 线止于 3.2.0，而 NuGet 的
        // 审计数据仍把 3.2.0 判为受影响（5 条 advisory 都在，--force restore
        // 实测）。4.x 线的修复版是 4.1.3（advisory 的 patched_versions 明确写了），
        // 但 ElBruno.LocalEmbeddings.ImageEmbeddings 1.6.4 是针对 ImageSharp 3.x
        // 编译的（引用版本 3.0.0.0）。.NET Core 允许加载更高版本，风险只剩
        // 「它用的成员在 4.x 被删/改签名」。
        //
        // 实测 ElBruno 程序集的元数据引用：SixLabors.ImageSharp / .PixelFormats /
        // .Processing 三个命名空间，关键成员是 ProcessPixelRows（像素访问）与
        // ImageEncoder。本条用同样的调用形态做编译期+运行期双证：
        // 编译通过 = 成员存在；运行通过 = 语义未变。
        var elb = Assembly.Load("ElBruno.LocalEmbeddings.ImageEmbeddings");
        elb.Should().NotBeNull("ElBruno 程序集必须能在 ImageSharp 4.x 下解析加载");

        using var image = new Image<Rgba32>(8, 8);
        var visited = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                visited += row.Length;
            }
        });
        visited.Should().Be(64, "ProcessPixelRows 的访问语义必须与 3.x 一致");

        image.Mutate(x => x.Resize(4, 4));
        image.Width.Should().Be(4);
    }

    [Fact]
    public async Task Decode_CraftedBigTiff_WithHugeIfdCount_FailsFastWithoutHanging()
    {
        var payload = BuildBigTiffWithHugeIfdCount();

        var decode = Task.Run(() =>
        {
            using var image = Image.Load(payload);
            return image.Width;
        });

        // 修复前该样本可让解码线程陷入非推进循环（GHSA-wmxv-xphr-5c9g）；
        // 修复后应立刻抛 ImageFormatException 之类的可捕获异常
        var completed = await Task.WhenAny(decode, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.Should().BeSameAs(decode,
            "BigTIFF 恶意 IFD 计数必须在有界时间内失败，不得挂死解码线程（DoS 面）");

        decode.IsFaulted.Should().BeTrue(
            "3.2.0 应校验 IFD 计数并拒绝该样本，而不是尝试读完整个声明长度");
        decode.Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task Decode_JpegWithOversizedIccClut_DoesNotCrashOrHang()
    {
        var payload = BuildJpegWithOversizedIccClut();

        var decode = Task.Run(() =>
        {
            using var image = Image.Load(payload);
            return (image.Width, image.Height);
        });

        var completed = await Task.WhenAny(decode, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.Should().BeSameAs(decode,
            "带超大 CLUT 声明的 ICC profile 不得让解码线程挂死（越界读/超大分配面）");

        // 两种结局都可接受：拒绝（3.2.0 的校验）或正常解码（忽略坏 ICC）。
        // 唯一不可接受的是进程级崩溃（OOM/越界访问），那会让本测试进程直接死掉，
        // 也就是说走到这里的断言本身就证明了没有硬崩
        if (decode.IsFaulted)
        {
            decode.Exception.Should().NotBeNull();
        }
        else
        {
            decode.Result.Width.Should().BeGreaterThan(0);
        }
    }

    // ─── 恶意样本构造 ───

    /// <summary>
    /// 最小 BigTIFF 头 + 一个声明 IFD 项数为 ulong.MaxValue 的目录。
    /// 结构：II 0x2B 0x00（小端）｜offsetSize=8｜保留 0｜首个 IFD 偏移 16｜
    /// 偏移 16 处是 8 字节的项数 = 0xFFFFFFFFFFFFFFFF。
    /// 无校验的实现会按该计数逐项读直到输入耗尽或溢出计算，
    /// 修复版在读取前拒绝。
    /// </summary>
    private static byte[] BuildBigTiffWithHugeIfdCount()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)'I');
        w.Write((byte)'I');
        w.Write((ushort)0x2B);        // BigTIFF magic
        w.Write((ushort)8);           // offset 字节数
        w.Write((ushort)0);           // 保留
        w.Write((ulong)16);           // 首个 IFD 的偏移
        w.Write(ulong.MaxValue);      // IFD 项数（恶意声明）
        // 只附少量数据：真实项数远大于可用字节，纯校验实现应立即拒绝
        w.Write(new byte[32]);
        return ms.ToArray();
    }

    /// <summary>
    /// 正常 JPEG（ImageSharp 自己编码）注入一个 APP2 ICC 段：
    /// profile 头 128 字节 + 1 个 'mft2'（CLUT）标签，
    /// 网格点声明为每通道 255（15 通道下是天文数字），
    /// 对应 GHSA-gwg2-r3hj-4w44 的未校验维度分配面。
    /// </summary>
    private static byte[] BuildJpegWithOversizedIccClut()
    {
        byte[] jpeg;
        using (var image = new Image<Rgba32>(4, 4))
        using (var ms = new MemoryStream())
        {
            image.SaveAsJpeg(ms, new JpegEncoder { Quality = 80 });
            jpeg = ms.ToArray();
        }

        var profile = BuildIccProfileWithOversizedClut();

        // APP2 段：FFE2 + 长度(2) + "ICC_PROFILE\0" + 序号(1) + 总数(1) + profile
        var segmentData = new List<byte>();
        segmentData.AddRange(Encoding.ASCII.GetBytes("ICC_PROFILE"));
        segmentData.Add(0);
        segmentData.Add(1);   // 第 1 段
        segmentData.Add(1);   // 共 1 段
        segmentData.AddRange(profile);

        var length = segmentData.Count + 2;
        if (length > 0xFFFF)
            throw new InvalidOperationException("ICC 样本过大");

        using var output = new MemoryStream();
        output.Write(jpeg, 0, 2); // SOI
        output.WriteByte(0xFF);
        output.WriteByte(0xE2);
        output.WriteByte((byte)(length >> 8));
        output.WriteByte((byte)(length & 0xFF));
        output.Write(segmentData.ToArray(), 0, segmentData.Count);
        output.Write(jpeg, 2, jpeg.Length - 2);
        return output.ToArray();
    }

    private static byte[] BuildIccProfileWithOversizedClut()
    {
        // 'mft2'（lut16Type）：type sig(4) + 保留(4) + 输入通道(1) + 输出通道(1)
        // + 网格点(1) + 填充(1) + 矩阵(36) + 输入表(2×n) + CLUT + 输出表
        var clut = new List<byte>();
        clut.AddRange(Encoding.ASCII.GetBytes("mft2"));
        clut.AddRange(new byte[4]);
        clut.Add(15);   // 输入通道：15（远超常规 3/4 通道）
        clut.Add(15);   // 输出通道：15
        clut.Add(255);  // 网格点：255（15^15 量级的分配，恶意声明）
        clut.Add(0);
        clut.AddRange(new byte[36]); // 3x3 矩阵
        clut.AddRange(new byte[4]);  // 输入表最小值/最大值占位

        var tagData = clut.ToArray();

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        // 头部 128 字节
        var header = new byte[128];
        w.Write(header);
        w.Write(new byte[] { 0, 0, 0, 1 });          // 标签数 1
        w.Write(Encoding.ASCII.GetBytes("mft2"));    // 标签签名
        w.Write(128 + 4 + 12);                       // 数据偏移（头 + 计数 + 单条表项）
        w.Write(tagData.Length);                     // 数据长度
        w.Write(tagData);
        return ms.ToArray();
    }
}
