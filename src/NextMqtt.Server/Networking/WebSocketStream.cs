using System.Net.WebSockets;

namespace NextMqtt.Server.Networking;

/// <summary>Adapts a WebSocket to the Stream API expected by the broker.</summary>
public sealed class WebSocketStream : Stream
{
    private readonly WebSocket _webSocket;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    /// <summary>Wraps an accepted WebSocket.</summary>
    /// <param name="webSocket">Socket already upgraded from HTTP.</param>
    public WebSocketStream(WebSocket webSocket)
    {
        _webSocket = webSocket;
    }

    /// <summary>Readable until the wrapper is disposed.</summary>
    public override bool CanRead => !_disposed;

    /// <summary>WebSocket frames are not seekable.</summary>
    public override bool CanSeek => false;

    /// <summary>Writable until the wrapper is disposed.</summary>
    public override bool CanWrite => !_disposed;

    /// <summary>Unsupported: the stream has no length.</summary>
    public override long Length => throw new NotSupportedException();

    /// <summary>Unsupported: the stream has no position.</summary>
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
    /// <param name="buffer">Source array.</param>
    /// <param name="offset">Start offset.</param>
    /// <param name="count">Byte count.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <summary>No-op: each write already sends a complete frame.</summary>
    public override void Flush()
    {
    }

    /// <summary>No-op: each write already sends a complete frame.</summary>
    /// <param name="cancellationToken">Ignored.</param>
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Synchronous read; blocks on the async implementation.</summary>
    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Synchronous write; blocks on the async implementation.</summary>
    public override void Write(byte[] buffer, int offset, int count)
        => WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Unsupported: WebSocket frames cannot be seeked.</summary>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <summary>Unsupported: the stream length is fixed.</summary>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>Releases the write lock exactly once.</summary>
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
