using static Grayjay.ClientServer.Sync.Internal.SyncSocketSession;

namespace Grayjay.ClientServer.Sync.Internal;

public class SyncStream
{
    public const int MAXIMUM_SIZE = 10_000_000;

    public readonly Opcode Opcode;
    public readonly byte SubOpcode;
    private readonly byte[] _buffer;
    public int BytesReceived { get; private set; } = 0;
    private readonly int _expectedSize;

    public bool IsComplete { get; private set; } = false;

    public SyncStream(int expectedSize, Opcode opcode, byte subOpcode)
    {
        if (expectedSize > MAXIMUM_SIZE)
            throw new Exception($"{expectedSize} exceeded maximum size {MAXIMUM_SIZE}");

        Opcode = opcode;
        SubOpcode = subOpcode;
        _expectedSize = expectedSize;
        _buffer = new byte[expectedSize];
    }

    public void Add(ReadOnlySpan<byte> data)
    {
        var remainingBytes = _expectedSize - BytesReceived;
        if (data.Length > remainingBytes)
            throw new Exception($"More bytes received {data.Length} than expected remaining {remainingBytes}");

        data.CopyTo(_buffer!.AsSpan().Slice(BytesReceived));
        BytesReceived += data.Length;
        IsComplete = BytesReceived == _expectedSize;
    }

    public byte[] GetBytes()
    {
        return _buffer;
    }
}