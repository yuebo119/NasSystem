// =============================================================================
// NasSystem.Infrastructure - Media/Mp4Parser.cs
// MP4/MOV 盒结构解析：时长（moov/mvhd）+ 尺寸（trak/tkhd）
// =============================================================================

namespace NasSystem.Infrastructure.Media;

using System.Buffers.Binary;

/// <summary>
/// MP4/MOV 最小解析。只读盒头不碰媒体数据：moov 通常在文件头部，
/// 秒级定位；时长取 mvhd 的 timescale/duration，尺寸取视频轨 tkhd 的
/// 16.16 定点宽高（音频轨的宽高是 0，取非零的那条）
/// </summary>
public static class Mp4Parser
{
    /// <summary>盒递归深度上限：防畸形/恶意嵌套文件把栈跑爆</summary>
    private const int MaxDepth = 6;

    public static MediaMetadata Extract(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = new BinaryReader(stream);

        double? durationSeconds = null;
        int? width = null;
        int? height = null;

        // 顶层盒顺序扫到 moov 为止。moov 在 mdat 之后的文件（罕见流式布局）
        // 会扫到文件尾也没找到，那时按无元数据处理
        while (stream.Position < stream.Length)
        {
            if (!TryReadBoxHeader(reader, out var boxType, out var payloadStart, out var payloadSize))
            {
                break;
            }

            if (boxType == "moov")
            {
                ParseMoov(reader, payloadStart, payloadSize, ref durationSeconds, ref width, ref height, depth: 1);
                break;
            }

            // 跳过当前盒payload（含 mdat 的大 payload 用 seek，不读内容）
            stream.Seek(payloadStart + payloadSize, SeekOrigin.Begin);
        }

        return new MediaMetadata(width, height, durationSeconds);
    }

    private static void ParseMoov(
        BinaryReader reader,
        long payloadStart,
        long payloadSize,
        ref double? durationSeconds,
        ref int? width,
        ref int? height,
        int depth)
    {
        if (depth > MaxDepth)
            return;

        var stream = reader.BaseStream;
        var end = payloadStart + payloadSize;
        stream.Seek(payloadStart, SeekOrigin.Begin);

        while (stream.Position < end)
        {
            if (!TryReadBoxHeader(reader, out var boxType, out var childStart, out var childSize))
            {
                return;
            }

            switch (boxType)
            {
                case "mvhd":
                    durationSeconds = ParseMvhd(reader, childStart);
                    break;
                case "trak":
                    ParseTrak(reader, childStart, childSize, ref width, ref height, depth + 1);
                    break;
            }

            stream.Seek(childStart + childSize, SeekOrigin.Begin);
        }
    }

    private static void ParseTrak(
        BinaryReader reader,
        long payloadStart,
        long payloadSize,
        ref int? width,
        ref int? height,
        int depth)
    {
        if (depth > MaxDepth)
            return;

        var stream = reader.BaseStream;
        var end = payloadStart + payloadSize;
        stream.Seek(payloadStart, SeekOrigin.Begin);

        while (stream.Position < end)
        {
            if (!TryReadBoxHeader(reader, out var boxType, out var childStart, out var childSize))
            {
                return;
            }

            if (boxType == "tkhd")
            {
                var (trackWidth, trackHeight) = ParseTkhd(reader, childStart);
                // 视频轨的宽高非零；音频轨为 0。第一条非零即视频轨
                if (trackWidth > 0 && trackHeight > 0 && width is null)
                {
                    width = trackWidth;
                    height = trackHeight;
                }
            }

            stream.Seek(childStart + childSize, SeekOrigin.Begin);
        }
    }

    /// <summary>mvhd：version 0 与 1 的字段宽度不同，按版本字节分流</summary>
    private static double? ParseMvhd(BinaryReader reader, long payloadStart)
    {
        var stream = reader.BaseStream;
        stream.Seek(payloadStart, SeekOrigin.Begin);
        var version = reader.ReadByte();
        reader.ReadBytes(3); // flags

        if (version == 1)
        {
            reader.ReadBytes(16); // creation + modification（各 8 字节）
            var timescale = BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
            var duration = BinaryPrimitives.ReadUInt64BigEndian(reader.ReadBytes(8));
            return timescale > 0 ? duration / (double)timescale : null;
        }

        reader.ReadBytes(8); // creation + modification（各 4 字节）
        var scale = BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
        var dur = BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
        return scale > 0 ? dur / (double)scale : null;
    }

    /// <summary>tkhd version 0：matrix 后的 4 字节宽 + 4 字节高，16.16 定点</summary>
    private static (int? Width, int? Height) ParseTkhd(BinaryReader reader, long payloadStart)
    {
        var stream = reader.BaseStream;
        stream.Seek(payloadStart, SeekOrigin.Begin);
        var version = reader.ReadByte();
        reader.ReadBytes(3); // flags

        if (version == 1)
        {
            reader.ReadBytes(32); // creation(8) + modification(8) + trackID(4) + reserved(4) + duration(8)
        }
        else
        {
            reader.ReadBytes(16); // creation(4) + modification(4) + trackID(4) + reserved(4)
            reader.ReadBytes(4);  // duration
        }

        reader.ReadBytes(8);  // reserved
        reader.ReadBytes(8);  // layer(2) + alternate_group(2) + volume(2) + reserved(2)
        reader.ReadBytes(36); // matrix

        var rawWidth = BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
        var rawHeight = BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
        return ((int)(rawWidth >> 16), (int)(rawHeight >> 16));
    }

    /// <summary>读盒头：size(4) + type(4)。size 为 1 时 64 位扩展尺寸（罕见大盒）</summary>
    private static bool TryReadBoxHeader(
        BinaryReader reader,
        out string boxType,
        out long payloadStart,
        out long payloadSize)
    {
        boxType = string.Empty;
        payloadStart = 0;
        payloadSize = 0;

        var stream = reader.BaseStream;
        if (stream.Position + 8 > stream.Length)
            return false;

        var size = BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
        var typeBytes = reader.ReadBytes(4);
        boxType = System.Text.Encoding.ASCII.GetString(typeBytes);
        payloadStart = stream.Position;

        if (size == 1)
        {
            // 64 位扩展尺寸：真实尺寸跟在后 8 字节
            if (stream.Position + 8 > stream.Length)
                return false;
            payloadSize = unchecked((long)BinaryPrimitives.ReadUInt64BigEndian(reader.ReadBytes(8))) - 16;
            payloadStart += 8;
        }
        else if (size == 0)
        {
            // size 0 = 延伸到文件尾（仅允许出现在最后一个盒）
            payloadSize = stream.Length - payloadStart;
        }
        else
        {
            payloadSize = size - 8;
        }

        return payloadSize >= 0;
    }
}
