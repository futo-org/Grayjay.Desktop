using System.Text;

namespace Grayjay.ClientServer.Proxy
{
    public class HttpProxyStream : Stream
    {
        private readonly Stream _innerStream;
        private readonly byte[] _buffer = new byte[4096];
        private int _bufferPosition = 0;
        private int _bufferLength = 0;
        private readonly bool _keepOpen;

        public override bool CanRead => _innerStream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => _innerStream.CanWrite;

        public override long Length => _innerStream.Length;
        public override long Position
        {
            get => _innerStream.Position - _bufferLength + _bufferPosition;
            set => throw new NotImplementedException();
        }

        private const bool SHOULD_LOG = false;
        private readonly BinaryWriter? _writeLogger;
        private readonly BinaryWriter? _readLogger;

        static  int _counter = 0;

        public override bool CanTimeout => _innerStream.CanTimeout;

        public override int ReadTimeout
        {
            get => _innerStream.ReadTimeout;
            set => _innerStream.ReadTimeout = value;
        }

        public override int WriteTimeout
        {
            get => _innerStream.WriteTimeout;
            set => _innerStream.WriteTimeout = value;
        }

        public HttpProxyStream(Stream innerStream, bool keepOpen = false)
        {
            if (SHOULD_LOG)
            {
                var id = Guid.NewGuid();
                if (!Directory.Exists("proxy_logs"))
                    Directory.CreateDirectory("proxy_logs");

                var counter = Interlocked.Increment(ref _counter);
                _writeLogger = SHOULD_LOG ? new BinaryWriter(File.OpenWrite($"proxy_logs/{counter}_" + id + "_write_stream.bin")) : null;
                _readLogger = SHOULD_LOG ? new BinaryWriter(File.OpenWrite($"proxy_logs/{counter}_" + id + "_read_stream.bin")) : null;
            }

            _innerStream = innerStream;
            _keepOpen = keepOpen;
        }

        public override void Flush()
        {
            _innerStream.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return _innerStream.FlushAsync(cancellationToken);
        }

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
        {
            var tcs = new TaskCompletionSource<int>(state);
            ReadAsync(buffer, offset, count).ContinueWith(t =>
            {
                if (t.IsFaulted) tcs.TrySetException(t.Exception.InnerException ?? new Exception());
                else if (t.IsCanceled) tcs.TrySetCanceled();
                else tcs.TrySetResult(t.Result);

                callback?.Invoke(tcs.Task);
            }, TaskScheduler.Default);

            return tcs.Task;
        }

        public override int EndRead(IAsyncResult asyncResult)
        {
            return ((Task<int>)asyncResult).Result;
        }

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
        {
            var tcs = new TaskCompletionSource(state);
            WriteAsync(buffer, offset, count).ContinueWith(t =>
            {
                if (t.IsFaulted) tcs.TrySetException(t.Exception.InnerException ?? new Exception());
                else if (t.IsCanceled) tcs.TrySetCanceled();
                else tcs.TrySetResult();

                callback?.Invoke(tcs.Task);
            }, TaskScheduler.Default);

            return tcs.Task;
        }

        public override void EndWrite(IAsyncResult asyncResult)
        {
            ((Task)asyncResult).Wait();
        }

        private void LogReadIfNecessary(byte[] data, int offset, int count)
        {
            if (_readLogger != null)
            {
                _readLogger.Write(data, offset, count);
                _readLogger.Flush();
            }
        }

        public async Task<byte[]> ReadUntilEndOfHeadersAsync(CancellationToken cancellationToken = default)
        {
            using MemoryStream headerStream = new MemoryStream();
            int foundNewlines = 0;

            while (true)
            {
                if (_bufferPosition >= _bufferLength)
                {
                    _bufferLength = await _innerStream.ReadAsync(_buffer, 0, _buffer.Length, cancellationToken);
                    LogReadIfNecessary(_buffer, 0, _bufferLength);

                    if (_bufferLength == 0)
                        throw new InvalidOperationException("End of stream reached before end of headers.");

                    _bufferPosition = 0;
                }

                for (int i = _bufferPosition; i < _bufferLength; i++)
                {
                    byte b = _buffer[i];
                    if (b == '\r' || b == '\n')
                    {
                        if ((b == '\n' && foundNewlines % 2 == 1) || (b == '\r' && foundNewlines % 2 == 0))
                            foundNewlines++;
                        else
                            foundNewlines = 0;
                        if (foundNewlines == 4)
                        {
                            headerStream.Write(_buffer, _bufferPosition, i - _bufferPosition + 1);
                            _bufferPosition = i + 1;
                            return headerStream.ToArray();
                        }
                    }
                    else
                    {
                        foundNewlines = 0;
                    }
                }

                headerStream.Write(_buffer, _bufferPosition, _bufferLength - _bufferPosition);
                _bufferPosition = _bufferLength;
            }

            throw new InvalidOperationException("End of headers not found.");
        }

        public async Task<string> ReadLineAsync(CancellationToken cancellationToken = default)
        {
            using MemoryStream lineStream = new MemoryStream();
            bool foundCarriageReturn = false;

            while (true)
            {
                if (_bufferPosition >= _bufferLength)
                {
                    _bufferLength = await _innerStream.ReadAsync(_buffer, 0, _buffer.Length, cancellationToken);
                    LogReadIfNecessary(_buffer, 0, _bufferLength);

                    if (_bufferLength == 0)
                    {
                        if (lineStream.Length == 0) 
                            throw new InvalidOperationException("End of stream reached before the end of the line.");
                        break;
                    }
                    _bufferPosition = 0;
                }

                for (int i = _bufferPosition; i < _bufferLength; i++)
                {
                    if (_buffer[i] == '\r')
                    {
                        foundCarriageReturn = true;
                    }
                    else if (_buffer[i] == '\n' && foundCarriageReturn)
                    {
                        lineStream.Write(_buffer, _bufferPosition, i - _bufferPosition - 1);
                        _bufferPosition = i + 1;
                        return Encoding.UTF8.GetString(lineStream.ToArray());
                    }
                    else
                    {
                        foundCarriageReturn = false;
                    }
                }

                // Write the part of the buffer that was read if no newline was found
                int lengthToWrite = foundCarriageReturn ? _bufferLength - _bufferPosition - 1 : _bufferLength - _bufferPosition;
                lineStream.Write(_buffer, _bufferPosition, lengthToWrite);
                _bufferPosition = _bufferLength;
            }

            return Encoding.UTF8.GetString(lineStream.ToArray());
        }

        public async Task TransferAllChunksAsync(HttpProxyStream outputStream, bool concatChunks = false, CancellationToken cancellationToken = default)
        {
            while (true)
            {
                string? sizeLine = await ReadLineAsync(cancellationToken);
                if (sizeLine == null)
                    throw new Exception("Size line expected.");

                if (!concatChunks)
                    await outputStream.WriteLineAsync(sizeLine, cancellationToken);

                int chunkSize = Convert.ToInt32(sizeLine, 16);
                if (chunkSize == 0)
                {
                    // Skip the empty line after the last chunk
                    await ReadLineAsync(cancellationToken);
                    if (!concatChunks)
                        await outputStream.WriteLineAsync("", cancellationToken);
                    break;
                }

                await TransferFixedLengthContentAsync(outputStream, chunkSize, cancellationToken);

                //Line break after chunk
                await ReadLineAsync(cancellationToken);
                if (!concatChunks)
                    await outputStream.WriteLineAsync("", cancellationToken);
            }
        }

        public async Task TransferFixedLengthContentAsync(Stream outputStream, int contentLength, CancellationToken cancellationToken = default)
        {
            var buffer = new byte[8192];
            int totalRead = 0;

            while (totalRead < contentLength)
            {
                int toRead = Math.Min(buffer.Length, contentLength - totalRead);
                int read = await ReadAsync(buffer, 0, toRead, cancellationToken);
                if (read == 0)
                    break;

                await outputStream.WriteAsync(buffer, 0, read, cancellationToken);
                totalRead += read;
            }
        }

        public async Task TransferUntilEndOfStreamAsync(Stream outputStream, CancellationToken cancellationToken = default)
        {
            var buffer = new byte[8192];
            int read;

            while ((read = await ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                await outputStream.WriteAsync(buffer, 0, read, cancellationToken);
        }

        public async Task WriteLineAsync(string line, CancellationToken cancellationToken = default)
        {
            await WriteAsync(Encoding.UTF8.GetBytes(line + "\r\n"), cancellationToken);
        }

        public async Task<HttpProxyRequest> ReadRequestHeadersAsync(CancellationToken cancellationToken = default)
        {
            return HttpProxyRequest.FromBytes(await ReadUntilEndOfHeadersAsync(cancellationToken));
        }

        public async Task<HttpProxyResponse> ReadResponseHeadersAsync(CancellationToken cancellationToken = default)
        {
            return HttpProxyResponse.FromBytes(await ReadUntilEndOfHeadersAsync(cancellationToken));
        }

        public async Task WriteRequestAsync(HttpProxyRequest httpProxyRequest, CancellationToken cancellationToken = default)
        {
            await WriteAsync(httpProxyRequest.ToBytes(), cancellationToken);
        }

        public async Task WriteResponseAsync(HttpProxyResponse httpProxyResponse, CancellationToken cancellationToken = default)
        {
            await WriteAsync(httpProxyResponse.ToBytes(), cancellationToken);
            if (httpProxyResponse.Data != null)
                await WriteAsync(httpProxyResponse.Data, cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int totalBytesRead = 0;
            while (count > 0)
            {
                if (_bufferPosition >= _bufferLength)
                {
                    _bufferLength = _innerStream.Read(_buffer, 0, _buffer.Length);
                    LogReadIfNecessary(_buffer, 0, _bufferLength);

                    if (_bufferLength == 0) break; // End of stream
                    _bufferPosition = 0;
                }

                int bytesToCopy = Math.Min(count, _bufferLength - _bufferPosition);
                Array.Copy(_buffer, _bufferPosition, buffer, offset, bytesToCopy);
                _bufferPosition += bytesToCopy;
                offset += bytesToCopy;
                count -= bytesToCopy;
                totalBytesRead += bytesToCopy;
            }

            return totalBytesRead;
        }

        public override int Read(Span<byte> buffer)
        {
            int totalBytesRead = 0;
            while (buffer.Length > 0)
            {
                if (_bufferPosition >= _bufferLength)
                {
                    _bufferLength = _innerStream.Read(_buffer, 0, _buffer.Length);
                    LogReadIfNecessary(_buffer, 0, _bufferLength);

                    if (_bufferLength == 0) break; // End of stream
                    _bufferPosition = 0;
                }

                int bytesToCopy = Math.Min(buffer.Length, _bufferLength - _bufferPosition);
                _buffer.AsSpan(_bufferPosition, bytesToCopy).CopyTo(buffer.Slice(0, bytesToCopy));
                _bufferPosition += bytesToCopy;
                buffer = buffer.Slice(bytesToCopy);
                totalBytesRead += bytesToCopy;
            }

            return totalBytesRead;
        }

        public override int ReadByte()
        {
            if (_bufferPosition >= _bufferLength)
            {
                _bufferLength = _innerStream.Read(_buffer, 0, _buffer.Length);
                LogReadIfNecessary(_buffer, 0, _bufferLength);

                if (_bufferLength == 0) return -1; // End of stream
                _bufferPosition = 0;
            }

            return _buffer[_bufferPosition++];
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int totalBytesRead = 0;
            while (count > 0)
            {
                if (_bufferPosition >= _bufferLength)
                {
                    _bufferLength = await _innerStream.ReadAsync(_buffer, 0, _buffer.Length, cancellationToken);
                    LogReadIfNecessary(_buffer, 0, _bufferLength);

                    if (_bufferLength == 0) break; // End of stream
                    _bufferPosition = 0;
                }

                int bytesToCopy = Math.Min(count, _bufferLength - _bufferPosition);
                Array.Copy(_buffer, _bufferPosition, buffer, offset, bytesToCopy);
                _bufferPosition += bytesToCopy;
                offset += bytesToCopy;
                count -= bytesToCopy;
                totalBytesRead += bytesToCopy;
            }

            return totalBytesRead;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int totalBytesRead = 0;
            while (buffer.Length > 0)
            {
                if (_bufferPosition >= _bufferLength)
                {
                    _bufferLength = await _innerStream.ReadAsync(_buffer, cancellationToken);
                    LogReadIfNecessary(_buffer, 0, _bufferLength);

                    if (_bufferLength == 0) break; // End of stream
                    _bufferPosition = 0;
                }

                int bytesToCopy = Math.Min(buffer.Length, _bufferLength - _bufferPosition);
                _buffer.AsMemory(_bufferPosition, bytesToCopy).CopyTo(buffer);
                _bufferPosition += bytesToCopy;
                buffer = buffer.Slice(bytesToCopy);
                totalBytesRead += bytesToCopy;
            }

            return totalBytesRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _innerStream.Write(buffer, offset, count);
            if (_writeLogger != null)
            {
                _writeLogger.Write(buffer, offset, count);
                _writeLogger.Flush();
            }
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _innerStream.Write(buffer);
            if (_writeLogger != null)
            {
                _writeLogger.Write(buffer);
                _writeLogger.Flush();
            }
        }

        public override void WriteByte(byte value)
        {
            _innerStream.WriteByte(value);
             if (_writeLogger != null)
             {
                _writeLogger.Write(value);
                _writeLogger.Flush();
             }
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _innerStream.WriteAsync(buffer, offset, count, cancellationToken);
            if (_writeLogger != null)
            {
                _writeLogger.Write(buffer, offset, count);
                _writeLogger.Flush();
            }
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _innerStream.WriteAsync(buffer, cancellationToken);
            if (_writeLogger != null)
            {
                _writeLogger.Write(buffer.Span);
                _writeLogger.Flush();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!_keepOpen)
                    _innerStream.Dispose();
                _writeLogger?.Dispose();
                _readLogger?.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_keepOpen)
                await _innerStream.DisposeAsync();
            if (_writeLogger != null)
                await _writeLogger.DisposeAsync();
            if (_readLogger != null)
                await _readLogger.DisposeAsync();
            await base.DisposeAsync();
        }

        public override void CopyTo(Stream destination, int bufferSize)
        {
            byte[] buffer = new byte[bufferSize];
            int bytesRead;
            while ((bytesRead = Read(buffer, 0, buffer.Length)) != 0)
            {
                destination.Write(buffer, 0, bytesRead);
            }
        }

        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[bufferSize];
            int bytesRead;
            while ((bytesRead = await ReadAsync(buffer, 0, buffer.Length, cancellationToken)) != 0)
            {
                await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken);
            }
        }
    }
}