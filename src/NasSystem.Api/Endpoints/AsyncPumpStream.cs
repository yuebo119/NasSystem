// =============================================================================
// NasSystem.Api - Endpoints/AsyncPumpStream.cs
// 同步写入口、异步泵送到下游的桥接流
// =============================================================================

namespace NasSystem.Api.Endpoints;

/// <summary>
/// 让"只会同步 Write 的生产者"（ZipArchive 的条目与 central directory 写入）
/// 能流向"只允许异步写的下游"（Kestrel 响应体、CryptoStream）。
/// </summary>
/// <remarks>
/// 存在理由：.NET 6+ Kestrel 的响应体禁同步 IO（AllowSynchronousIO 已移除，
/// 无开关），而 <see cref="System.IO.Compression.ZipArchive"/> 对 backingStream
/// 的写入含同步路径（entries dispose 与 central directory 均实测触发
/// "Synchronous operations are disallowed"）。把 ZipArchive 的流换成本类：
/// 同步 Write 只落内存缓冲，缓冲满或 Dispose 时经 WriteAsync 泵送到下游，
/// 下游收到的全部是异步写。
///
/// 背压策略：缓冲上限（默认 1MB）内直接承接；超出时同步等待本次 WriteAsync
/// 完成（GetAwaiter().GetResult）。阻塞只发生在消费慢于生产的背压窗口，
/// 不是全程阻塞——个人 NAS 单用户导出的可接受代价，换取零 OOM 的流式语义。
/// </remarks>
internal sealed class AsyncPumpStream : Stream
{
    private const int DefaultBufferSize = 1024 * 1024;

    private readonly Stream _inner;
    private readonly byte[] _buffer;
    private int _length;

    public AsyncPumpStream(Stream inner, int bufferSize = DefaultBufferSize)
    {
        _inner = inner;
        _buffer = new byte[bufferSize];
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) =>
        Write(new ReadOnlySpan<byte>(buffer, offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        while (!buffer.IsEmpty)
        {
            if (_length == _buffer.Length)
                PumpBufferBlocking();

            var chunk = Math.Min(buffer.Length, _buffer.Length - _length);
            buffer[..chunk].CopyTo(_buffer.AsSpan(_length));
            _length += chunk;
            buffer = buffer[chunk..];
        }
    }

    public override void Flush() => PumpBufferBlocking();

    /// <summary>
    /// 同步排空：把缓冲经 WriteAsync 送往下游并等待完成。
    /// 等待的是"异步写完成"这个信号，不是同步调下游 Write
    /// </summary>
    private void PumpBufferBlocking()
    {
        if (_length == 0)
            return;
        var pending = _buffer.AsMemory(0, _length);
        _length = 0;
        _inner.WriteAsync(pending).GetAwaiter().GetResult();
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_length == 0)
            return;
        var pending = _buffer.AsMemory(0, _length);
        _length = 0;
        await _inner.WriteAsync(pending, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            PumpBufferBlocking();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_length > 0)
        {
            var pending = _buffer.AsMemory(0, _length);
            _length = 0;
            await _inner.WriteAsync(pending);
        }
        await base.DisposeAsync();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();
}
