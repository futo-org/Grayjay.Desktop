namespace Grayjay.ClientServer.Sabr.Cast
{
    public class LiveCastPlanner
    {
        private const long GRACE_SLACK_MS = 5_000L;

        public class Config
        {
            public int WindowSegments { get; init; } = 8;
            public int MaxLagSegments { get; init; } = 40;
            public int MinStartSegments { get; init; } = 3;
            public int PlaybackLagSegments { get; init; } = 4;
        }

        public record struct Track(int Low, int Head);
        public record struct Range(int First, int Last);

        private readonly Config _config;
        private readonly Func<long> _clock;
        private readonly object _lock = new object();

        private int _edge = -1;
        private int _publishedStart = -1;
        private readonly LinkedList<(long At, int Start)> _commits = new();
        private Range? _emitted;
        private int _floor = -1;
        private readonly Dictionary<int, int> _position = new();

        private long _segmentMs = 2000;
        public long SegmentMs
        {
            get => _segmentMs;
            set => _segmentMs = Math.Clamp(value, 500, 15_000);
        }

        public LiveCastPlanner(Config? config = null, Func<long>? clock = null)
        {
            _config = config ?? new Config();
            _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (_config.MinStartSegments < 2 || _config.MinStartSegments > _config.WindowSegments)
                throw new ArgumentException("MinStartSegments must be between 2 and WindowSegments");
            if (_config.PlaybackLagSegments < 0 || _config.PlaybackLagSegments >= _config.WindowSegments)
                throw new ArgumentException("PlaybackLagSegments must be under WindowSegments");
            if (_config.MaxLagSegments <= _config.WindowSegments)
                throw new ArgumentException("MaxLagSegments must exceed the window");
        }

        public Range? Window { get { lock (_lock) return _emitted; } }
        public int LiveEdge { get { lock (_lock) return _edge; } }

        public void OnLiveEdge(int headSequence)
        {
            lock (_lock) if (headSequence > _edge) _edge = headSequence;
        }

        public void NoteRequest(int role, int sequence)
        {
            lock (_lock) _position[role] = sequence;
        }

        private int TrailingPosition()
        {
            if (_position.Count == 0) return -1;
            var clamped = _position.Values.Min();
            if (_floor >= 0) clamped = Math.Max(clamped, _floor);
            if (_edge >= 0) clamped = Math.Max(clamped, _edge - _config.MaxLagSegments);
            return clamped;
        }

        public int RetainFrom()
        {
            lock (_lock) return RetainFromLocked();
        }

        private int RetainFromLocked()
        {
            var promised = PromisedStartLocked();
            if (promised >= 0)
            {
                var trail = TrailingPosition();
                _floor = Math.Max(_floor, trail >= 0 ? Math.Min(promised, trail) : promised);
            }
            return _floor;
        }

        private int PromisedStartLocked()
        {
            var cutoff = _clock() - (2 * SegmentMs + GRACE_SLACK_MS);
            var promised = -1;
            foreach (var (at, start) in _commits)
            {
                if (at > cutoff) break;
                promised = start;
            }
            if (promised >= 0) return promised;
            return _commits.Count > 0 ? _commits.First!.Value.Start : _publishedStart;
        }

        public Range? PlanWindow(ICollection<Track> tracks)
        {
            lock (_lock)
            {
                if (tracks.Count == 0) return null;

                var low = tracks.Max(x => x.Low);
                var head = tracks.Min(x => x.Head);
                if (low < 0 || head < low) return null;

                var previous = _emitted;
                if (previous == null && head - low + 1 < _config.MinStartSegments) return null;

                var start = Math.Max(low, head - _config.WindowSegments + 1);

                var trail = TrailingPosition();
                if (trail >= 0) start = Math.Min(start, trail - _config.PlaybackLagSegments);

                if (previous != null && low <= previous.Value.Last)
                {
                    start = Math.Min(start, previous.Value.Last);
                    start = Math.Max(start, previous.Value.First);
                }

                start = Math.Max(start, low);
                if (start > head) return null;
                return new Range(start, head);
            }
        }

        public bool Commit(Range range)
        {
            lock (_lock)
            {
                var previous = _emitted;
                var expired = previous != null && range.First > previous.Value.Last;

                var now = _clock();
                _emitted = range;
                _publishedStart = Math.Max(_publishedStart, range.First);

                _commits.AddLast((now, range.First));
                while (_commits.Count > 1 && _commits.First!.Value.At < now - (2 * SegmentMs + GRACE_SLACK_MS) * 2)
                    _commits.RemoveFirst();

                RetainFromLocked();
                return expired;
            }
        }
    }
}
