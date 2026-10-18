// =============================================================================
// NasSystem.Infrastructure - Media/Mp3Parser.cs
// MP3 时长解析：跳 ID3v2 → 帧头遍历 / Xing 精确帧数
// =============================================================================

namespace NasSystem.Infrastructure.Media;

/// <summary>
/// MP3 时长解析。两条路径：
/// 首帧带 Xing/Info 头（VBR 文件的标准做法）→ 直接读总帧数，精确；
/// 否则顺序遍历帧头累加，CBR 精确、VBR 近似。
/// 不解码音频样本，只读帧头：秒级完成
/// </summary>
public static class Mp3Parser
{
    // MPEG1 Layer3 比特率表（kbps，索引 1-14；0=free，15=bad）
    private static readonly int[] Mpeg1Layer3Bitrates =
        [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0];

    // MPEG2/2.5 Layer3 比特率表
    private static readonly int[] Mpeg2Layer3Bitrates =
        [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0];

    // 采样率表按版本族：[MPEG1, MPEG2, MPEG2.5]
    private static readonly int[][] Samplerates =
    [
        [44100, 48000, 32000],
        [22050, 24000, 16000],
        [11025, 12000, 8000]
    ];

    public static MediaMetadata Extract(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = new BinaryReader(stream);

        SkipId3v2(stream);

        var firstFrameStart = stream.Position;
        var header = ReadFrameHeader(reader);
        if (header is null)
        {
            return MediaMetadata.Empty;
        }

        var bitrate = header.Value.Bitrate;
        var samplerate = header.Value.Samplerate;
        var samplesPerFrame = header.Value.SamplesPerFrame;

        // Xing/Info 头：首帧音频数据开始处（side info 大小随版本与声道数变）
        if (TryReadXingFrames(reader, firstFrameStart, header.Value, out var xingFrames))
        {
            var seconds = xingFrames * samplesPerFrame / (double)samplerate;
            return new MediaMetadata(null, null, seconds);
        }

        // 帧遍历：从首帧开始逐帧累加，遇到不同步的头或文件尾结束
        double totalSeconds = 0;
        stream.Seek(firstFrameStart, SeekOrigin.Begin);
        while (stream.Position + 4 <= stream.Length)
        {
            var frame = ReadFrameHeader(reader);
            if (frame is null || frame.Value.Bitrate == 0 || frame.Value.Samplerate == 0)
            {
                break; // 不同步：可能是尾部标签或损坏，停止累加
            }
            totalSeconds += frame.Value.SamplesPerFrame / (double)frame.Value.Samplerate;
            stream.Seek(frame.Value.FrameLength - 4, SeekOrigin.Current);
        }

        return totalSeconds > 0
            ? new MediaMetadata(null, null, totalSeconds)
            : MediaMetadata.Empty;
    }

    /// <summary>ID3v2 头："ID3" + 版本(2) + 标志(1) + 尺寸(4, syncsafe 每字节 7 位)</summary>
    private static void SkipId3v2(Stream stream)
    {
        if (stream.Length < 10)
            return;

        var header = new byte[10];
        stream.ReadExactly(header, 0, 10);
        if (header[0] != 'I' || header[1] != 'D' || header[2] != '3')
        {
            stream.Seek(0, SeekOrigin.Begin); // 无标签，回到文件头
            return;
        }

        var size = (header[6] << 21) | (header[7] << 14) | (header[8] << 7) | header[9];
        stream.Seek(10 + size, SeekOrigin.Begin);
    }

    private readonly record struct FrameHeader(
        int Bitrate, int Samplerate, int SamplesPerFrame, int FrameLength, int ChannelMode);

    /// <summary>读并解析一个 MPEG 音频帧头。不同步返回 null（流位置停在帧头前）</summary>
    private static FrameHeader? ReadFrameHeader(BinaryReader reader)
    {
        var stream = reader.BaseStream;
        var start = stream.Position;

        var bytes = new byte[4];
        var read = stream.ReadAtLeast(bytes, 4, throwOnEndOfStream: false);
        if (read < 4)
        {
            stream.Seek(start, SeekOrigin.Begin);
            return null;
        }

        // 同步：11 位置 1（前 3 字节是 0xFF 0xE? 形态）
        if (bytes[0] != 0xFF || (bytes[1] & 0xE0) != 0xE0)
        {
            stream.Seek(start, SeekOrigin.Begin);
            return null;
        }

        var versionBits = (bytes[1] >> 3) & 0x03; // 00=2.5, 10=2, 11=1
        var layerBits = (bytes[1] >> 1) & 0x03;   // 01=Layer3
        if (layerBits != 0x01)
        {
            stream.Seek(start, SeekOrigin.Begin);
            return null; // 只支持 Layer3（ mp3 主体）
        }

        var versionIndex = versionBits switch
        {
            0x03 => 0, // MPEG1
            0x02 => 1, // MPEG2
            0x00 => 2, // MPEG2.5
            _ => -1
        };
        if (versionIndex < 0)
        {
            stream.Seek(start, SeekOrigin.Begin);
            return null;
        }

        var bitrateIndex = (bytes[2] >> 4) & 0x0F;
        var samplerateIndex = (bytes[2] >> 2) & 0x03;
        var padding = (bytes[2] >> 1) & 0x01;
        var channelMode = (bytes[3] >> 6) & 0x03; // 3=单声道

        if (bitrateIndex is 0 or 15 || samplerateIndex == 3)
        {
            stream.Seek(start, SeekOrigin.Begin);
            return null;
        }

        var bitrate = versionIndex == 0
            ? Mpeg1Layer3Bitrates[bitrateIndex] * 1000
            : Mpeg2Layer3Bitrates[bitrateIndex] * 1000;
        var samplerate = Samplerates[versionIndex][samplerateIndex];
        var samplesPerFrame = versionIndex == 0 ? 1152 : 576;

        // Layer3 帧长公式（含 padding）
        var frameLength = 144 * bitrate / samplerate + padding;
        if (frameLength <= 4)
        {
            stream.Seek(start, SeekOrigin.Begin);
            return null;
        }

        return new FrameHeader(bitrate, samplerate, samplesPerFrame, frameLength, channelMode);
    }

    /// <summary>
    /// Xing/Info 头取精确总帧数。位置：首帧头(4) + side info
    /// （MPEG1 单声道 17 / 立体声 32，MPEG2 减半）
    /// </summary>
    private static bool TryReadXingFrames(
        BinaryReader reader,
        long firstFrameStart,
        FrameHeader header,
        out int frames)
    {
        frames = 0;
        var stream = reader.BaseStream;

        var sideInfoSize = header.SamplesPerFrame == 1152
            ? (header.ChannelMode == 3 ? 17 : 32)
            : (header.ChannelMode == 3 ? 9 : 17);
        var xingOffset = firstFrameStart + 4 + sideInfoSize;

        if (xingOffset + 8 > stream.Length)
            return false;

        stream.Seek(xingOffset, SeekOrigin.Begin);
        var tag = reader.ReadBytes(4);
        var isXing = tag[0] == 'X' && tag[1] == 'i' && tag[2] == 'n' && tag[3] == 'g';
        var isInfo = tag[0] == 'I' && tag[1] == 'n' && tag[2] == 'f' && tag[3] == 'o';
        if (!isXing && !isInfo)
        {
            return false;
        }

        var flags = reader.ReadBytes(4);
        var flagValue = (flags[0] << 24) | (flags[1] << 16) | (flags[2] << 8) | flags[3];
        const int framesFlag = 0x0001;
        if ((flagValue & framesFlag) == 0)
            return false;

        var framesBytes = reader.ReadBytes(4);
        frames = (framesBytes[0] << 24) | (framesBytes[1] << 16) | (framesBytes[2] << 8) | framesBytes[3];
        return frames > 0;
    }
}
