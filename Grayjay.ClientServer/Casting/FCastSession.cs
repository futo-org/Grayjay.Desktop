namespace Grayjay.ClientServer.Casting;

using System;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Grayjay.Desktop.POC;

public enum SessionState
{
    Idle,
    WaitingForLength,
    WaitingForData,
    Disconnected
}

public enum Opcode
{
    None = 0,
    Play,
    Pause,
    Resume,
    Stop,
    Seek,
    PlaybackUpdate,
    VolumeUpdate,
    SetVolume,
    PlaybackError,
    SetSpeed,
    Version,
    Ping,
    Pong
}

public class FCastSession : IDisposable
{
    private const int LengthBytes = 4;
    private const int MaximumPacketLength = 32000;
    private byte[] _buffer = new byte[MaximumPacketLength];
    private int _bytesRead;
    private int _packetLength;
    private Stream _stream;
    private SemaphoreSlim _writerSemaphore = new SemaphoreSlim(1);
    private SemaphoreSlim _readerSemaphore = new SemaphoreSlim(1);
    private SessionState _state;

    public event Action<PlaybackUpdateMessage>? OnPlaybackUpdate;
    public event Action<VolumeUpdateMessage>? OnVolumeUpdate;
    public event Action<PlaybackErrorMessage>? OnPlaybackError;
    public event Action? OnPong;
    public event Action<VersionMessage>? OnVersion;

    public FCastSession(Stream stream)
    {
        _stream = stream;
        _state = SessionState.Idle;
    }

    public async Task SendMessageAsync(Opcode opcode, CancellationToken cancellationToken)
    {
        await _writerSemaphore.WaitAsync();

        try
        {
            int size = 1;
            byte[] header = new byte[LengthBytes + 1];
            Array.Copy(BitConverter.GetBytes(size), header, LengthBytes);
            header[LengthBytes] = (byte)opcode;

            Logger.i(nameof(FCastSession), $"Sent {header.Length} bytes with (opcode: {opcode}, header size: {header.Length}, no body).");
            Logger.i(nameof(FCastSession), "Sent bytes: " + Convert.ToHexString(header));
            await _stream.WriteAsync(header, cancellationToken);
        }
        finally
        {
            _writerSemaphore.Release();
        }
    }

    public async Task SendMessageAsync<T>(Opcode opcode, T message, CancellationToken cancellationToken) where T : class
    {
        await _writerSemaphore.WaitAsync();

        try
        {
            string json = JsonSerializer.Serialize(message);
            byte[] data = Encoding.UTF8.GetBytes(json);
            int size = 1 + data.Length;
            byte[] header = new byte[LengthBytes + 1];
            Array.Copy(BitConverter.GetBytes(size), header, LengthBytes);
            header[LengthBytes] = (byte)opcode;

            byte[] packet = new byte[header.Length + data.Length];
            header.CopyTo(packet, 0);
            data.CopyTo(packet, header.Length);

            Logger.i(nameof(FCastSession), $"Sent {packet.Length} bytes with (opcode: {opcode}, header size: {header.Length}, body size: {data.Length}, body: {json}).");
            Logger.i(nameof(FCastSession), "Sent bytes: " + Convert.ToHexString(packet));
            await _stream.WriteAsync(packet, cancellationToken);
        }
        finally
        {
            _writerSemaphore.Release();
        }
    }

    public async Task ReceiveLoopAsync(CancellationToken cancellationToken = default)
    {
        Logger.i(nameof(FCastSession), "Start receiving.");
        _state = SessionState.WaitingForLength;

        byte[] buffer = new byte[1024];
        while (!cancellationToken.IsCancellationRequested)
        {
            await _readerSemaphore.WaitAsync();

            int bytesRead;
            try
            {
                bytesRead = await _stream.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0)
                {
                    Logger.i(nameof(FCastSession), "Connection shutdown gracefully.");
                    Dispose();
                    break;
                }
            }
            finally
            {
                _readerSemaphore.Release();
            }

            Logger.i(nameof(FCastSession), "Read bytes: " + Convert.ToHexString(buffer, 0, bytesRead));
            await ProcessBytesAsync(buffer, bytesRead, cancellationToken);
        }

        _state = SessionState.Idle;
    }

    private async Task ProcessBytesAsync(byte[] receivedBytes, int length, CancellationToken cancellationToken)
    {
        Logger.i(nameof(FCastSession), $"{length} bytes received");

        switch (_state)
        {
            case SessionState.WaitingForLength:
                await HandleLengthBytesAsync(receivedBytes, 0, length, cancellationToken);
                break;
            case SessionState.WaitingForData:
                await HandlePacketBytesAsync(receivedBytes, 0, length, cancellationToken);
                break;
            default:
                Logger.i(nameof(FCastSession), $"Data received is unhandled in current session state {_state}");
                break;
        }
    }

    private async Task HandleLengthBytesAsync(byte[] receivedBytes, int offset, int length, CancellationToken cancellationToken)
    {
        int bytesToRead = Math.Min(LengthBytes, length);
        Buffer.BlockCopy(receivedBytes, offset, _buffer, _bytesRead, bytesToRead);
        _bytesRead += bytesToRead;

        Logger.i(nameof(FCastSession), $"handleLengthBytes: Read {bytesToRead} bytes from packet");

        if (_bytesRead >= LengthBytes)
        {
            _state = SessionState.WaitingForData;
            _packetLength = BinaryPrimitives.ReadInt32LittleEndian(_buffer);
            _bytesRead = 0;

            Logger.i(nameof(FCastSession), $"Packet length header received from: {_packetLength}");

            if (_packetLength > MaximumPacketLength)
            {
                Logger.i(nameof(FCastSession), $"Maximum packet length is 32kB, killing stream: {_packetLength}");
                Dispose();
                _state = SessionState.Disconnected;
                throw new InvalidOperationException($"Stream killed due to packet length ({_packetLength}) exceeding maximum 32kB packet size.");
            }

            if (length > bytesToRead)
            {
                await HandlePacketBytesAsync(receivedBytes, offset + bytesToRead, length - bytesToRead, cancellationToken);
            }
        }
    }

    private async Task HandlePacketBytesAsync(byte[] receivedBytes, int offset, int length, CancellationToken cancellationToken)
    {
        int bytesToRead = Math.Min(_packetLength, length);
        Buffer.BlockCopy(receivedBytes, offset, _buffer, _bytesRead, bytesToRead);
        _bytesRead += bytesToRead;

        Logger.i(nameof(FCastSession), $"handlePacketBytes: Read {bytesToRead} bytes from packet");

        if (_bytesRead >= _packetLength)
        {
            Logger.i(nameof(FCastSession), $"Packet finished receiving of {_packetLength} bytes.");
            await HandleNextPacketAsync(cancellationToken);

            _state = SessionState.WaitingForLength;
            _packetLength = 0;
            _bytesRead = 0;

            if (length > bytesToRead)
            {
                await HandleLengthBytesAsync(receivedBytes, offset + bytesToRead, length - bytesToRead, cancellationToken);
            }
        }
    }

    private async Task HandleNextPacketAsync(CancellationToken cancellationToken)
    {
        Logger.i(nameof(FCastSession), $"Processing packet of {_bytesRead} bytes");

        Opcode opcode = (Opcode)_buffer[0];
        int packetLength = _packetLength;
        string? body = packetLength > 1 ? Encoding.UTF8.GetString(_buffer, 1, packetLength - 1) : null;

        Logger.i(nameof(FCastSession), $"Received body: {body}");
        await HandlePacketAsync(opcode, body, cancellationToken);
    }

    private async Task HandlePacketAsync(Opcode opcode, string? body, CancellationToken cancellationToken)
    {
        Logger.i(nameof(FCastSession), $"Received message with opcode {opcode}.");

        switch (opcode)
        {
            case Opcode.PlaybackUpdate:
                OnPlaybackUpdate?.Invoke(JsonSerializer.Deserialize<PlaybackUpdateMessage>(body!)!);
                break;
            case Opcode.VolumeUpdate:
                OnVolumeUpdate?.Invoke(JsonSerializer.Deserialize<VolumeUpdateMessage>(body!)!);
                break;
            case Opcode.PlaybackError:
                OnPlaybackError?.Invoke(JsonSerializer.Deserialize<PlaybackErrorMessage>(body!)!);
                break;
            case Opcode.Version:
                OnVersion?.Invoke(JsonSerializer.Deserialize<VersionMessage>(body!)!);
                break;
            case Opcode.Ping:
                Logger.i(nameof(FCastSession), "Received ping");
                await SendMessageAsync(Opcode.Pong, cancellationToken);
                Logger.i(nameof(FCastSession), "Sent pong");
                break;
            case Opcode.Pong:
                Logger.i(nameof(FCastSession), "Received pong");
                OnPong?.Invoke();
                break;
            default:
                Logger.i(nameof(FCastSession), "Error handling packet");
                break;
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
    }
}

public class PlayMessage
{
    [JsonPropertyName("container")]
    public required string Container { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("time")]
    public double? Time { get; set; }

    [JsonPropertyName("speed")]
    public double? Speed { get; set; }

    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }
}

public class SeekMessage
{
    [JsonPropertyName("time")]
    public required double Time { get; set; }
}

public class PlaybackUpdateMessage
{
    [JsonPropertyName("time")]
    public required double Time { get; set; }

    [JsonPropertyName("duration")]
    public required double Duration { get; set; }

    [JsonPropertyName("speed")]
    public required double Speed { get; set; }

    [JsonPropertyName("state")]
    public required int State { get; set; } // 0 = None, 1 = Playing, 2 = Paused
}

public class VolumeUpdateMessage
{
    [JsonPropertyName("volume")]
    public required double Volume { get; set; } // (0-1)
}

public class SetVolumeMessage
{
    [JsonPropertyName("volume")]
    public required double Volume { get; set; }
}

public class SetSpeedMessage
{
    [JsonPropertyName("speed")]
    public required double Speed { get; set; }
}

public class PlaybackErrorMessage
{
    [JsonPropertyName("message")]
    public required string Message { get; set; }
}

public class VersionMessage
{
    [JsonPropertyName("version")]
    public required ulong Version { get; set; }
}
