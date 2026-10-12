namespace Grayjay.ClientServer.Helpers;

/// <summary>
/// Read-only wrapper that fails a read with <see cref="IOException"/> when the inner stream delivers no data within the idle timeout.
/// Only time spent waiting inside a read counts. Cancelling <c>cancel</c> aborts a pending read with <see cref="OperationCanceledException"/>.
/// </summary>
public sealed class IdleTimeoutStream : Stream
{
    private readonly Stream _inner;
    private readonly TimeSpan _idleTimeout;
    private readonly CancellationToken _cancel;
    private CancellationTokenSource _timeoutSource;

    public IdleTimeoutStream(Stream inner, TimeSpan idleTimeout, CancellationToken cancel = default)
    {
        _inner = inner;
        _idleTimeout = idleTimeout;
        _cancel = cancel;
        _timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancel);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    // The managed HTTP response stream only honours timeouts through cancellable async reads.
    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        // A timer that fired just after the previous read completed leaves the source cancelled; start a fresh one.
        if (!_timeoutSource.TryReset())
        {
            _timeoutSource.Dispose();
            _timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(_cancel);
        }

        using var readSource = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(_timeoutSource.Token, cancellationToken)
            : null;
        _timeoutSource.CancelAfter(_idleTimeout);
        try
        {
            return await _inner.ReadAsync(buffer, readSource?.Token ?? _timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!_cancel.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new IOException($"No data received for {_idleTimeout.TotalSeconds:0.#} seconds");
        }
        finally
        {
            _timeoutSource.CancelAfter(Timeout.InfiniteTimeSpan);
        }
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
            _timeoutSource.Dispose();
        }
        base.Dispose(disposing);
    }
}
