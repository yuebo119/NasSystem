// =============================================================================
// NasSystem.Infrastructure - Media/MkvParser.cs
// MKV/WebM EBML 解析：时长（Segment/Info/Duration）+ 尺寸（TrackEntry/Video）
// =============================================================================

namespace NasSystem.Infrastructure.Media;

/// <summary>
/// MKV/WebM 最小解析。EBML 元素 ID 与尺寸都是变长整数（vint）：
/// 只walk到 Info 与 Tracks 两个目标元素，遇到 Cluster（媒体数据主体）
/// 直接跳过不深究。Duration 是浮点（单位由 TimecodeScale 定，
/// 默认 1000000 纳秒），PixelWidth/PixelHeight 是无符号整数
/// </summary>
public static class MkvParser
{
    // 目标元素 ID（EBML 完整 ID，含长度标记位）
    private const uint Segment = 0x18538067;
    private const uint Info = 0x1549A966;
    private const uint Tracks = 0x1654AE6B;
    private const uint TrackEntry = 0xAE;
    private const uint Video = 0xE0;
    private const uint PixelWidth = 0xB0;
    private const uint PixelHeight = 0xBA;
    private const uint Duration = 0x4489;
    private const uint TimecodeScale = 0x2AD7B1;

    public static MediaMetadata Extract(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        double? durationSeconds = null;
        int? width = null;
        int? height = null;
        double timecodeScale = 1_000_000; // EBML 默认：纳秒

        // 顶层找 Segment
        while (true)
        {
            if (!TryReadElementId(stream, out var id))
                break;
            if (!TryReadVint(stream, out var size))
                break;

            if (id == Segment)
            {
                var segmentEnd = stream.Position + size;
                // Segment 直接子元素里找 Info / Tracks
                while (stream.Position < segmentEnd)
                {
                    if (!TryReadElementId(stream, out var childId))
                        break;
                    if (!TryReadVint(stream, out var childSize))
                        break;

                    var childEnd = stream.Position + childSize;
                    if (childId == Info)
                    {
                        ParseInfo(stream, childEnd, ref durationSeconds, ref timecodeScale);
                    }
                    else if (childId == Tracks)
                    {
                        ParseTracks(stream, childEnd, ref width, ref height);
                    }

                    stream.Seek(childEnd, SeekOrigin.Begin);
                }
                break;
            }

            stream.Seek(stream.Position + size, SeekOrigin.Begin);
        }

        // Duration 单位 = TimecodeScale 纳秒
        if (durationSeconds.HasValue)
        {
            durationSeconds = durationSeconds.Value * timecodeScale / 1_000_000_000.0;
        }

        return new MediaMetadata(width, height, durationSeconds);
    }

    private static void ParseInfo(
        Stream stream, long end, ref double? duration, ref double timecodeScale)
    {
        while (stream.Position < end)
        {
            if (!TryReadElementId(stream, out var id))
                return;
            if (!TryReadVint(stream, out var size))
                return;

            var payloadEnd = stream.Position + size;
            if (id == Duration)
            {
                duration = ReadFloat(stream, size);
            }
            else if (id == TimecodeScale)
            {
                var scale = ReadUint(stream, size);
                if (scale > 0)
                    timecodeScale = scale;
            }

            stream.Seek(payloadEnd, SeekOrigin.Begin);
        }
    }

    private static void ParseTracks(Stream stream, long end, ref int? width, ref int? height)
    {
        while (stream.Position < end)
        {
            if (!TryReadElementId(stream, out var id))
                return;
            if (!TryReadVint(stream, out var size))
                return;

            var entryEnd = stream.Position + size;
            if (id == TrackEntry)
            {
                ParseTrackEntry(stream, entryEnd, ref width, ref height);
            }

            stream.Seek(entryEnd, SeekOrigin.Begin);
        }
    }

    private static void ParseTrackEntry(Stream stream, long end, ref int? width, ref int? height)
    {
        while (stream.Position < end)
        {
            if (!TryReadElementId(stream, out var id))
                return;
            if (!TryReadVint(stream, out var size))
                return;

            var videoEnd = stream.Position + size;
            if (id == Video)
            {
                // Video 直接子元素里取 PixelWidth/PixelHeight
                while (stream.Position < videoEnd)
                {
                    if (!TryReadElementId(stream, out var propId))
                        return;
                    if (!TryReadVint(stream, out var propSize))
                        return;

                    var propEnd = stream.Position + propSize;
                    if (propId == PixelWidth && width is null)
                        width = (int?)ReadUint(stream, propSize);
                    else if (propId == PixelHeight && height is null)
                        height = (int?)ReadUint(stream, propSize);

                    stream.Seek(propEnd, SeekOrigin.Begin);
                }
            }

            stream.Seek(videoEnd, SeekOrigin.Begin);
        }
    }

    // -------------------------------------------------------------------------
    // EBML 基础读法
    // -------------------------------------------------------------------------

    /// <summary>
    /// 读元素 ID（vint，保留长度标记位）。ID 的 vint 长度由首字节的
    /// 前导零数量决定（标记位是 0，数据位是 1）
    /// </summary>
    private static bool TryReadElementId(Stream stream, out uint id)
    {
        id = 0;
        var first = stream.ReadByte();
        if (first < 0)
            return false;

        int length;
        if ((first & 0x80) != 0) length = 1;
        else if ((first & 0x40) != 0) length = 2;
        else if ((first & 0x20) != 0) length = 3;
        else if ((first & 0x10) != 0) length = 4;
        else return false; // 更长的 ID 不在 MKV 常用范围

        id = (uint)first;
        for (var i = 1; i < length; i++)
        {
            var next = stream.ReadByte();
            if (next < 0)
                return false;
            id = (id << 8) | (byte)next;
        }
        return true;
    }

    /// <summary>读 vint 尺寸（长度标记位是 1，其后是数据位）</summary>
    private static bool TryReadVint(Stream stream, out long value)
    {
        value = 0;
        var first = stream.ReadByte();
        if (first < 0)
            return false;

        int length;
        byte mask;
        if ((first & 0x80) != 0) { length = 1; mask = 0x7F; }
        else if ((first & 0x40) != 0) { length = 2; mask = 0x3F; }
        else if ((first & 0x20) != 0) { length = 3; mask = 0x1F; }
        else if ((first & 0x10) != 0) { length = 4; mask = 0x0F; }
        else if ((first & 0x08) != 0) { length = 5; mask = 0x07; }
        else if ((first & 0x04) != 0) { length = 6; mask = 0x03; }
        else if ((first & 0x02) != 0) { length = 7; mask = 0x01; }
        else if ((first & 0x01) != 0) { length = 8; mask = 0x00; }
        else return false;

        value = first & mask;
        for (var i = 1; i < length; i++)
        {
            var next = stream.ReadByte();
            if (next < 0)
                return false;
            value = (value << 8) | (byte)next;
        }

        // 全 1 的未知尺寸（流式录制常见）：按读到当前位置处理
        return true;
    }

    /// <summary>
    /// EBML 浮点：4 字节 float32、8 字节 float64，0 字节为 0。
    /// EBML 按大端存储，BitConverter 按本机字节序解析：
    /// 小端机器（x86/ARM 常见）要把大端字节反转后再喂，
    /// 大端机器原生可读。条件写反的症状是解析出 1e-317 量级的
    /// denormal（大端字节被当小端读）
    /// </summary>
    private static double? ReadFloat(Stream stream, long size)
    {
        var buffer = new byte[size];
        var read = stream.ReadAtLeast(buffer, (int)size, throwOnEndOfStream: false);
        if (read < size)
            return null;

        if (size == 4)
        {
            var span = buffer.AsSpan(0, 4);
            if (BitConverter.IsLittleEndian)
                span.Reverse();
            return BitConverter.ToSingle(span);
        }
        if (size == 8)
        {
            var span = buffer.AsSpan(0, 8);
            if (BitConverter.IsLittleEndian)
                span.Reverse();
            return BitConverter.ToDouble(span);
        }
        return 0;
    }

    /// <summary>EBML 无符号整数：大端，不足 8 字节左对齐补齐</summary>
    private static long ReadUint(Stream stream, long size)
    {
        if (size <= 0 || size > 8)
            return 0;

        var buffer = new byte[size];
        var read = stream.ReadAtLeast(buffer, (int)size, throwOnEndOfStream: false);
        if (read < size)
            return 0;

        long value = 0;
        foreach (var b in buffer)
        {
            value = (value << 8) | b;
        }
        return value;
    }
}
