using System.Text;

namespace Grayjay.ClientServer.Sabr.Cast
{
    public class CastTimeline
    {
        private const long MICROS_PER_SECOND = 1_000_000L;
        private const int MAX_SEGMENTS = 100_000;

        private record struct Entry(long StartTicks, long DurationTicks);

        private readonly object _lock = new object();
        private readonly SortedDictionary<int, Entry> _entries = new();

        public int Timescale { get; }
        public long PresentationOffsetTicks { get; }

        public CastTimeline(int timescale, long presentationOffsetTicks = 0)
        {
            Timescale = timescale;
            PresentationOffsetTicks = presentationOffsetTicks;
        }

        public int FirstNumber { get { lock (_lock) return _entries.Count == 0 ? -1 : _entries.Keys.First(); } }
        public int LastNumber { get { lock (_lock) return _entries.Count == 0 ? -1 : _entries.Keys.Last(); } }
        public bool IsEmpty { get { lock (_lock) return _entries.Count == 0; } }

        public void Put(int sequence, long startTicks, long durationTicks)
        {
            lock (_lock) _entries[sequence] = new Entry(startTicks, Math.Max(durationTicks, 1));
        }

        public void DropBefore(int sequence)
        {
            lock (_lock)
                foreach (var key in _entries.Keys.Where(x => x < sequence).ToList())
                    _entries.Remove(key);
        }

        public CastTimeline TruncateTo(int lastSequence)
        {
            if (lastSequence < 0 || lastSequence == int.MaxValue) return this;
            lock (_lock)
                foreach (var key in _entries.Keys.Where(x => x > lastSequence).ToList())
                    _entries.Remove(key);
            return this;
        }

        public long? StartUs(int sequence)
        {
            lock (_lock) return _entries.TryGetValue(sequence, out var e) ? TicksToUs(e.StartTicks) : null;
        }

        public long? DurationUs(int sequence)
        {
            lock (_lock) return _entries.TryGetValue(sequence, out var e) ? TicksToUs(e.DurationTicks) : null;
        }

        public long? MidUs(int sequence)
        {
            lock (_lock) return _entries.TryGetValue(sequence, out var e) ? TicksToUs(e.StartTicks + e.DurationTicks / 2) : null;
        }

        public long TotalUs()
        {
            lock (_lock)
            {
                if (_entries.Count == 0) return 0;
                var last = _entries.Values.Last();
                return TicksToUs(last.StartTicks + last.DurationTicks - PresentationOffsetTicks);
            }
        }

        public string? SegmentTimelineXml(int from, int to)
        {
            lock (_lock)
            {
                if (from > to) return null;
                var window = _entries.Where(x => x.Key >= from && x.Key <= to).ToList();
                if (window.Count == 0) return null;
                if (window.Count != to - from + 1) return null;

                var sb = new StringBuilder("<SegmentTimeline>\n");
                long pendingStart = -1;
                long pendingDuration = -1;
                var pendingRepeat = 0;
                var pendingEmitT = true;
                long previousEnd = -1;

                foreach (var (_, entry) in window)
                {
                    var contiguous = previousEnd == entry.StartTicks;
                    if (pendingDuration == entry.DurationTicks && contiguous)
                        pendingRepeat++;
                    else
                    {
                        Emit(sb, pendingStart, pendingDuration, pendingRepeat, pendingEmitT);
                        pendingStart = entry.StartTicks;
                        pendingDuration = entry.DurationTicks;
                        pendingRepeat = 0;
                        pendingEmitT = !contiguous;
                    }
                    previousEnd = entry.StartTicks + entry.DurationTicks;
                }
                Emit(sb, pendingStart, pendingDuration, pendingRepeat, pendingEmitT);

                sb.Append("</SegmentTimeline>\n");
                return sb.ToString();
            }
        }

        private static void Emit(StringBuilder sb, long start, long duration, int repeat, bool emitT)
        {
            if (duration < 0) return;
            var t = emitT ? $" t=\"{start}\"" : "";
            if (repeat > 0) sb.Append($"<S{t} d=\"{duration}\" r=\"{repeat}\"/>\n");
            else sb.Append($"<S{t} d=\"{duration}\"/>\n");
        }

        private long TicksToUs(long ticks) =>
            ticks / Timescale * MICROS_PER_SECOND + (ticks % Timescale) * MICROS_PER_SECOND / Timescale;

        public static CastTimeline FromSidx(int startNumber, SidxTiming timing)
        {
            var timeline = new CastTimeline(timing.Timescale, timing.BaseTicks);
            var start = timing.BaseTicks;
            for (var i = 0; i < timing.Durations.Length; i++)
            {
                timeline.Put(startNumber + i, start, timing.Durations[i]);
                start += timing.Durations[i];
            }
            return timeline;
        }

        public static CastTimeline? Uniform(int startNumber, int endNumber, long segmentMs, long totalMs)
        {
            if (endNumber < startNumber || segmentMs <= 0) return null;
            var count = endNumber - startNumber + 1;
            if (count > MAX_SEGMENTS) return null;
            var timeline = new CastTimeline(1000);
            long start = 0;
            for (var i = 0; i < count; i++)
            {
                var duration = (i == count - 1 && totalMs > start) ? (totalMs - start) : segmentMs;
                timeline.Put(startNumber + i, start, Math.Max(duration, 1));
                start += duration;
            }
            return timeline;
        }
    }
}
