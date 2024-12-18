using System.Text;

public class RandomStringGenerator
{
    private static Random random = new Random();

    public static string GenerateRandomString(int length, string allowedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")
    {
        StringBuilder result = new StringBuilder(length);
        for (int i = 0; i < length; i++)
        {
            result.Append(allowedChars[random.Next(allowedChars.Length)]);
        }
        return result.ToString();
    }
}

public class DuplexStream : Stream
{
    private readonly Stream _readStream;
    private readonly Stream _writeStream;
    private readonly bool _keepOpen;

    public DuplexStream(Stream readStream, Stream writeStream, bool keepOpen = false)
    {
        _readStream = readStream ?? throw new ArgumentNullException(nameof(readStream));
        _writeStream = writeStream ?? throw new ArgumentNullException(nameof(writeStream));
        _keepOpen = keepOpen;
    }

    public override bool CanRead => _readStream.CanRead;
    public override bool CanSeek => _readStream.CanSeek && _writeStream.CanSeek;
    public override bool CanWrite => _writeStream.CanWrite;
    public override long Length => _readStream.Length;
    public override long Position
    {
        get => _readStream.Position;
        set
        {
            _readStream.Position = value;
            _writeStream.Position = value;
        }
    }

    public override void Flush() => _writeStream.Flush();

    public override async Task FlushAsync(CancellationToken cancellationToken) => await _writeStream.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => _readStream.Read(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin)
    {
        long readPosition = _readStream.Seek(offset, origin);
        _writeStream.Seek(offset, origin);
        return readPosition;
    }

    public override void SetLength(long value)
    {
        _readStream.SetLength(value);
        _writeStream.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count) => _writeStream.Write(buffer, offset, count);

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        await _readStream.ReadAsync(buffer, offset, count, cancellationToken);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        await _readStream.ReadAsync(buffer, cancellationToken);

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        await _writeStream.WriteAsync(buffer, offset, count, cancellationToken);

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        await _writeStream.WriteAsync(buffer, cancellationToken);

    public override int Read(Span<byte> buffer) => _readStream.Read(buffer);

    public override void Write(ReadOnlySpan<byte> buffer) => _writeStream.Write(buffer);

    public override int ReadByte() => _readStream.ReadByte();

    public override void WriteByte(byte value) => _writeStream.WriteByte(value);

    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state) =>
        _readStream.BeginRead(buffer, offset, count, callback, state);

    public override int EndRead(IAsyncResult asyncResult) => _readStream.EndRead(asyncResult);

    public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state) =>
        _writeStream.BeginWrite(buffer, offset, count, callback, state);

    public override void EndWrite(IAsyncResult asyncResult) => _writeStream.EndWrite(asyncResult);

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_keepOpen)
        {
            _readStream?.Dispose();
            _writeStream?.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_keepOpen)
            await Task.WhenAll(_readStream.DisposeAsync().AsTask(), _writeStream.DisposeAsync().AsTask());
    }

    public override bool CanTimeout => _readStream.CanTimeout || _writeStream.CanTimeout;

    public override int ReadTimeout
    {
        get => _readStream.ReadTimeout;
        set => _readStream.ReadTimeout = value;
    }

    public override int WriteTimeout
    {
        get => _writeStream.WriteTimeout;
        set => _writeStream.WriteTimeout = value;
    }
}