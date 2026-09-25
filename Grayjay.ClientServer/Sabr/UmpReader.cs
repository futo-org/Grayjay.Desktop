namespace Grayjay.ClientServer.Sabr
{
    public static class UmpPartType
    {
        public const int ONESIE_HEADER = 10;
        public const int ONESIE_DATA = 11;
        public const int MEDIA_HEADER = 20;
        public const int MEDIA = 21;
        public const int MEDIA_END = 22;
        public const int LIVE_METADATA = 31;
        public const int HOSTNAME_CHANGE_HINT = 32;
        public const int NEXT_REQUEST_POLICY = 35;
        public const int FORMAT_INITIALIZATION_METADATA = 42;
        public const int SABR_REDIRECT = 43;
        public const int SABR_ERROR = 44;
        public const int SABR_SEEK = 45;
        public const int RELOAD_PLAYER_RESPONSE = 46;
        public const int PLAYBACK_START_POLICY = 47;
        public const int ALLOWED_CACHED_FORMATS = 48;
        public const int REQUEST_IDENTIFIER = 52;
        public const int REQUEST_CANCELLATION_POLICY = 53;
        public const int TIMELINE_CONTEXT = 55;
        public const int SABR_CONTEXT_UPDATE = 57;
        public const int STREAM_PROTECTION_STATUS = 58;
        public const int SABR_CONTEXT_SENDING_POLICY = 59;
        public const int SNACKBAR_MESSAGE = 67;
        public const int NETWORK_TIMING = 68;
        public const int CUEPOINT_LIST = 69;

        private static readonly Dictionary<int, string> NAMES = new Dictionary<int, string>()
        {
            { 10, "ONESIE_HEADER" }, { 11, "ONESIE_DATA" }, { 12, "ONESIE_ENCRYPTED_MEDIA" },
            { 20, "MEDIA_HEADER" }, { 21, "MEDIA" }, { 22, "MEDIA_END" },
            { 31, "LIVE_METADATA" }, { 32, "HOSTNAME_CHANGE_HINT" }, { 33, "LIVE_METADATA_PROMISE" },
            { 34, "LIVE_METADATA_PROMISE_CANCELLATION" }, { 35, "NEXT_REQUEST_POLICY" },
            { 36, "USTREAMER_VIDEO_AND_FORMAT_DATA" }, { 37, "FORMAT_SELECTION_CONFIG" },
            { 38, "USTREAMER_SELECTED_MEDIA_STREAM" }, { 42, "FORMAT_INIT" }, { 43, "SABR_REDIRECT" },
            { 44, "SABR_ERROR" }, { 45, "SABR_SEEK" }, { 46, "RELOAD_PLAYER" }, { 47, "PLAYBACK_START_POLICY" },
            { 48, "ALLOWED_CACHED_FORMATS" }, { 49, "START_BW_SAMPLING_HINT" }, { 50, "PAUSE_BW_SAMPLING_HINT" },
            { 51, "SELECTABLE_FORMATS" }, { 52, "REQUEST_IDENTIFIER" }, { 53, "REQUEST_CANCELLATION_POLICY" },
            { 54, "ONESIE_PREFETCH_REJECTION" }, { 55, "TIMELINE_CONTEXT" }, { 56, "REQUEST_PIPELINING" },
            { 57, "SABR_CONTEXT_UPDATE" }, { 58, "STREAM_PROTECTION_STATUS" }, { 59, "SABR_CONTEXT_SENDING_POLICY" },
            { 60, "LAWNMOWER_POLICY" }, { 61, "SABR_ACK" }, { 62, "END_OF_TRACK" }, { 63, "CACHE_LOAD_POLICY" },
            { 64, "LAWNMOWER_MESSAGING_POLICY" }, { 65, "PREWARM_CONNECTION" }, { 66, "PLAYBACK_DEBUG_INFO" },
            { 67, "SNACKBAR_MESSAGE" }, { 68, "NETWORK_TIMING" }, { 69, "CUEPOINT_LIST" },
            { 70, "STITCHED_REGIONS_OF_INTEREST" }, { 71, "STITCHED_SEGMENTS_METADATA_LIST" },
            { 72, "PROBE_SUCCESS" }
        };

        public static string Name(int type) => NAMES.TryGetValue(type, out var name) ? name : $"UNKNOWN({type})";
    }

    public class UmpPart
    {
        public int Type { get; }
        public byte[] Data { get; }

        public UmpPart(int type, byte[] data)
        {
            Type = type;
            Data = data;
        }
    }

    public class UmpReader
    {
        private const long MAX_PART_SIZE = 64L * 1024 * 1024;

        private readonly Stream _input;

        public UmpReader(Stream input)
        {
            _input = input;
        }

        public async Task<UmpPart?> NextAsync(CancellationToken cancellationToken = default)
        {
            var type = await ReadVarIntAsync(cancellationToken);
            if (type == null)
                return null;
            var length = await ReadVarIntAsync(cancellationToken) ?? throw new EndOfStreamException($"UMP part {type} truncated before length");
            if (length < 0 || length > MAX_PART_SIZE)
                throw new InvalidDataException($"UMP part {type} has implausible length {length}");

            var data = new byte[length];
            await _input.ReadExactlyAsync(data, cancellationToken);
            return new UmpPart((int)type.Value, data);
        }

        private readonly byte[] _single = new byte[1];

        private async Task<int> ReadByteAsync(CancellationToken cancellationToken)
        {
            var read = await _input.ReadAsync(_single.AsMemory(0, 1), cancellationToken);
            if (read <= 0)
                return -1;
            return _single[0];
        }

        private async Task<long?> ReadVarIntAsync(CancellationToken cancellationToken)
        {
            var first = await ReadByteAsync(cancellationToken);
            if (first < 0)
                return null;

            var size = SizeOf(first);
            if (size == 1)
                return first;

            long trailing = 0;
            for (var i = 0; i < size - 1; i++)
            {
                var b = await ReadByteAsync(cancellationToken);
                if (b < 0)
                    throw new EndOfStreamException("Unexpected end of UMP stream");
                trailing |= (long)b << (8 * i);
            }

            if (size == 5)
                return trailing;

            var valueBits = 8 - size;
            long head = first & ((1 << valueBits) - 1);
            return head | (trailing << valueBits);
        }

        public static int SizeOf(int firstByte)
        {
            if (firstByte < 128) return 1;
            if (firstByte < 192) return 2;
            if (firstByte < 224) return 3;
            if (firstByte < 240) return 4;
            return 5;
        }

        public static (long Value, int Offset) DecodeVarInt(byte[] bytes, int offset)
        {
            if (offset >= bytes.Length)
                return (-1, offset);
            var first = bytes[offset];
            var size = SizeOf(first);
            if (size == 1)
                return (first, offset + 1);
            if (offset + size > bytes.Length)
                return (-1, bytes.Length);

            long trailing = 0;
            for (var i = 1; i < size; i++)
                trailing |= (long)bytes[offset + i] << (8 * (i - 1));

            if (size == 5)
                return (trailing, offset + 5);
            var valueBits = 8 - size;
            long head = first & ((1 << valueBits) - 1);
            return (head | (trailing << valueBits), offset + size);
        }

        public static byte[] EncodeVarInt(long value)
        {
            if (value < 128)
                return new[] { (byte)value };
            if (value < (1L << 14))
                return new[] { (byte)(0x80 | (value & 0x3F)), (byte)(value >> 6) };
            if (value < (1L << 21))
                return new[] { (byte)(0xC0 | (value & 0x1F)), (byte)(value >> 5), (byte)(value >> 13) };
            if (value < (1L << 28))
                return new[] { (byte)(0xE0 | (value & 0x0F)), (byte)(value >> 4), (byte)(value >> 12), (byte)(value >> 20) };
            return new[] { (byte)0xF0, (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24) };
        }
    }
}
