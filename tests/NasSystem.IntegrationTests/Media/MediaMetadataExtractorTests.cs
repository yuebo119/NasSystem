// =============================================================================
// NasSystem.IntegrationTests - Media/MediaMetadataExtractorTests.cs
// 媒体元数据解析器（ROADMAP Phase 3）
// =============================================================================

namespace NasSystem.IntegrationTests.Media;

using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using NasSystem.Infrastructure.Media;
using Xunit;

/// <summary>
/// 三个解析器的锁定测试。测试文件全部手工构造（盒/EBML/帧头都是
/// 公开格式规范），不依赖样本素材：解析逻辑的边界（版本 0/1 分流、
/// vint 变长、字节序）用构造数据精确覆盖，真实样本只验证"格式猜对了"
/// </summary>
public class MediaMetadataExtractorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"media-test-{Guid.NewGuid():N}");

    public MediaMetadataExtractorTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // 句柄未释放时留下，由系统临时目录清理
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Mp4_Version0Header_ExtractsDurationAndSize()
    {
        // mvhd v0：timescale=1000，duration=5000 → 5 秒；
        // tkhd v0：1920x1080（16.16 定点）
        var mp4 = BuildMp4(timescale: 1000, duration: 5000, width: 1920, height: 1080, mvhdVersion: 0);
        var path = WriteFile("v0.mp4", mp4);

        var metadata = MediaMetadataExtractor.Extract(path, ".mp4");

        metadata.DurationSeconds.Should().Be(5.0);
        metadata.Width.Should().Be(1920);
        metadata.Height.Should().Be(1080);
    }

    [Fact]
    public void Mp4_Version1Header_ExtractsDuration()
    {
        // mvhd v1：creation/modification 各 8 字节，duration 8 字节
        var mp4 = BuildMp4(timescale: 600, duration: 1800, width: 640, height: 480, mvhdVersion: 1);
        var path = WriteFile("v1.mp4", mp4);

        var metadata = MediaMetadataExtractor.Extract(path, ".mp4");

        metadata.DurationSeconds.Should().Be(3.0);
        metadata.Width.Should().Be(640);
        metadata.Height.Should().Be(480);
    }

    [Fact]
    public void Mkv_ExtractsDurationAndSize()
    {
        // Duration=12500（TimecodeScale 单位），TimecodeScale=1000000 纳秒 → 12.5 秒
        var mkv = BuildMkv(duration: 12500, timecodeScale: 1_000_000, width: 3840, height: 2160);
        var path = WriteFile("v.mkv", mkv);

        var metadata = MediaMetadataExtractor.Extract(path, ".mkv");

        metadata.DurationSeconds.Should().BeApproximately(12.5, 0.001);
        metadata.Width.Should().Be(3840);
        metadata.Height.Should().Be(2160);
    }

    [Fact]
    public void Mp3_WithXingHeader_ExtractsExactDuration()
    {
        // 1000 帧 × 1152 样本 / 44100Hz ≈ 26.12 秒
        var mp3 = BuildMp3WithXing(frames: 1000, bitrateKbps: 128, samplerate: 44100);
        var path = WriteFile("xing.mp3", mp3);

        var metadata = MediaMetadataExtractor.Extract(path, ".mp3");

        metadata.DurationSeconds.Should().BeApproximately(26.122, 0.01);
        metadata.Width.Should().BeNull("音频无尺寸");
    }

    [Fact]
    public void Mp3_WithoutXing_FallsBackToFrameCounting()
    {
        // 两个完整帧：2 × 1152 / 44100 ≈ 0.0523 秒
        var mp3 = BuildMp3Frames(frameCount: 2, bitrateKbps: 128, samplerate: 44100);
        var path = WriteFile("frames.mp3", mp3);

        var metadata = MediaMetadataExtractor.Extract(path, ".mp3");

        metadata.DurationSeconds.Should().BeApproximately(0.0523, 0.001);
    }

    [Fact]
    public void Mp3_WithId3v2Tag_SkipsTagAndStillParses()
    {
        var id3 = BuildId3v2(payloadSize: 100);
        var mp3 = BuildMp3WithXing(frames: 500, bitrateKbps: 192, samplerate: 44100);
        var path = WriteFile("tagged.mp3", [.. id3, .. mp3]);

        var metadata = MediaMetadataExtractor.Extract(path, ".mp3");

        // 500 帧 × 1152 / 44100 ≈ 13.06 秒：ID3 跳对了才有数
        metadata.DurationSeconds.Should().BeApproximately(13.061, 0.01);
    }

    [Fact]
    public void UnsupportedExtension_ReturnsEmpty()
    {
        var path = WriteFile("doc.pdf", [1, 2, 3, 4]);

        var metadata = MediaMetadataExtractor.Extract(path, ".pdf");

        metadata.HasAny.Should().BeFalse();
    }

    [Fact]
    public void CorruptFile_ReturnsEmpty_NotThrows()
    {
        // 声明是 mp4 但内容是垃圾：解析必须降级为空而不是抛
        var path = WriteFile("corrupt.mp4", Encoding.UTF8.GetBytes("not a real mp4 at all"));

        var act = () => MediaMetadataExtractor.Extract(path, ".mp4");

        act.Should().NotThrow();
        MediaMetadataExtractor.Extract(path, ".mp4").HasAny.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // 测试文件构造
    // -------------------------------------------------------------------------

    private string WriteFile(string name, byte[] content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    /// <summary>盒：size(4) + type(4) + payload</summary>
    private static byte[] Box(string type, params byte[][] payloads)
    {
        var payloadLength = payloads.Sum(p => p.Length);
        var box = new byte[8 + payloadLength];
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)(8 + payloadLength));
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        var offset = 8;
        foreach (var payload in payloads)
        {
            payload.CopyTo(box, offset);
            offset += payload.Length;
        }
        return box;
    }

    private static byte[] Be32(uint value)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        return buffer;
    }

    private static byte[] Be64(ulong value)
    {
        var buffer = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
        return buffer;
    }

    /// <summary>ftyp + moov(mvhd + trak(tkhd))。mvhdVersion 0/1 两态都构造</summary>
    private static byte[] BuildMp4(uint timescale, uint duration, int width, int height, byte mvhdVersion)
    {
        var ftyp = Box("ftyp", Encoding.ASCII.GetBytes("isom"), Be32(0x200));

        var mvhdPayload = new List<byte> { mvhdVersion, 0, 0, 0 };
        if (mvhdVersion == 1)
        {
            mvhdPayload.AddRange(Be64(0)); // creation
            mvhdPayload.AddRange(Be64(0)); // modification
            mvhdPayload.AddRange(Be32(timescale));
            mvhdPayload.AddRange(Be64(duration));
        }
        else
        {
            mvhdPayload.AddRange(Be32(0)); // creation
            mvhdPayload.AddRange(Be32(0)); // modification
            mvhdPayload.AddRange(Be32(timescale));
            mvhdPayload.AddRange(Be32(duration));
        }
        mvhdPayload.AddRange(Be32(0x00010000)); // rate
        mvhdPayload.AddRange(new byte[2 + 2 + 2 + 2 + 36 + 24]); // volume/ reserved / matrix / predefined
        mvhdPayload.AddRange(Be32(2)); // next track id
        var mvhd = Box("mvhd", mvhdPayload.ToArray());

        var tkhdPayload = new List<byte> { 0, 0, 0, 0 }; // version 0 + flags
        tkhdPayload.AddRange(Be32(0));  // creation
        tkhdPayload.AddRange(Be32(0));  // modification
        tkhdPayload.AddRange(Be32(1));  // track id
        tkhdPayload.AddRange(Be32(0));  // reserved
        tkhdPayload.AddRange(Be32(duration));
        tkhdPayload.AddRange(new byte[8]);  // reserved
        tkhdPayload.AddRange(new byte[8]);  // layer + alternate + volume + reserved
        tkhdPayload.AddRange(new byte[36]); // matrix
        tkhdPayload.AddRange(Be32((uint)width << 16));
        tkhdPayload.AddRange(Be32((uint)height << 16));
        var tkhd = Box("tkhd", tkhdPayload.ToArray());
        var trak = Box("trak", tkhd);
        var moov = Box("moov", mvhd, trak);

        return [.. ftyp, .. moov];
    }

    /// <summary>EBML 元素：id 字节序列 + vint 尺寸 + payload</summary>
    private static byte[] Ebml(byte[] id, byte[] payload)
    {
        var size = EncodeVint((ulong)payload.Length);
        return [.. id, .. size, .. payload];
    }

    /// <summary>vint 编码（含长度标记位），只用到 8 字节内的常见长度</summary>
    private static byte[] EncodeVint(ulong value)
    {
        if (value < 0x7F)
            return [(byte)(value | 0x80)];
        if (value < 0x3FFF)
            return [(byte)((value >> 8) | 0x40), (byte)(value & 0xFF)];
        if (value < 0x1FFFFF)
            return [(byte)((value >> 16) | 0x20), (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF)];
        return
        [
            (byte)((value >> 24) | 0x10), (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF)
        ];
    }

    private static byte[] EbmlId(params byte[] id) => id;

    /// <summary>EBML uint：大端定长</summary>
    private static byte[] EbmlUint(ulong value, int length) =>
        Enumerable.Range(0, length)
            .Select(i => (byte)((value >> (8 * (length - 1 - i))) & 0xFF))
            .ToArray();

    /// <summary>EBML float64：大端</summary>
    private static byte[] EbmlFloat64(double value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return bytes;
    }

    private static byte[] BuildMkv(double duration, ulong timecodeScale, int width, int height)
    {
        var info = Ebml(EbmlId(0x15, 0x49, 0xA9, 0x66), [
            .. Ebml(EbmlId(0x44, 0x89), EbmlFloat64(duration)),
            .. Ebml(EbmlId(0x2A, 0xD7, 0xB1), EbmlUint(timecodeScale, 4))
        ]);

        var video = Ebml(EbmlId(0xE0), [
            .. Ebml(EbmlId(0xB0), EbmlUint((ulong)width, 2)),
            .. Ebml(EbmlId(0xBA), EbmlUint((ulong)height, 2))
        ]);
        var trackEntry = Ebml(EbmlId(0xAE), video);
        var tracks = Ebml(EbmlId(0x16, 0x54, 0xAE, 0x6B), trackEntry);

        var segmentPayload = new List<byte> { };
        segmentPayload.AddRange(info);
        segmentPayload.AddRange(tracks);
        var segment = Ebml(EbmlId(0x18, 0x53, 0x80, 0x67), segmentPayload.ToArray());

        return segment;
    }

    /// <summary>MPEG1 Layer3 帧头（128kbps/44100Hz/立体声/无 padding）</summary>
    private static byte[] Mp3FrameHeader(int bitrateKbps, int samplerate)
    {
        var bitrateIndex = Mpeg1L3Index(bitrateKbps);
        var samplerateIndex = samplerate switch
        {
            44100 => 0,
            48000 => 1,
            32000 => 2,
            _ => throw new ArgumentException($"测试仅支持 44100/48000/32000: {samplerate}")
        };
        return
        [
            0xFF,
            0xFB, // MPEG1 + Layer3 + 无 CRC
            (byte)((bitrateIndex << 4) | (samplerateIndex << 2)), // 无 padding
            0x00  // 立体声
        ];
    }

    private static int Mpeg1L3Index(int kbps) => kbps switch
    {
        32 => 1,
        64 => 4,
        96 => 6,
        128 => 9,
        192 => 12,
        256 => 13,
        320 => 14,
        _ => throw new ArgumentException($"测试仅支持常用比特率: {kbps}")
    };

    /// <summary>帧长 = 144 × 比特率 / 采样率 + padding（Layer3）</summary>
    private static int FrameLength(int bitrateKbps, int samplerate) =>
        144 * (bitrateKbps * 1000) / samplerate;

    /// <summary>带 Xing 头的 MP3：帧头 + 32 字节 side info + Xing + flags + 帧数</summary>
    private static byte[] BuildMp3WithXing(int frames, int bitrateKbps, int samplerate)
    {
        var header = Mp3FrameHeader(bitrateKbps, samplerate);
        var xing = new List<byte>();
        xing.AddRange(header);
        xing.AddRange(new byte[32]); // MPEG1 立体声 side info
        xing.AddRange(Encoding.ASCII.GetBytes("Xing"));
        xing.AddRange(Be32(0x0001)); // flags：仅 frames
        xing.AddRange(Be32((uint)frames));
        return xing.ToArray();
    }

    /// <summary>无 Xing 的 MP3：N 个完整帧（帧体填零）</summary>
    private static byte[] BuildMp3Frames(int frameCount, int bitrateKbps, int samplerate)
    {
        var header = Mp3FrameHeader(bitrateKbps, samplerate);
        var frameLength = FrameLength(bitrateKbps, samplerate);
        var frame = new byte[frameLength];
        header.CopyTo(frame, 0);

        var file = new List<byte>();
        for (var i = 0; i < frameCount; i++)
        {
            file.AddRange(frame);
        }
        return file.ToArray();
    }

    /// <summary>ID3v2 头 + 指定长度填充（syncsafe 尺寸）</summary>
    private static byte[] BuildId3v2(int payloadSize)
    {
        var header = new List<byte>();
        header.AddRange(Encoding.ASCII.GetBytes("ID3"));
        header.AddRange([0x03, 0x00]); // v2.3
        header.Add(0x00);             // flags
        // syncsafe：每字节只用低 7 位
        header.Add((byte)((payloadSize >> 21) & 0x7F));
        header.Add((byte)((payloadSize >> 14) & 0x7F));
        header.Add((byte)((payloadSize >> 7) & 0x7F));
        header.Add((byte)(payloadSize & 0x7F));
        header.AddRange(new byte[payloadSize]);
        return header.ToArray();
    }
}
