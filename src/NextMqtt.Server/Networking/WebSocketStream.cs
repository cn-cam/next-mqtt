using System.Net.WebSockets;

namespace NextMqtt.Server.Networking;

/// <summary>Adapts a WebSocket to the Stream API expected by the broker.</summary>
/// <remarks>Broker 只认 Stream，这里把帧收发映射成读写。</remarks>
public sealed class WebSocketStream : Stream
{
    /// <summary>Accepted socket; disposed by this wrapper.</summary>
    private readonly WebSocket _webSocket;
    /// <summary>Serialises sends, because frames must not interleave.</summary>
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    /// <summary>Set once so disposal runs a single time.</summary>
    private bool _disposed;

    /// <summary>Wraps an accepted WebSocket.</summary>
    /// <remarks>不接管所有权：传入的 socket 由本类负责关闭。</remarks>
    /// <param name="webSocket">Socket already upgraded from HTTP.</param>
    public WebSocketStream(WebSocket webSocket)
    {
        _webSocket = webSocket;
    }

    /// <summary>Readable until the wrapper is disposed.</summary>
    /// <remarks>关闭后返回 false，调用方应停止读取。</remarks>
    public override bool CanRead => !_disposed;

    /// <summary>WebSocket frames are not seekable.</summary>
    /// <remarks>上层不会回退，协议解析本来就是单向的。</remarks>
    public override bool CanSeek => false;

    /// <summary>Writable until the wrapper is disposed.</summary>
    /// <remarks>同上，关闭后不再接受写入。</remarks>
    public override bool CanWrite => !_disposed;

    /// <summary>Unsupported: the stream has no length.</summary>
    /// <remarks>抛 NotSupported 而不是返回 0，避免调用方误判为空流。</remarks>
    public override long Length => throw new NotSupportedException();

    /// <summary>Unsupported: the stream has no position.</summary>
    /// <remarks>读写位置对帧流没有意义。</remarks>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>Reads one frame, returning 0 when the peer closed.</summary>
    /// <remarks>尽量填满调用方的缓冲区；空帧会被跳过。</remarks>
    /// <param name="buffer">Destination memory.</param>
    /// <param name="cancellationToken">Cancels the receive.</param>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var result = await _webSocket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return 0;
            }

            if (result.Count > 0)
            {
                return result.Count;
            }

            if (result.EndOfMessage)
            {
                return 0;
            }
        }
    }

    /// <summary>Array overload that forwards to the memory overload.</summary>
    /// <remarks>只为兼容 Stream 抽象，不额外拷贝。</remarks>
    /// <param name="buffer">Destination array.</param>
    /// <param name="offset">Start offset.</param>
    /// <param name="count">Maximum bytes.</param>
    /// <param name="cancellationToken">Cancels the receive.</param>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <summary>Sends one binary frame atomically.</summary>
    /// <remarks>写锁保证并发发送不会交错；取消时可能已部分写出。</remarks>
    /// <param name="buffer">Frame payload.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _webSocket.SendAsync(buffer, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Array overload that forwards to the memory overload.</summary>
    /// <remarks>只为兼容 Stream 抽象，不额外拷贝。</remarks>
    /// <param name="buffer">Source array.</param>
    /// <param name="offset">Start offset.</param>
    /// <param name="count">Byte count.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <summary>No-op: each write already sends a complete frame.</summary>
    /// <remarks>MQTT 报文即帧，没有缓冲需要刷。</remarks>
    public override void Flush()
    {
    }

    /// <summary>No-op: each write already sends a complete frame.</summary>
    /// <remarks>MQTT 报文即帧，没有缓冲需要刷。</remarks>
    /// <param name="cancellationToken">Ignored.</param>
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Synchronous read; blocks on the async implementation.</summary>
    /// <remarks>只用于兼容，Broker 的路径全部是异步的。</remarks>
    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Synchronous write; blocks on the async implementation.</summary>
    /// <remarks>同上；高并发下会占住线程池线程。</remarks>
    public override void Write(byte[] buffer, int offset, int count)
        => WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Unsupported: WebSocket frames cannot be seeked.</summary>
    /// <remarks>抛异常而不是静默忽略。</remarks>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <summary>Unsupported: the stream length is fixed.</summary>
    /// <remarks>帧流没有可设置的固定长度。</remarks>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>Releases the write lock exactly once.</summary>
    /// <remarks>重复 Dispose 安全，信号量只释放一次。</remarks>
    /// <param name="disposing">True for explicit disposal.</param>
    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            if (disposing)
            {
                _writeLock.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    /// <summary>Closes the WebSocket gracefully, then disposes it.</summary>
    /// <remarks>用 1 秒超时发 CloseOutput，对端不回应就直接释放；异常一律吞掉。</remarks>
    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;

            try
            {
                if (_webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    await _webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "closing", timeout.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // The peer may already be gone.
            }

            _webSocket.Dispose();
            _writeLock.Dispose();
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }
}
