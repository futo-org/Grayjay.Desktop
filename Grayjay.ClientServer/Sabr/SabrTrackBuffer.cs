using Grayjay.Engine.Models.Video.Sources;

namespace Grayjay.ClientServer.Sabr
{
    public class SabrTrackBuffer
    {
        private readonly object _lock = new object();
        private readonly SortedDictionary<int, SabrSegment> _segments = new SortedDictionary<int, SabrSegment>();
        private readonly AsyncSignal _changed = new AsyncSignal();

        public UMPFormatKey FormatKey { get; }

        private volatile SabrSegment? _initSegment;
        public SabrSegment? InitSegment => _initSegment;

        public SabrTrackBuffer(UMPFormatKey formatKey)
        {
            FormatKey = formatKey;
        }

        public int SegmentCount { get { lock (_lock) return _segments.Count; } }
        public int HighestSequence { get { lock (_lock) return _segments.Count == 0 ? -1 : _segments.Keys.Last(); } }
        public int LowestSequence { get { lock (_lock) return _segments.Count == 0 ? -1 : _segments.Keys.First(); } }

        public void Announce(SabrSegment segment)
        {
            lock (_lock)
            {
                if (segment.IsInit) _initSegment = segment;
                else _segments[segment.SequenceNumber] = segment;
            }
            _changed.Set();
        }

        public void NotifyChanged() => _changed.Set();

        public Task ChangedTask => _changed.WaitTask;

        public SabrSegment? Get(int sequenceNumber)
        {
            lock (_lock) return _segments.TryGetValue(sequenceNumber, out var s) ? s : null;
        }

        public List<SabrSegment> Snapshot()
        {
            lock (_lock) return _segments.Values.ToList();
        }

        public SabrSegment? FirstAtOrAfter(int minSequence)
        {
            lock (_lock)
            {
                if (minSequence < 0) return _segments.Count == 0 ? null : _segments.First().Value;
                foreach (var pair in _segments)
                    if (pair.Key >= minSequence) return pair.Value;
                return null;
            }
        }

        public SabrSegment? FirstCovering(long positionUs)
        {
            lock (_lock)
            {
                foreach (var segment in _segments.Values)
                    if (segment.EndUs > positionUs) return segment;
                return null;
            }
        }

        private async Task<T?> AwaitAsync<T>(Func<T?> probe, TimeSpan timeout, CancellationToken cancellationToken) where T : class
        {
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                var changed = _changed.WaitTask;
                var found = probe();
                if (found != null) return found;
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) return null;
                try
                {
                    await changed.WaitAsync(remaining, cancellationToken);
                }
                catch (TimeoutException)
                {
                    return probe();
                }
            }
        }

        public Task<SabrSegment?> AwaitAnnouncedAsync(int minSequence, TimeSpan timeout, CancellationToken cancellationToken = default)
            => AwaitAsync(() => FirstAtOrAfter(minSequence), timeout, cancellationToken);

        public Task<SabrSegment?> AwaitCoveringAsync(long positionUs, TimeSpan timeout, CancellationToken cancellationToken = default)
            => AwaitAsync(() => FirstCovering(positionUs), timeout, cancellationToken);

        public Task<SabrSegment?> AwaitSequenceAsync(int sequence, TimeSpan timeout, CancellationToken cancellationToken = default)
            => AwaitAsync(() => Get(sequence), timeout, cancellationToken);

        public Task<SabrSegment?> AwaitInitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
            => AwaitAsync(() => InitSegment is { IsComplete: true } init ? init : null, timeout, cancellationToken);

        public async Task<bool> AwaitCompleteAsync(SabrSegment segment, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var result = await AwaitAsync(() => segment.IsComplete || !IsTracked(segment) ? segment : null, timeout, cancellationToken);
            return result != null && segment.IsComplete;
        }

        private bool IsTracked(SabrSegment segment)
        {
            lock (_lock)
            {
                if (segment.IsInit) return ReferenceEquals(_initSegment, segment);
                return _segments.TryGetValue(segment.SequenceNumber, out var s) && ReferenceEquals(s, segment);
            }
        }

        public long BufferedEndFromFrontUs() => BufferedEndUs(long.MinValue);

        public long BufferedExactEndUs()
        {
            lock (_lock)
            {
                var end = long.MinValue;
                var expected = -1;
                foreach (var (sequence, segment) in _segments)
                {
                    if (expected != -1 && sequence != expected) break;
                    if (!segment.IsComplete || !segment.DurationExact) break;
                    end = segment.EndUs;
                    expected = sequence + 1;
                }
                return end;
            }
        }

        public (int First, int Last)? PublishableRun()
        {
            lock (_lock)
            {
                if (_segments.Count == 0) return null;
                var first = _segments.Keys.First();
                var end = -1;
                var sequence = first;
                while (_segments.TryGetValue(sequence, out var segment))
                {
                    if (!segment.IsComplete || !segment.DurationExact) break;
                    end = sequence;
                    sequence++;
                }
                return end < 0 ? null : (first, end);
            }
        }

        public long ExactEndFromSequence(int sequence)
        {
            lock (_lock)
            {
                var end = long.MinValue;
                var expected = sequence;
                foreach (var (seq, segment) in _segments)
                {
                    if (seq < sequence) continue;
                    if (seq != expected) break;
                    if (!segment.IsComplete || !segment.DurationExact) break;
                    end = segment.EndUs;
                    expected = seq + 1;
                }
                return end;
            }
        }

        public List<long> RecentStartDeltasUs(int max)
        {
            lock (_lock)
            {
                var deltas = new List<long>(max);
                SabrSegment? newer = null;
                foreach (var segment in _segments.Values.Reverse())
                {
                    var next = newer;
                    newer = segment;
                    if (next == null || next.SequenceNumber != segment.SequenceNumber + 1) continue;
                    var delta = next.StartUs - segment.StartUs;
                    if (delta > 0) deltas.Add(delta);
                    if (deltas.Count >= max) break;
                }
                return deltas;
            }
        }

        public long BufferedEndUs(long fromUs)
        {
            lock (_lock)
            {
                var end = long.MinValue;
                var expected = -1;
                foreach (var (sequence, segment) in _segments)
                {
                    if (expected == -1)
                    {
                        if (fromUs != long.MinValue && segment.EndUs < fromUs) continue;
                        if (fromUs != long.MinValue && segment.StartUs > fromUs) return long.MinValue;
                    }
                    if (expected != -1 && sequence != expected) break;
                    if (!segment.IsComplete) break;
                    end = segment.EndUs;
                    expected = sequence + 1;
                }
                return end;
            }
        }

        public int LastCompletedSequence(long fromUs)
        {
            lock (_lock)
            {
                var last = -1;
                var expected = -1;
                foreach (var (sequence, segment) in _segments)
                {
                    if (expected == -1)
                    {
                        if (fromUs != long.MinValue && segment.EndUs < fromUs) continue;
                        if (fromUs != long.MinValue && segment.StartUs > fromUs) return -1;
                    }
                    if (expected != -1 && sequence != expected) break;
                    if (!segment.IsComplete) break;
                    last = sequence;
                    expected = sequence + 1;
                }
                return last;
            }
        }

        public int LastCompletedFromFront() => LastCompletedSequence(long.MinValue);

        public void Discard(SabrSegment segment)
        {
            lock (_lock)
            {
                if (segment.IsComplete) return;
                if (segment.IsInit)
                {
                    if (ReferenceEquals(_initSegment, segment)) _initSegment = null;
                }
                else if (_segments.TryGetValue(segment.SequenceNumber, out var existing) && ReferenceEquals(existing, segment))
                    _segments.Remove(segment.SequenceNumber);
            }
            _changed.Set();
        }

        public void EvictBeforeSequence(int sequence)
        {
            var evicted = false;
            lock (_lock)
            {
                foreach (var (seq, segment) in _segments.ToList())
                {
                    if (seq >= sequence) break;
                    if (!segment.IsComplete) break;
                    _segments.Remove(seq);
                    evicted = true;
                }
            }
            if (evicted) _changed.Set();
        }

        public void EvictBefore(long positionUs)
        {
            var evicted = false;
            lock (_lock)
            {
                var expected = -1;
                foreach (var (sequence, segment) in _segments.ToList())
                {
                    if (expected != -1 && sequence != expected) break;
                    if (!segment.IsComplete) break;
                    if (segment.EndUs >= positionUs) break;
                    _segments.Remove(sequence);
                    expected = sequence + 1;
                    evicted = true;
                }
            }
            if (evicted) _changed.Set();
        }

        public void Clear()
        {
            lock (_lock) _segments.Clear();
            _changed.Set();
        }
    }
}
