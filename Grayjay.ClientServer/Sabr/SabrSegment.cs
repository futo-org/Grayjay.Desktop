using Grayjay.Engine.Models.Video.Sources;

namespace Grayjay.ClientServer.Sabr
{
    public class SabrSegment
    {
        private const int INITIAL_CAPACITY = 32 * 1024;

        public UMPFormatKey FormatKey { get; }
        public int SequenceNumber { get; }
        public bool IsInit { get; }
        public long StartUs { get; }
        public int ContentLength { get; }
        public long StartTicks { get; }
        public int Timescale { get; }

        private long _durationUs;
        public long DurationUs => Volatile.Read(ref _durationUs);

        private volatile bool _durationExact;
        public bool DurationExact => _durationExact;

        private readonly object _lock = new object();
        private byte[] _data;
        private volatile int _size;
        public int Size => _size;

        private volatile bool _isComplete;
        public bool IsComplete => _isComplete;

        public long EndUs => StartUs + DurationUs;

        public SabrSegment(UMPFormatKey formatKey, int sequenceNumber, bool isInit, long startUs, long durationUs, int contentLength, long startTicks = 0, int timescale = 0)
        {
            FormatKey = formatKey;
            SequenceNumber = sequenceNumber;
            IsInit = isInit;
            StartUs = startUs;
            _durationUs = durationUs;
            ContentLength = contentLength;
            StartTicks = startTicks;
            Timescale = timescale;
            _data = new byte[contentLength > 0 ? contentLength : INITIAL_CAPACITY];
        }

        public void SetDuration(long us, bool exact)
        {
            if (us <= 0) return;
            if (_durationExact && !exact) return;
            Volatile.Write(ref _durationUs, us);
            _durationExact = exact;
        }

        public void Append(byte[] bytes, int offset, int length)
        {
            if (length <= 0) return;
            lock (_lock)
            {
                var required = _size + length;
                if (required > _data.Length)
                {
                    var capacity = Math.Max(_data.Length, INITIAL_CAPACITY);
                    while (capacity < required) capacity *= 2;
                    Array.Resize(ref _data, capacity);
                }
                Buffer.BlockCopy(bytes, offset, _data, _size, length);
                _size += length;
            }
        }

        public int Read(int position, byte[] dest, int destOffset, int length)
        {
            lock (_lock)
            {
                var available = _size - position;
                if (available <= 0) return 0;
                var toCopy = Math.Min(available, length);
                Buffer.BlockCopy(_data, position, dest, destOffset, toCopy);
                return toCopy;
            }
        }

        public byte[] ToByteArray()
        {
            lock (_lock)
            {
                var result = new byte[_size];
                Buffer.BlockCopy(_data, 0, result, 0, _size);
                return result;
            }
        }

        public void MarkComplete()
        {
            _isComplete = true;
        }
    }
}
