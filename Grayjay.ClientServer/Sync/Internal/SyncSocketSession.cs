using System.Buffers.Binary;
using Grayjay.Desktop.POC;
using Noise;

namespace Grayjay.ClientServer.Sync.Internal;


public class SyncSocketSession : IDisposable
{
    private static readonly Protocol _protocol = new Protocol(
        HandshakePattern.IK,
        CipherFunction.ChaChaPoly,
        HashFunction.Blake2b
    );

    public enum Opcode : byte
    {
        PING = 0,
        PONG = 1,
        NOTIFY_AUTHORIZED = 2,
        NOTIFY_UNAUTHORIZED = 3,
        STREAM_START = 4,
        STREAM_DATA = 5,
        STREAM_END = 6,
        DATA = 7
    }


    private readonly Stream _inputStream;
    private readonly Stream _outputStream;
    private readonly SemaphoreSlim _sendSemaphore = new SemaphoreSlim(1);
    private readonly byte[] _buffer = new byte[MAXIMUM_PACKET_SIZE_ENCRYPTED];
    private readonly byte[] _bufferDecrypted = new byte[MAXIMUM_PACKET_SIZE];
    private readonly byte[] _sendBuffer = new byte[MAXIMUM_PACKET_SIZE];
    private readonly byte[] _sendBufferEncrypted = new byte[MAXIMUM_PACKET_SIZE_ENCRYPTED];
    private readonly Dictionary<int, SyncStream> _syncStreams = new();
    private int _streamIdGenerator = 0;
    private readonly Action<SyncSocketSession> _onClose;
    private readonly Action<SyncSocketSession> _onHandshakeComplete;
    private Thread? _thread = null;
    private Transport? _transport = null;
    public string? RemotePublicKey { get; private set; } = null;
    private bool _started;
    private KeyPair _localKeyPair;
    private readonly string _localPublicKey;
    public string LocalPublicKey => _localPublicKey;
    private readonly Action<SyncSocketSession, Opcode, byte, byte[]> _onData;
    public string RemoteAddress { get; }
    public IAuthorizable? Authorizable { get; set; }

    public SyncSocketSession(string remoteAddress, KeyPair localKeyPair, Stream inputStream, Stream outputStream,
        Action<SyncSocketSession> onClose, Action<SyncSocketSession> onHandshakeComplete,
        Action<SyncSocketSession, Opcode, byte, byte[]> onData)
    {
        _inputStream = inputStream;
        _outputStream = outputStream;
        _onClose = onClose;
        _onHandshakeComplete = onHandshakeComplete;
        _localKeyPair = localKeyPair;
        _onData = onData;
        _localPublicKey = Convert.ToBase64String(localKeyPair.PublicKey);
        RemoteAddress = remoteAddress;
    }

    public void StartAsInitiator(string remotePublicKey)
    {
        _started = true;
        _thread = new Thread(() =>
        {
            try
            {
                HandshakeAsInitiator(remotePublicKey);
                _onHandshakeComplete(this);
                ReceiveLoop();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to run as initiator: {e}");
            }
            finally
            {
                Stop();
            }
        });
        _thread.Start();
    }

    public void StartAsResponder()
    {
        _started = true;
        _thread = new Thread(() =>
        {
            try
            {
                HandshakeAsResponder();
                _onHandshakeComplete(this);
                ReceiveLoop();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to run as responder: {e}");
            }
            finally
            {
                Stop();
            }
        });
        _thread.Start();
    }

    private void ReceiveLoop()
    {
        while (_started)
        {
            try
            {
                byte[] messageSizeBytes = new byte[4];
                _inputStream.Read(messageSizeBytes, 0, 4);
                int messageSize = BitConverter.ToInt32(messageSizeBytes, 0);

                //Console.WriteLine($"Read message size {messageSize}");

                if (messageSize > MAXIMUM_PACKET_SIZE_ENCRYPTED)
                    throw new Exception($"Message size ({messageSize}) exceeds maximum allowed size ({MAXIMUM_PACKET_SIZE_ENCRYPTED})");

                int bytesRead = 0;
                while (bytesRead < messageSize)
                {
                    int read = _inputStream.Read(_buffer, bytesRead, messageSize - bytesRead);
                    if (read == -1)
                        throw new Exception("Stream closed");
                    bytesRead += read;
                }

                //Console.WriteLine($"Read message bytes {bytesRead}");

                int plen = _transport!.ReadMessage(_buffer.AsSpan().Slice(0, messageSize), _bufferDecrypted);
                //Console.WriteLine($"Decrypted message bytes {plen}");

                HandleData(_bufferDecrypted, plen);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception while receiving data: {e}");
                break;
            }
        }
    }

    public void Stop()
    {
        _started = false;
        _onClose(this);
        _inputStream.Close();
        _outputStream.Close();
        _transport?.Dispose();
        _thread = null;
        Console.WriteLine("Session closed");
    }

    private void HandshakeAsInitiator(string remotePublicKey)
    {
        PerformVersionCheck();

        var message = new byte[Protocol.MaxMessageLength];
        var plaintext = new byte[Protocol.MaxMessageLength];
        using (var handshakeState = _protocol.Create(true, s: _localKeyPair.PrivateKey, rs: Convert.FromBase64String(remotePublicKey)))
        {
            var (bytesWritten, _, _) = handshakeState.WriteMessage(null, message);
            _outputStream.Write(BitConverter.GetBytes(bytesWritten));
            _outputStream.Write(message, 0, bytesWritten);
            Console.WriteLine($"HandshakeAsInitiator: Wrote message size {bytesWritten}");

            var bytesRead = _inputStream.Read(message, 0, 4);
            if (bytesRead != 4)
                throw new Exception("Expected exactly 4 bytes (message size)");

            var messageSize = BitConverter.ToInt32(message);
            Console.WriteLine($"HandshakeAsInitiator: Read message size {messageSize}");
            bytesRead = 0;
            while (bytesRead < messageSize)
            {
                var read = _inputStream.Read(message, bytesRead, messageSize - bytesRead);
                if (read == 0)
                    throw new Exception("Stream closed.");
                bytesRead += read;
            }

            var (_, _, transport) = handshakeState.ReadMessage(message.AsSpan().Slice(0, messageSize), plaintext);
            _transport = transport;

            RemotePublicKey = Convert.ToBase64String(handshakeState.RemoteStaticPublicKey);
        }
    }

    private void HandshakeAsResponder()
    {
        PerformVersionCheck();

        var message = new byte[Protocol.MaxMessageLength];
        var plaintext = new byte[Protocol.MaxMessageLength];
        using (var handshakeState = _protocol.Create(false, s: _localKeyPair.PrivateKey))
        {
            var bytesRead = _inputStream.Read(message, 0, 4);
            if (bytesRead != 4)
                throw new Exception($"Expected exactly 4 bytes (message size), read {bytesRead}");

            var messageSize = BitConverter.ToInt32(message);
            Console.WriteLine($"HandshakeAsResponder: Read message size {messageSize}");

            bytesRead = 0;
            while (bytesRead < messageSize)
            {
                var read = _inputStream.Read(message, bytesRead, messageSize - bytesRead);
                if (read == 0)
                    throw new Exception("Stream closed.");
                bytesRead += read;
            }

            var (_, _, _) = handshakeState.ReadMessage(message.AsSpan().Slice(0, messageSize), plaintext);

            var (bytesWritten, _, transport) = handshakeState.WriteMessage(null, message);
            _outputStream.Write(BitConverter.GetBytes(bytesWritten));
            _outputStream.Write(message, 0, bytesWritten);
            Console.WriteLine($"HandshakeAsResponder: Wrote message size {bytesWritten}");

            _transport = transport;

            RemotePublicKey = Convert.ToBase64String(handshakeState.RemoteStaticPublicKey);
        }
    }

    private void PerformVersionCheck()
    {
        const int CURRENT_VERSION = 2;
        _outputStream.Write(BitConverter.GetBytes(CURRENT_VERSION), 0, 4);
        byte[] versionBytes = new byte[4];
        int bytesRead = _inputStream.Read(versionBytes, 0, 4);
        if (bytesRead != 4)
            throw new Exception($"Expected 4 bytes to be read, read {bytesRead}");
        int version = BitConverter.ToInt32(versionBytes, 0);
        Logger.i(nameof(SyncSocketSession), $"PerformVersionCheck {version}");
        if (version != CURRENT_VERSION)
            throw new Exception("Invalid version");
    }

    public async Task SendAsync(Opcode opcode, byte subOpcode, byte[] data, int offset = 0, int size = -1, CancellationToken cancellationToken = default) =>
        await SendAsync((byte)opcode, subOpcode, data, offset, size, cancellationToken);
    public async Task SendAsync(byte opcode, byte subOpcode, byte[] data, int offset = 0, int size = -1, CancellationToken cancellationToken = default)
    {
        if (size == -1)
            size = data.Length;

        if (size + HEADER_SIZE > MAXIMUM_PACKET_SIZE)
        {
            var segmentSize = MAXIMUM_PACKET_SIZE - HEADER_SIZE;
            var segmentData = new byte[segmentSize];
            var id = Interlocked.Increment(ref _streamIdGenerator);

            for (var sendOffset = 0; sendOffset < size;)
            {
                var bytesRemaining = size - sendOffset;
                int bytesToSend;
                int segmentPacketSize;

                Opcode op;
                if (sendOffset == 0)
                {
                    op = Opcode.STREAM_START;
                    bytesToSend = segmentSize - 4 - 4 - 1 - 1;
                    segmentPacketSize = bytesToSend + 4 + 4 + 1 + 1;
                }
                else
                {
                    bytesToSend = Math.Min(segmentSize - 4 - 4, bytesRemaining);
                    if (bytesToSend >= bytesRemaining)
                        op = Opcode.STREAM_END;
                    else
                        op = Opcode.STREAM_DATA;

                    segmentPacketSize = bytesToSend + 4 + 4;
                }

                if (op == Opcode.STREAM_START)
                {
                    //TODO: replace segmentData.AsSpan() into a local variable once C# 13
                    BinaryPrimitives.WriteInt32LittleEndian(segmentData.AsSpan().Slice(0, 4), id);
                    BinaryPrimitives.WriteInt32LittleEndian(segmentData.AsSpan().Slice(4, 4), size);
                    segmentData[8] = (byte)opcode;
                    segmentData[9] = (byte)subOpcode;
                    data.AsSpan(offset, size).Slice(sendOffset, bytesToSend).CopyTo(segmentData.AsSpan().Slice(10));
                }
                else
                {
                    //TODO: replace segmentData.AsSpan() into a local variable once C# 13
                    BinaryPrimitives.WriteInt32LittleEndian(segmentData.AsSpan().Slice(0, 4), id);
                    BinaryPrimitives.WriteInt32LittleEndian(segmentData.AsSpan().Slice(4, 4), sendOffset);
                    data.AsSpan(offset, size).Slice(sendOffset, bytesToSend).CopyTo(segmentData.AsSpan().Slice(8));
                }

                sendOffset += bytesToSend;
                await SendAsync((byte)op, 0, segmentData.AsSpan().Slice(0, segmentPacketSize).ToArray(), cancellationToken: cancellationToken);
            }
        }
        else
        {
            try
            {
                await _sendSemaphore.WaitAsync();

                Array.Copy(BitConverter.GetBytes(data.Length + 2), 0, _sendBuffer, 0, 4);
                _sendBuffer[4] = (byte)opcode;
                _sendBuffer[5] = (byte)subOpcode;
                data.CopyTo(_sendBuffer.AsSpan().Slice(HEADER_SIZE));

                //Console.WriteLine($"Encrypted message bytes {data.Length + HEADER_SIZE}");

                var len = _transport!.WriteMessage(_sendBuffer.AsSpan().Slice(0, data.Length + HEADER_SIZE), _sendBufferEncrypted);

                _outputStream.Write(BitConverter.GetBytes(len), 0, 4);
                //Console.WriteLine($"Wrote message size {len}");
                _outputStream.Write(_sendBufferEncrypted, 0, len);
                //Console.WriteLine($"Wrote message bytes {len}");
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }
    }

    public async Task SendAsync(Opcode opcode, byte subOpcode = 0, CancellationToken cancellationToken = default)
    {
        try
        {
            await _sendSemaphore.WaitAsync(cancellationToken);

            Array.Copy(BitConverter.GetBytes(2), 0, _sendBuffer, 0, 4);
            _sendBuffer[4] = (byte)opcode;
            _sendBuffer[5] = (byte)subOpcode;

            //Console.WriteLine($"Encrypted message bytes {HEADER_SIZE}");

            var len = _transport!.WriteMessage(_sendBuffer.AsSpan().Slice(0, HEADER_SIZE), _sendBufferEncrypted);
            await _outputStream.WriteAsync(BitConverter.GetBytes(len), 0, 4, cancellationToken);
            //Console.WriteLine($"Wrote message size {len}");
            await _outputStream.WriteAsync(_sendBufferEncrypted, 0, len, cancellationToken);
            //Console.WriteLine($"Wrote message bytes {len}");
        }
        finally
        {
            _sendSemaphore.Release();
        }
    }

    private void HandleData(byte[] data, int length)
    {
        if (length < HEADER_SIZE)
            throw new Exception("Packet must be at least 6 bytes (header size)");

        int size = BitConverter.ToInt32(data, 0);
        if (size != length - 4)
            throw new Exception("Incomplete packet received");

        byte opcode = data[4];
        byte subOpcode = data[5];
        byte[] packetData = new byte[size - 2];
        Array.Copy(data, HEADER_SIZE, packetData, 0, size - 2);

        HandlePacket((Opcode)opcode, subOpcode, packetData);
    }

    private void HandlePacket(Opcode opcode, byte subOpcode, byte[] data)
    {
        switch (opcode)
        {
            case Opcode.PING:
                Task.Run(async () => 
                {
                    try
                    {
                        await SendAsync(Opcode.PONG);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Failed to send pong: " + e.ToString());
                    }
                });
                //Console.WriteLine("Received PING, sent PONG");
                return;
            case Opcode.PONG:
                //Console.WriteLine("Received PONG");
                return;
            case Opcode.NOTIFY_AUTHORIZED:
            case Opcode.NOTIFY_UNAUTHORIZED:
                _onData(this, opcode, subOpcode, data);
                return;
        }

        if (Authorizable?.IsAuthorized != true)
            return;

        switch (opcode)
        {
            case Opcode.STREAM_START:
                {
                    using var stream = new MemoryStream(data);
                    using var reader = new BinaryReader(stream);
                    var id = reader.ReadInt32();
                    var expectedSize = reader.ReadInt32();
                    var op = (Opcode)reader.ReadByte();
                    var subOp = reader.ReadByte();
                    var syncStream = new SyncStream(expectedSize, op, subOp);
                    if (stream.Position < stream.Length)
                        syncStream.Add(data.AsSpan().Slice((int)stream.Position));

                    lock (_syncStreams)
                    {
                        _syncStreams[id] = syncStream;
                    }
                    break;
                }
            case Opcode.STREAM_DATA:
                {
                    using var stream = new MemoryStream(data);
                    using var reader = new BinaryReader(stream);
                    var id = reader.ReadInt32();
                    var expectedOffset = reader.ReadInt32();

                    SyncStream? syncStream;
                    lock (_syncStreams)
                    {
                        if (!_syncStreams.TryGetValue(id, out syncStream) || syncStream == null)
                            throw new Exception("Received data for sync stream that does not exist");
                    }

                    if (expectedOffset != syncStream.BytesReceived)
                        throw new Exception("Expected offset not matching with the amount of received bytes");

                    if (stream.Position < stream.Length)
                        syncStream.Add(data.AsSpan().Slice((int)stream.Position));

                    break;
                }
            case Opcode.STREAM_END:
                {
                    using var stream = new MemoryStream(data);
                    using var reader = new BinaryReader(stream);
                    var id = reader.ReadInt32();
                    var expectedOffset = reader.ReadInt32();

                    SyncStream? syncStream;
                    lock (_syncStreams)
                    {
                        if (!_syncStreams.Remove(id, out syncStream) || syncStream == null)
                            throw new Exception("Received data for sync stream that does not exist");
                    }

                    if (expectedOffset != syncStream.BytesReceived)
                        throw new Exception("Expected offset not matching with the amount of received bytes");

                    if (stream.Position < stream.Length)
                        syncStream.Add(data.AsSpan().Slice((int)stream.Position));

                    if (!syncStream.IsComplete)
                        throw new Exception("After sync stream end, the stream must be complete");

                    HandlePacket(syncStream.Opcode, syncStream.SubOpcode, syncStream.GetBytes());
                    break;
                }
            case Opcode.DATA:
                {
                    _onData(this, opcode, subOpcode, data);
                    break;
                }
            default:
                Logger.w<SyncSocketSession>($"Unknown opcode received (opcode = {opcode}, subOpcode = {subOpcode})");
                break;
        }
    }

    public void Dispose()
    {
        lock (_syncStreams)
        {
            _syncStreams.Clear();
        }

        Stop();
    }

    public const int MAXIMUM_PACKET_SIZE = 65535 - 16;
    public const int MAXIMUM_PACKET_SIZE_ENCRYPTED = MAXIMUM_PACKET_SIZE + 16;
    public const int HEADER_SIZE = 6;
}