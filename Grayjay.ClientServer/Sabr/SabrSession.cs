using System.Collections.Concurrent;
using System.Net;
using Google.Protobuf;
using Grayjay.ClientServer.Sabr.Proto;
using Grayjay.Desktop.POC;
using Grayjay.Engine.Models.Video.Additions;
using Grayjay.Engine.Models.Video.Sources;
using EngineHttpHeaders = Grayjay.Engine.Models.HttpHeaders;

namespace Grayjay.ClientServer.Sabr
{
    public interface ISabrSessionListener
    {
        void OnLiveMetadata(LiveMetadata metadata);
        void OnFormatInitialization(FormatInitializationMetadata metadata);
        void OnSessionError(Exception error);
        void OnBackoff(long delayMs) { }
        void OnBackoffEnded() { }
    }

    public class SabrSession : IDisposable
    {
        private class Demand
        {
            public UMPFormat Format { get; }
            public long FromUs { get; }
            public object? Owner { get; }
            public List<UMPFormat> Alternates { get; }

            public Demand(UMPFormat format, long fromUs, object? owner, List<UMPFormat>? alternates = null)
            {
                Format = format;
                FromUs = fromUs;
                Owner = owner;
                Alternates = alternates ?? new List<UMPFormat>() { format };
            }
        }

        public class Transferable
        {
            public int RequestNumber { get; init; }
            public ByteString? PlaybackCookie { get; init; }
            public Dictionary<int, SabrContext> SabrContexts { get; init; } = new();
            public HashSet<int> ActiveSabrContexts { get; init; } = new();
            public string StreamingUrl { get; init; } = "";
            public long BackoffUntilMs { get; init; }
            public long ServerBackoffUntilMs { get; init; }
            public long MediaBaseUs { get; init; }
            public bool MediaBaseSet { get; init; }
            public Dictionary<UMPFormatKey, FormatInitializationMetadata> FormatInitialization { get; init; } = new();
            public LiveMetadata? LiveMetadata { get; init; }
        }

        private const string TAG = "SabrSession";
        public const string TAG_LIVE = "SABRLIVE";
        public static void LiveLog(string msg) => Logger.i(TAG_LIVE, msg);
        public static void SabrLog(string msg) => Logger.i(TAG_LIVE, msg);

        public const int ROLE_VIDEO = 0;
        public const int ROLE_AUDIO = 1;

        private const long LIVE_POLL_MS = 1_000L;
        private const long DEFAULT_LIVE_SEGMENT_US = 5_000_000L;
        private const long MICROS_PER_SECOND = 1_000_000L;
        private const long DEFAULT_READAHEAD_MS = 20_000L;
        private const long PUMP_IDLE_POLL_MS = 250L;
        private const long ERROR_BACKOFF_BASE_MS = 1_000L;
        private const long LIVE_HEAD_PLAYER_TIME_US = 9_007_199_254_740_991L * 1000L;
        private const long SLOW_REQUEST_LOG_MS = 2_000L;
        private const long STARVED_US = 3_000_000L;
        private const long SABR_SEEK_SLACK_US = 30_000_000L;
        private const long ERROR_BACKOFF_MAX_MS = 30_000L;
        private const int ERROR_BACKOFF_MAX_SHIFT = 5;
        private const int MAX_CONSECUTIVE_ERRORS = 4;
        private const long DEFAULT_KEEP_BEHIND_US = 30_000_000L;
        private const int AD_CUEPOINT_MAGIC = 11;
        private const long RESTART_TOLERANCE_US = 1_000_000L;
        private const long FRONTIER_EPSILON_US = 1_000_000L;
        private const int NO_PROGRESS_THRESHOLD = 2;
        private const int MAX_EMPTY_RESPONSES = 8;
        private const int MAX_SUBSTITUTED_RESPONSES = 4;
        private const int MAX_REDIRECTS = 3;
        private const long EMPTY_BACKOFF_BASE_MS = 500L;
        private const long BANDWIDTH_ESTIMATE = 104857L;
        private const long THROUGHPUT_MIN_BYTES = 65_536L;
        private const long THROUGHPUT_SMOOTHING = 30L;
        private const long THROUGHPUT_MIN_SPEEDUP = 2;
        private const int PROTECTION_STATUS_ATTESTATION_REQUIRED = 3;

        private static int _liveSessions = 0;

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private static long TicksToUs(long ticks, int timescale) =>
            ticks / timescale * MICROS_PER_SECOND + (ticks % timescale) * MICROS_PER_SECOND / timescale;

        private static long UsToTicks(long us, int timescale) =>
            us / MICROS_PER_SECOND * timescale + (us % MICROS_PER_SECOND) * timescale / MICROS_PER_SECOND;

        private readonly HttpClient _httpClient;
        private readonly byte[] _ustreamerConfig;
        private readonly ClientInfo _clientInfo;
        private readonly string? _poTokenRaw;
        private readonly bool _ownsHttpClient;
        private readonly IRequestModifier? _requestModifier;
        private readonly Func<bool, string?>? _poTokenRefresher;

        public string VideoId { get; }
        public bool IsLive { get; }
        public long DurationUs { get; }

        private readonly ConcurrentDictionary<int, UMPFormatKey> _serverChosen = new();
        private readonly ConcurrentDictionary<string, long> _adCuepoints = new();
        private readonly object _pumpLock = new object();
        private readonly AsyncSignal _wake = new AsyncSignal();
        private readonly ConcurrentDictionary<UMPFormatKey, SabrTrackBuffer> _buffers = new();
        private readonly ConcurrentDictionary<int, SabrContext> _sabrContexts = new();
        private readonly ConcurrentDictionary<int, bool> _activeSabrContexts = new();
        private readonly ConcurrentDictionary<UMPFormatKey, FormatInitializationMetadata> _formatInitialization = new();
        private readonly ConcurrentDictionary<UMPFormatKey, bool> _formatComplete = new();
        private readonly ConcurrentDictionary<UMPFormatKey, int> _formatNoProgress = new();
        private readonly ConcurrentDictionary<UMPFormatKey, bool> _demandedKeys = new();

        private volatile int _emptyResponses = 0;
        private volatile int _demandedHeaders = 0;
        private volatile int _foreignHeaders = 0;
        private volatile int _substitutedResponses = 0;
        private volatile int _consecutiveRedirects = 0;

        private int _requestNumber = 0;
        private readonly long _createdAtMs = NowMs();

        private Task? _pumpTask;

        private int _released = 0;
        private bool Released => Volatile.Read(ref _released) != 0;
        public bool IsReleased => Released;

        private volatile string _streamingUrl;
        private ByteString? _poTokenDecoded;
        private volatile bool _poTokenResolved = false;
        private volatile bool _poTokenRefreshed = false;
        private ByteString? _playbackCookie;
        private long _backoffUntilMs = 0;
        private long _serverBackoffUntilMs = 0;
        private long _errorBackoffUntilMs = 0;
        private volatile int _consecutiveErrors = 0;
        private volatile bool _backoffNotified = false;
        private volatile bool _backoffShown = false;
        private long _lastWaitLogMs = 0;
        private long _lastRequestMs = 0;
        private long _lastActionMs = NowMs();
        private CancellationTokenSource? _requestCts;
        private volatile bool _aborting = false;
        private volatile int _restartEpoch = 0;

        private long _mediaBaseUs = 0;
        public long MediaBaseUs => Interlocked.Read(ref _mediaBaseUs);
        private volatile bool _mediaBaseSet = false;

        private long _targetVideoReadaheadMs = DEFAULT_READAHEAD_MS;
        private long _targetAudioReadaheadMs = DEFAULT_READAHEAD_MS;

        private volatile Demand? _videoDemand;
        private volatile Demand? _audioDemand;

        private long _playbackPositionUs = 0;
        private long? _resumePositionUs = null;
        private long _restartFromUs = long.MinValue;

        public long FetchFloorUs
        {
            get { lock (_pumpLock) return _resumePositionUs ?? _restartFromUs; }
        }

        private volatile ISabrSessionListener? _listener;
        public Action? OnSegmentsChanged { get; private set; }
        private object? _segmentsChangedOwner;

        public long KeepBehindUs { get; set; } = DEFAULT_KEEP_BEHIND_US;
        public int ViewportWidth { get; set; }
        public int ViewportHeight { get; set; }
        public long InitialBandwidthBytesPerSec { get; set; }
        public long MinReadaheadMs { get; set; } = 0;
        public long MaxReadaheadMs { get; set; } = 0;

        public Func<long>? PlayheadProvider { get; set; }

        private volatile Exception? _fatalError;
        public Exception? FatalError => _fatalError;

        private volatile LiveMetadata? _liveMetadata;
        public LiveMetadata? LiveMetadata => _liveMetadata;
        public long LiveMetadataAtMs { get; private set; }

        private volatile bool _inheritedContexts = false;
        private volatile bool _reestimated = false;

        private long _mediaBytes = 0;
        private long _mediaUsDelivered = 0;
        private long _throughputBytesPerSec = 0;

        private long _lastSabrSeekUs = long.MinValue;
        private long? _seekPendingUs = null;

        public SabrSession(HttpClient httpClient, string serverAbrStreamingUrl, byte[] ustreamerConfig, string videoId, ClientInfo clientInfo,
            string? poTokenRaw, bool isLive, long durationUs, bool ownsHttpClient = false, IRequestModifier? requestModifier = null, Func<bool, string?>? poTokenRefresher = null)
        {
            _httpClient = httpClient;
            _streamingUrl = serverAbrStreamingUrl;
            _ustreamerConfig = ustreamerConfig;
            VideoId = videoId;
            _clientInfo = clientInfo;
            _poTokenRaw = poTokenRaw;
            IsLive = isLive;
            DurationUs = durationUs;
            _ownsHttpClient = ownsHttpClient;
            _requestModifier = requestModifier;
            _poTokenRefresher = poTokenRefresher;

            var live = Interlocked.Increment(ref _liveSessions);
            SabrLog($"Session created for {videoId} (live sessions: {live})");
        }

        public void SetListener(ISabrSessionListener? listener) => _listener = listener;

        public void SetSegmentsChangedListener(Action? action, object? owner = null)
        {
            if (action == null && owner != null && !ReferenceEquals(_segmentsChangedOwner, owner)) return;
            _segmentsChangedOwner = action == null ? null : owner;
            OnSegmentsChanged = action;
        }

        public Transferable ExportTransferable()
        {
            var result = new Transferable()
            {
                RequestNumber = Volatile.Read(ref _requestNumber),
                PlaybackCookie = _playbackCookie,
                SabrContexts = new Dictionary<int, SabrContext>(_sabrContexts),
                ActiveSabrContexts = _activeSabrContexts.Keys.ToHashSet(),
                StreamingUrl = _streamingUrl,
                BackoffUntilMs = Interlocked.Read(ref _backoffUntilMs),
                ServerBackoffUntilMs = Interlocked.Read(ref _serverBackoffUntilMs),
                MediaBaseUs = MediaBaseUs,
                MediaBaseSet = _mediaBaseSet,
                FormatInitialization = new Dictionary<UMPFormatKey, FormatInitializationMetadata>(_formatInitialization),
                LiveMetadata = _liveMetadata
            };
            var remainingMs = result.BackoffUntilMs - NowMs();
            SabrLog($"Exported session state: rn={result.RequestNumber} contexts=[{string.Join(",", result.SabrContexts.Keys.OrderBy(x => x))}] " +
                $"active=[{string.Join(",", result.ActiveSabrContexts.OrderBy(x => x))}] " +
                $"cookie={result.PlaybackCookie?.Length ?? 0}b backoffRemaining={(remainingMs > 0 ? remainingMs : 0)}ms");
            return result;
        }

        public void Restore(Transferable state)
        {
            foreach (var pair in state.SabrContexts)
                _sabrContexts[pair.Key] = pair.Value;
            foreach (var type in state.ActiveSabrContexts)
                _activeSabrContexts[type] = true;
            _inheritedContexts = true;
            SabrLog($"Restored SABR contexts: [{string.Join(",", state.SabrContexts.Keys.OrderBy(x => x))}] " +
                $"active=[{string.Join(",", state.ActiveSabrContexts.OrderBy(x => x))}] (the session's own identity is NOT inherited)");
        }

        public void Continue(Transferable state)
        {
            Restore(state);
            _playbackCookie = state.PlaybackCookie;
            Volatile.Write(ref _requestNumber, state.RequestNumber);
            if (!string.IsNullOrEmpty(state.StreamingUrl))
                _streamingUrl = state.StreamingUrl;
            Interlocked.Exchange(ref _serverBackoffUntilMs, state.ServerBackoffUntilMs);
            Interlocked.Exchange(ref _backoffUntilMs, state.BackoffUntilMs);
            foreach (var pair in state.FormatInitialization)
                _formatInitialization[pair.Key] = pair.Value;
            if (state.MediaBaseSet)
            {
                Interlocked.Exchange(ref _mediaBaseUs, state.MediaBaseUs);
                _mediaBaseSet = true;
            }
            var remainingMs = state.BackoffUntilMs - NowMs();
            SabrLog($"Continuing session state: rn={state.RequestNumber} cookie={state.PlaybackCookie?.Length ?? 0}b backoffRemaining={(remainingMs > 0 ? remainingMs : 0)}ms");
        }

        private static string HostOf(string url)
        {
            try { return new Uri(url).Host; } catch { return "?"; }
        }

        public SabrTrackBuffer BufferFor(UMPFormat format) => BufferFor(format.Key);
        public SabrTrackBuffer BufferFor(UMPFormatKey key) => _buffers.GetOrAdd(key, k => new SabrTrackBuffer(k));

        public FormatInitializationMetadata? FormatInitializationFor(UMPFormat format) =>
            _formatInitialization.TryGetValue(format.Key, out var m) ? m : null;

        public HashSet<UMPFormatKey> ObservedFormatKeys() => _buffers.Keys.ToHashSet();

        public bool HasSeparateInit(UMPFormat format)
        {
            if (BufferFor(format.Key).InitSegment != null) return true;
            return !IsLive;
        }

        public void Start()
        {
            lock (_pumpLock)
            {
                if (_pumpTask != null || Released || _fatalError != null) return;
                _pumpTask = Task.Run(PumpAsync);
            }
        }

        public void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            SabrLog($"Session released for {VideoId} (live sessions: {Interlocked.Decrement(ref _liveSessions)})");
            _wake.Set();
            foreach (var buffer in _buffers.Values) buffer.NotifyChanged();
            try { _requestCts?.Cancel(); } catch { }

            _buffers.Clear();
            _formatInitialization.Clear();
            _listener = null;
            OnSegmentsChanged = null;
            _segmentsChangedOwner = null;
            if (_ownsHttpClient)
            {
                try { _httpClient.Dispose(); } catch { }
            }
        }

        public void Dispose() => Release();

        public void SetDemand(int role, UMPFormat format, long fromUs, object? owner = null) =>
            SetDemand(role, new List<UMPFormat>() { format }, fromUs, owner);

        public void SetDemand(int role, List<UMPFormat> acceptable, long fromUs, object? owner = null)
        {
            if (acceptable.Count == 0) return;
            var previous = role == ROLE_VIDEO ? _videoDemand : _audioDemand;

            var active = (previous != null && acceptable.Any(x => x.Key == previous.Format.Key)) ? previous.Format : null;
            if (active == null && _serverChosen.TryGetValue(role, out var chosen))
                active = acceptable.FirstOrDefault(x => x.Key == chosen);
            active ??= acceptable[0];

            if (previous != null && previous.Format.Key == active.Key && previous.FromUs == fromUs &&
                previous.Alternates.Count == acceptable.Count &&
                previous.Alternates.Zip(acceptable).All(p => p.First.Key == p.Second.Key))
            {
                if (!ReferenceEquals(previous.Owner, owner))
                {
                    var same = new Demand(active, fromUs, owner, acceptable);
                    if (role == ROLE_VIDEO) _videoDemand = same; else _audioDemand = same;
                }
                return;
            }

            if (previous == null || previous.Format.Key != active.Key)
            {
                Interlocked.Exchange(ref _lastActionMs, NowMs());
                _formatComplete.TryRemove(active.Key, out _);
                _formatNoProgress.TryRemove(active.Key, out _);
            }

            foreach (var f in acceptable) _demandedKeys[f.Key] = true;

            var demand = new Demand(active, fromUs, owner, acceptable);
            if (role == ROLE_VIDEO) _videoDemand = demand; else _audioDemand = demand;
            WakePump();
        }

        public UMPFormat? ActiveFormat(int role) => (role == ROLE_VIDEO ? _videoDemand : _audioDemand)?.Format;

        private void AdoptServerFormat(UMPFormatKey key)
        {
            foreach (var role in new[] { ROLE_VIDEO, ROLE_AUDIO })
            {
                var demand = role == ROLE_VIDEO ? _videoDemand : _audioDemand;
                if (demand == null || demand.Format.Key == key) continue;
                var chosen = demand.Alternates.FirstOrDefault(x => x.Key == key);
                if (chosen == null) continue;
                if (demand.Alternates.Count <= 1) continue;

                SabrLog($"Server switched role={role} from itag={demand.Format.Itag} to itag={chosen.Itag}");
                _serverChosen[role] = key;
                var moved = new Demand(chosen, demand.FromUs, demand.Owner, demand.Alternates);
                if (role == ROLE_VIDEO) _videoDemand = moved; else _audioDemand = moved;
                return;
            }
        }

        public void ClearDemand(int role, object? owner = null)
        {
            var current = role == ROLE_VIDEO ? _videoDemand : _audioDemand;
            if (current == null || !ReferenceEquals(current.Owner, owner)) return;
            if (role == ROLE_VIDEO) _videoDemand = null; else _audioDemand = null;
            SabrLog($"Demand for role={role} cleared by its owner");
        }

        public void SetPlaybackPosition(long positionUs) => Interlocked.Exchange(ref _playbackPositionUs, positionUs);
        public long PlaybackPositionUs => Interlocked.Read(ref _playbackPositionUs);

        public void SeekTo(long fromUs)
        {
            lock (_pumpLock)
            {
                var demands = new[] { _videoDemand, _audioDemand }.Where(x => x != null).Cast<Demand>().ToList();
                var buffered = demands.Count > 0 && demands.All(demand =>
                {
                    var segment = BufferFor(demand.Format).FirstCovering(fromUs);
                    return segment != null && segment.StartUs <= fromUs;
                });
                if (!buffered)
                {
                    Restart(fromUs);
                    return;
                }

                SetPlaybackPosition(fromUs);
                var v = _videoDemand;
                if (v != null) _videoDemand = new Demand(v.Format, fromUs, v.Owner, v.Alternates);
                var a = _audioDemand;
                if (a != null) _audioDemand = new Demand(a.Format, fromUs, a.Owner, a.Alternates);
                Interlocked.Exchange(ref _lastActionMs, NowMs());
            }
            _wake.Set();
        }

        public void Restart(long fromUs, bool force = false)
        {
            lock (_pumpLock)
            {
                var current = _resumePositionUs;
                if (!force && current != null && Math.Abs(current.Value - fromUs) < RESTART_TOLERANCE_US) return;

                SetPlaybackPosition(fromUs);
                _resumePositionUs = fromUs;
                _restartFromUs = fromUs;
                _seekPendingUs = null;
                Interlocked.Exchange(ref _lastActionMs, NowMs());
                _restartEpoch++;
                foreach (var buffer in _buffers.Values) buffer.Clear();
                _formatComplete.Clear();
                _formatNoProgress.Clear();
                _emptyResponses = 0;
                _substitutedResponses = 0;
                _consecutiveRedirects = 0;

                var v = _videoDemand;
                if (v != null) _videoDemand = new Demand(v.Format, fromUs, v.Owner, v.Alternates);
                var a = _audioDemand;
                if (a != null) _audioDemand = new Demand(a.Format, fromUs, a.Owner, a.Alternates);

                Interlocked.Exchange(ref _backoffUntilMs, Math.Max(Interlocked.Read(ref _serverBackoffUntilMs), Interlocked.Read(ref _errorBackoffUntilMs)));
                _aborting = true;
                try { _requestCts?.Cancel(); } catch { }
            }
            _wake.Set();
        }

        public void WakePump() => _wake.Set();

        private async Task PumpAsync()
        {
            try
            {
                await PumpLoopAsync();
            }
            finally
            {
                lock (_pumpLock) _pumpTask = null;
            }
        }

        private async Task PumpLoopAsync()
        {
            while (!Released)
            {
                try
                {
                    while (!Released && !NeedsData())
                        await _wake.WaitAsync(TimeSpan.FromMilliseconds(PUMP_IDLE_POLL_MS));
                    if (Released) return;

                    var now = NowMs();
                    var waitMs = Interlocked.Read(ref _backoffUntilMs) - now;
                    var serverWaitMs = Interlocked.Read(ref _serverBackoffUntilMs) - now;
                    if (waitMs > 0)
                    {
                        if (serverWaitMs > 0)
                        {
                            if (!_backoffNotified)
                            {
                                _backoffNotified = true;
                                SabrLog($"Waiting {serverWaitMs}ms -- SERVER");
                                if (Starved())
                                {
                                    _backoffShown = true;
                                    _listener?.OnBackoff(serverWaitMs);
                                }
                            }
                        }
                        else if (now - Interlocked.Read(ref _lastWaitLogMs) > 1_000)
                        {
                            Interlocked.Exchange(ref _lastWaitLogMs, now);
                            var reason = Interlocked.Read(ref _errorBackoffUntilMs) > now ? "error retry" : "stall (empty responses)";
                            SabrLog($"Waiting {waitMs}ms -- ours, {reason} (not shown)");
                        }
                        await _wake.WaitAsync(TimeSpan.FromMilliseconds(Math.Min(waitMs, PUMP_IDLE_POLL_MS)));
                        continue;
                    }
                    if (_backoffNotified)
                    {
                        _backoffNotified = false;
                        if (_backoffShown)
                        {
                            _backoffShown = false;
                            _listener?.OnBackoffEnded();
                        }
                    }

                    EvictConsumedSegments();
                    await PerformRequestAsync();
                    _consecutiveErrors = 0;
                    _aborting = false;
                }
                catch (Exception ex)
                {
                    if (Released) return;
                    if (_aborting)
                    {
                        _aborting = false;
                        lock (_pumpLock)
                        {
                            if (_resumePositionUs != null)
                                foreach (var buffer in _buffers.Values) buffer.Clear();
                        }
                        continue;
                    }
                    Logger.e(TAG, "SABR request failed", ex);

                    if (ex is SabrBlockedException && !_poTokenRefreshed && _poTokenRefresher != null)
                    {
                        _poTokenRefreshed = true;
                        try
                        {
                            var fresh = _poTokenRefresher(true);
                            if (!string.IsNullOrEmpty(fresh))
                            {
                                var decoded = SabrStreamSpec.DecodeBase64Lenient(fresh);
                                if (decoded != null)
                                {
                                    _poTokenDecoded = ByteString.CopyFrom(decoded);
                                    _poTokenResolved = true;
                                    SabrLog($"poToken refreshed after block ({decoded.Length} bytes); retrying");
                                    continue;
                                }
                            }
                        }
                        catch (Exception refreshEx)
                        {
                            Logger.w(TAG, "Failed to refresh po token", refreshEx);
                        }
                    }

                    if (ex is SabrBlockedException)
                    {
                        SabrLog($"BLOCKED: {ex.Message}. surfacing for reload.");
                        Fail(ex);
                        return;
                    }

                    if (ex is SabrReloadRequiredException || ex is SabrFormatSubstitutedException)
                    {
                        Fail(ex);
                        return;
                    }
                    _consecutiveErrors++;
                    if (_consecutiveErrors >= MAX_CONSECUTIVE_ERRORS)
                    {
                        Fail(ex);
                        return;
                    }
                    _listener?.OnSessionError(ex);
                    var delay = Math.Min(ERROR_BACKOFF_BASE_MS << Math.Min(_consecutiveErrors - 1, ERROR_BACKOFF_MAX_SHIFT), ERROR_BACKOFF_MAX_MS);
                    Interlocked.Exchange(ref _errorBackoffUntilMs, NowMs() + delay);
                    Interlocked.Exchange(ref _backoffUntilMs, Math.Max(Interlocked.Read(ref _serverBackoffUntilMs), Interlocked.Read(ref _errorBackoffUntilMs)));
                }
            }
        }

        private void Fail(Exception ex)
        {
            _fatalError = ex;
            _listener?.OnSessionError(ex);
            foreach (var buffer in _buffers.Values) buffer.NotifyChanged();
        }

        private long BandwidthEstimate()
        {
            var t = Interlocked.Read(ref _throughputBytesPerSec);
            if (t > 0) return t;
            if (InitialBandwidthBytesPerSec > 0) return InitialBandwidthBytesPerSec;
            return BANDWIDTH_ESTIMATE;
        }

        private void RecordThroughput(long bytes, long elapsedMs, long mediaUs)
        {
            if (bytes < THROUGHPUT_MIN_BYTES || elapsedMs <= 0) return;
            if (mediaUs < elapsedMs * 1000 * THROUGHPUT_MIN_SPEEDUP) return;

            var sample = bytes * 1000 / elapsedMs;
            var current = Interlocked.Read(ref _throughputBytesPerSec);
            Interlocked.Exchange(ref _throughputBytesPerSec, current == 0 ? sample : (current * (100 - THROUGHPUT_SMOOTHING) + sample * THROUGHPUT_SMOOTHING) / 100);
        }

        private bool Starved()
        {
            var demands = new[] { _videoDemand, _audioDemand }.Where(x => x != null).Cast<Demand>().ToList();
            if (demands.Count == 0) return false;
            foreach (var demand in demands)
            {
                var buffer = BufferFor(demand.Format);
                var end = buffer.BufferedEndUs(EffectiveFromUs(buffer, demand.FromUs));
                if (end == long.MinValue) return true;
                if (end - PlaybackPositionUs < STARVED_US) return true;
            }
            return false;
        }

        private bool NeedsData()
        {
            var video = _videoDemand;
            var audio = _audioDemand;
            if (video == null && audio == null) return false;
            lock (_pumpLock)
            {
                if (_resumePositionUs != null) return true;
            }
            if (video != null && NeedsData(video, Interlocked.Read(ref _targetVideoReadaheadMs))) return true;
            if (audio != null && NeedsData(audio, Interlocked.Read(ref _targetAudioReadaheadMs))) return true;
            return false;
        }

        private bool NeedsData(Demand demand, long targetMs)
        {
            if (IsComplete(demand.Format)) return false;
            var buffer = BufferFor(demand.Format);

            var anchor = IsLive ? Math.Max(demand.FromUs, RefreshPlayheadUs()) : demand.FromUs;
            var from = EffectiveFromUs(buffer, anchor);
            var end = buffer.BufferedEndUs(from);
            if (end == long.MinValue) return true;

            var target = Math.Max(targetMs, MinReadaheadMs);
            if (MaxReadaheadMs > 0) target = Math.Min(target, MaxReadaheadMs);
            if (end - from >= target * 1000) return false;
            return true;
        }

        private static long EffectiveFromUs(SabrTrackBuffer buffer, long fromUs)
        {
            var first = buffer.FirstAtOrAfter(-1);
            if (first == null) return fromUs;
            return Math.Max(fromUs, first.StartUs);
        }

        public bool IsComplete(UMPFormat format)
        {
            if (IsLive) return false;
            if (_formatComplete.TryGetValue(format.Key, out var complete) && complete) return true;
            var endSegment = _formatInitialization.TryGetValue(format.Key, out var init) ? init.EndSegmentNumber : 0;
            if (endSegment <= 0) return false;

            var fromUs = DemandFromUs(format);
            if (BufferFor(format).LastCompletedSequence(fromUs) >= endSegment)
            {
                _formatComplete[format.Key] = true;
                return true;
            }
            return false;
        }

        public int EndSegmentNumber(UMPFormat format) =>
            _formatInitialization.TryGetValue(format.Key, out var init) ? init.EndSegmentNumber : 0;

        private long DemandFromUs(UMPFormat format)
        {
            long? raw = null;
            var v = _videoDemand;
            var a = _audioDemand;
            if (v != null && v.Alternates.Any(x => x.Key == format.Key)) raw = v.FromUs;
            else if (a != null && a.Alternates.Any(x => x.Key == format.Key)) raw = a.FromUs;
            if (raw == null) return long.MinValue;
            return EffectiveFromUs(BufferFor(format), raw.Value);
        }

        private long RefreshPlayheadUs()
        {
            bool idle;
            lock (_pumpLock) idle = _resumePositionUs == null && _seekPendingUs == null;
            if (idle)
            {
                var provided = PlayheadProvider?.Invoke();
                if (provided != null && provided.Value != long.MinValue)
                    SetPlaybackPosition(provided.Value);
            }
            return PlaybackPositionUs;
        }

        private long RequestPositionUs()
        {
            if (_resumePositionUs != null) return _resumePositionUs.Value;
            if (_seekPendingUs != null) return _seekPendingUs.Value;

            RefreshPlayheadUs();

            if (IsLive && _liveMetadata == null) return LIVE_HEAD_PLAYER_TIME_US;

            var earliest = long.MaxValue;
            foreach (var demand in new[] { _videoDemand, _audioDemand })
            {
                if (demand == null) continue;
                var buffer = BufferFor(demand.Format);
                var effective = EffectiveFromUs(buffer, demand.FromUs);
                var end = buffer.BufferedEndUs(effective);
                var from = end == long.MinValue ? effective : Math.Max(effective, end);
                earliest = Math.Min(earliest, from);
            }
            var position = PlaybackPositionUs;
            if (!IsLive) return earliest == long.MaxValue ? position : earliest;

            var head = _liveMetadata?.HeadSequenceTimeMs ?? 0;
            var headUs = head > 0 ? head * 1000L : long.MaxValue;
            if (earliest == long.MaxValue) return Math.Min(position, headUs);
            return Math.Min(Math.Min(position, earliest), headUs);
        }

        private void EvictConsumedSegments()
        {
            long floor;
            lock (_pumpLock) floor = _resumePositionUs ?? PlaybackPositionUs;
            var threshold = Math.Min(PlaybackPositionUs, floor) - KeepBehindUs;
            if (threshold <= 0) return;
            foreach (var buffer in _buffers.Values)
                buffer.EvictBefore(threshold);
        }

        private async Task PerformRequestAsync()
        {
            int startEpoch;
            UMPFormat? video;
            UMPFormat? audio;
            long? requestedResume;
            long positionUs;
            CancellationTokenSource cts;
            lock (_pumpLock)
            {
                _aborting = false;
                startEpoch = _restartEpoch;
                video = _videoDemand?.Format;
                audio = _audioDemand?.Format;
                if (video == null && audio == null) return;
                requestedResume = _resumePositionUs;
                positionUs = RequestPositionUs();
                cts = new CancellationTokenSource();
                _requestCts = cts;
            }
            _demandedHeaders = 0;
            _foreignHeaders = 0;

            {
                var v = video != null ? $"v[itag={video.Itag} lastSeq={BufferFor(video).LastCompletedFromFront()} end={BufferFor(video).BufferedEndFromFrontUs() / 1000}ms]" : "";
                var a = audio != null ? $"a[itag={audio.Itag} lastSeq={BufferFor(audio).LastCompletedFromFront()} end={BufferFor(audio).BufferedEndFromFrontUs() / 1000}ms]" : "";
                var throughput = Interlocked.Read(ref _throughputBytesPerSec);
                SabrLog($"Request #{Volatile.Read(ref _requestNumber) + 1} live={IsLive} playerTimeMs={positionUs / 1000} {v} {a} " +
                    $"bw={BandwidthEstimate() / 1024}KB/s{(throughput == 0 ? "(seed)" : "")} " +
                    $"readahead=v{Interlocked.Read(ref _targetVideoReadaheadMs)}ms/a{Interlocked.Read(ref _targetAudioReadaheadMs)}ms");
            }

            var body = BuildRequest(video, audio, positionUs / 1000).ToByteArray();
            var url = AppendRequestNumber(_streamingUrl);

            var headers = new EngineHttpHeaders();
            headers.Add("Accept", "application/vnd.yt-ump");
            headers.Add("Accept-Encoding", "identity");
            headers.Add("Origin", "https://www.youtube.com");
            headers.Add("Referer", "https://www.youtube.com/");
            headers.Add("User-Agent", SabrStreamSpec.DEFAULT_USER_AGENT);
            if (_requestModifier != null)
            {
                try
                {
                    var modified = _requestModifier.ModifyRequest(url, headers);
                    if (modified != null)
                    {
                        if (!string.IsNullOrEmpty(modified.Url)) url = modified.Url;
                        if (modified.Headers != null)
                            foreach (var header in modified.Headers)
                                headers.Set(header.Key, header.Value);
                    }
                }
                catch (Exception ex)
                {
                    Logger.w(TAG, "Request modifier failed for SABR request", ex);
                }
            }

            (int Seq, long End)? videoBefore = video != null ? (BufferFor(video).HighestSequence, BufferFor(video).BufferedEndUs(DemandFromUs(video))) : null;
            (int Seq, long End)? audioBefore = audio != null ? (BufferFor(audio).HighestSequence, BufferFor(audio).BufferedEndUs(DemandFromUs(audio))) : null;
            var videoCountBefore = video != null ? BufferFor(video).SegmentCount : 0;
            var audioCountBefore = audio != null ? BufferFor(audio).SegmentCount : 0;
            var videoInitBefore = video != null ? BufferFor(video).InitSegment : null;
            var audioInitBefore = audio != null ? BufferFor(audio).InitSegment : null;

            Interlocked.Exchange(ref _lastRequestMs, NowMs());
            var rn = Volatile.Read(ref _requestNumber);
            var sentMs = NowMs();

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Version = HttpVersion.Version11;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/x-protobuf");
            foreach (var header in headers)
            {
                if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            try
            {
                HttpResponseMessage response;
                try
                {
                    response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                }
                catch (Exception ex)
                {
                    SabrLog($"REQUEST FAILED after {NowMs() - sentMs}ms rn={rn} host={HostOf(url)} firstOfSession={rn == 1} inheritedContexts={_inheritedContexts} " +
                        $"sentContexts=[{string.Join(",", _activeSabrContexts.Keys.OrderBy(x => x))}] cookie={_playbackCookie?.Length ?? 0}b " +
                        $"v={video?.Itag} a={audio?.Itag} bodyBytes={body.Length} -- {ex.GetType().Name}: {ex.Message}");
                    throw;
                }

                using (response)
                {
                    var headersMs = NowMs() - sentMs;
                    if (headersMs > SLOW_REQUEST_LOG_MS)
                        SabrLog($"SLOW REQUEST {headersMs}ms to first response byte rn={rn} host={HostOf(url)} firstOfSession={rn == 1} " +
                            $"inheritedContexts={_inheritedContexts} v={video?.Itag} a={audio?.Itag} bodyBytes={body.Length} code={(int)response.StatusCode}");

                    if (Released) return;
                    if (!response.IsSuccessStatusCode)
                    {
                        if (response.StatusCode == HttpStatusCode.Forbidden)
                            throw new SabrBlockedException("SABR request returned HTTP 403");
                        throw new SabrException($"SABR request returned HTTP {(int)response.StatusCode}");
                    }

                    lock (_pumpLock)
                    {
                        if (_resumePositionUs == requestedResume) _resumePositionUs = null;
                    }

                    var acceptedKeys = (_videoDemand?.Alternates ?? new List<UMPFormat>())
                        .Concat(_audioDemand?.Alternates ?? new List<UMPFormat>())
                        .Select(x => x.Key).ToHashSet();

                    bool redirected;
                    var bytesBefore = Interlocked.Read(ref _mediaBytes);
                    var mediaUsBefore = Interlocked.Read(ref _mediaUsDelivered);
                    try
                    {
                        using var stream = new BufferedStream(await response.Content.ReadAsStreamAsync(cts.Token), 64 * 1024);
                        redirected = await ConsumeAsync(new UmpReader(stream), positionUs, acceptedKeys, cts.Token);
                    }
                    finally
                    {
                        RecordThroughput(
                            Interlocked.Read(ref _mediaBytes) - bytesBefore,
                            NowMs() - sentMs,
                            Interlocked.Read(ref _mediaUsDelivered) - mediaUsBefore);
                    }

                    if (_restartEpoch != startEpoch)
                    {
                        lock (_pumpLock)
                        {
                            foreach (var buffer in _buffers.Values) buffer.Clear();
                        }
                        _emptyResponses = 0;
                        return;
                    }

                    var advanced = (video != null && BufferFor(video).SegmentCount > videoCountBefore) ||
                        (audio != null && BufferFor(audio).SegmentCount > audioCountBefore) ||
                        (video != null && videoInitBefore == null && BufferFor(video).InitSegment != null) ||
                        (audio != null && audioInitBefore == null && BufferFor(audio).InitSegment != null);

                    ClearSeekIfLanded(advanced);

                    if (advanced)
                    {
                        _emptyResponses = 0;
                        _substitutedResponses = 0;
                        _consecutiveRedirects = 0;
                        Interlocked.Exchange(ref _errorBackoffUntilMs, 0);
                        Interlocked.Exchange(ref _backoffUntilMs, Interlocked.Read(ref _serverBackoffUntilMs));
                    }

                    if (IsLive)
                    {
                        if (!_aborting && !redirected) ClampToSeekableWindow();
                        if (!advanced && !redirected)
                            Interlocked.Exchange(ref _backoffUntilMs, Math.Max(Interlocked.Read(ref _backoffUntilMs), NowMs() + LIVE_POLL_MS));
                    }
                    else if (!advanced && !_aborting && !redirected)
                    {
                        UpdateProgress(video, videoBefore, positionUs);
                        UpdateProgress(audio, audioBefore, positionUs);
                        if ((video == null || IsComplete(video)) && (audio == null || IsComplete(audio))) return;

                        var demandStable = _videoDemand?.Format.Key == video?.Key && _audioDemand?.Format.Key == audio?.Key;
                        if (_demandedHeaders == 0 && _foreignHeaders > 0 && demandStable)
                        {
                            _substitutedResponses++;
                            if (_substitutedResponses >= MAX_SUBSTITUTED_RESPONSES)
                                throw new SabrFormatSubstitutedException(
                                    $"Requested itag={video?.Itag ?? audio?.Itag} but the server served a different " +
                                    $"format for {_substitutedResponses} consecutive requests. The plugin's formats " +
                                    "are out of sync with the app.");
                        }

                        _emptyResponses++;
                        var delay = Math.Min(EMPTY_BACKOFF_BASE_MS << Math.Min(_emptyResponses - 1, ERROR_BACKOFF_MAX_SHIFT), ERROR_BACKOFF_MAX_MS);
                        Interlocked.Exchange(ref _backoffUntilMs, Math.Max(Interlocked.Read(ref _backoffUntilMs), NowMs() + delay));
                        SabrLog($"Response carried no new media ({_emptyResponses}/{MAX_EMPTY_RESPONSES}) " +
                            $"at playerTimeMs={positionUs / 1000} demandedHeaders={_demandedHeaders} foreign={_foreignHeaders}");
                        if (_emptyResponses >= MAX_EMPTY_RESPONSES)
                            throw new SabrException($"Server returned no media for {_emptyResponses} consecutive requests");
                    }
                    else if (!IsLive && !_aborting && !redirected)
                    {
                        UpdateProgress(video, videoBefore, positionUs);
                        UpdateProgress(audio, audioBefore, positionUs);
                    }
                }
            }
            finally
            {
                lock (_pumpLock)
                {
                    if (ReferenceEquals(_requestCts, cts)) _requestCts = null;
                }
                cts.Dispose();
            }
        }

        private void UpdateProgress(UMPFormat? format, (int Seq, long End)? before, long requestedUs)
        {
            if (format == null || before == null) return;
            if (_formatInitialization.TryGetValue(format.Key, out var init) && init.EndSegmentNumber > 0) return;

            var (beforeSeq, beforeEnd) = before.Value;
            var advanced = BufferFor(format).HighestSequence > beforeSeq;
            if (advanced)
            {
                _formatNoProgress[format.Key] = 0;
                return;
            }

            if (beforeSeq < 0 || beforeEnd == long.MinValue) return;
            if (init == null) return;

            var endUs = init.EndTimeMs > 0 ? init.EndTimeMs * 1000 : DurationUs;
            var slackUs = Math.Max(FRONTIER_EPSILON_US, BufferFor(format).Get(beforeSeq)?.DurationUs ?? 0);
            if (endUs > 0 && beforeEnd < endUs - FRONTIER_EPSILON_US) return;
            if (endUs > 0 && requestedUs < endUs - slackUs) return;

            var atFrontier = beforeEnd <= requestedUs + FRONTIER_EPSILON_US;
            if (!atFrontier) return;
            var n = (_formatNoProgress.TryGetValue(format.Key, out var c) ? c : 0) + 1;
            _formatNoProgress[format.Key] = n;
            if (n >= NO_PROGRESS_THRESHOLD) _formatComplete[format.Key] = true;
        }

        private static string SeekSourceName(int source) => source switch
        {
            9 => "SABR_PARTIAL_CHUNK",
            10 => "SABR_SEEK_TO_HEAD",
            12 => "SABR_SEEK_TO_DVR_LOWER_BOUND",
            13 => "SABR_SEEK_TO_DVR_UPPER_BOUND",
            17 => "SABR_ACCURATE_SEEK",
            29 => "SABR_INGESTION_WALL_TIME_SEEK",
            59 => "SABR_SEEK_TO_CLOSEST_KEYFRAME",
            108 => "SABR_RELOAD_PLAYER_RESPONSE_TOKEN_SEEK",
            _ => source.ToString()
        };

        private void ApplySabrSeek(long seekToUs, long requestedPositionUs)
        {
            if (seekToUs == Interlocked.Read(ref _lastSabrSeekUs) && _seekPendingUs == null) return;

            var metadata = _liveMetadata;
            if (IsLive && metadata != null)
            {
                var minScale = metadata.MinSeekableTimescale;
                var maxScale = metadata.MaxSeekableTimescale;
                if (minScale <= 0 || maxScale <= 0) return;
                var windowStartUs = metadata.MinSeekableTimeTicks * 1_000_000L / minScale;
                var windowEndUs = metadata.MaxSeekableTimeTicks * 1_000_000L / maxScale;
                if (seekToUs < windowStartUs || seekToUs > windowEndUs + SABR_SEEK_SLACK_US)
                {
                    LiveLog($"Ignoring SabrSeek to {seekToUs / 1000}ms: outside the seekable window {windowStartUs / 1000}..{windowEndUs / 1000}ms");
                    return;
                }
            }

            LiveLog($"SabrSeek: the server will not serve {requestedPositionUs / 1000}ms, moving to {seekToUs / 1000}ms");
            Interlocked.Exchange(ref _lastSabrSeekUs, seekToUs);

            lock (_pumpLock)
            {
                if (!_adCuepoints.IsEmpty)
                {
                    SabrLog($"Dropping {_adCuepoints.Count} cuepoint(s) across a server seek");
                    _adCuepoints.Clear();
                }
                _seekPendingUs = seekToUs;
                SetPlaybackPosition(seekToUs);
                var v = _videoDemand;
                if (v != null) _videoDemand = new Demand(v.Format, seekToUs, v.Owner, v.Alternates);
                var a = _audioDemand;
                if (a != null) _audioDemand = new Demand(a.Format, seekToUs, a.Owner, a.Alternates);
                Interlocked.Exchange(ref _lastActionMs, NowMs());
            }
            _wake.Set();
        }

        private void OnCuepointList(CuepointList list)
        {
            foreach (var info in list.CuepointInfo)
            {
                var cuepoint = info.Cuepoint;
                if (cuepoint == null) continue;
                var id = cuepoint.Identifier;
                if (string.IsNullOrEmpty(id)) continue;

                if (cuepoint.Event == CuepointEvent.Stop)
                {
                    if (_adCuepoints.TryRemove(id, out _))
                        SabrLog($"Cuepoint {id} stopped");
                    continue;
                }

                long endMs = 0;
                if (info.TimeRange != null && info.TimeRange.Timescale > 0)
                    endMs = (info.TimeRange.StartTicks + info.TimeRange.DurationTicks) * 1000 / info.TimeRange.Timescale;
                var expiryMs = Math.Max(endMs, (long)(cuepoint.OffsetSec + cuepoint.DurationSec) * 1000);

                if (_adCuepoints.TryAdd(id, expiryMs))
                    SabrLog($"Cuepoint {id} ({cuepoint.Type}, {cuepoint.Event}, {cuepoint.DurationSec}s)");
                else
                    _adCuepoints[id] = expiryMs;
            }
        }

        private void ExpireCuepoints(long positionMs)
        {
            if (_adCuepoints.IsEmpty) return;
            foreach (var entry in _adCuepoints.ToList())
            {
                if (entry.Value >= 1 && entry.Value < positionMs)
                {
                    SabrLog($"Cuepoint {entry.Key} has passed ({entry.Value}ms < {positionMs}ms); dropping it");
                    _adCuepoints.TryRemove(entry.Key, out _);
                }
            }
        }

        private void ClearSeekIfLanded(bool landedAfterSeek)
        {
            lock (_pumpLock)
            {
                var pending = _seekPendingUs;
                if (pending == null) return;
                if (!landedAfterSeek) return;
                LiveLog($"SabrSeek to {pending.Value / 1000}ms satisfied by fresh media; resuming");
                _seekPendingUs = null;
            }
        }

        private async Task<bool> ConsumeAsync(UmpReader reader, long requestedPositionUs, HashSet<UMPFormatKey> requestedKeys, CancellationToken cancellationToken)
        {
            var pending = new Dictionary<int, SabrSegment>();
            string? redirect = null;
            long? seekToUs = null;
            var partCounts = new SortedDictionary<int, int>();

            try
            {
                while (!Released)
                {
                    var part = await reader.NextAsync(cancellationToken);
                    if (part == null) break;
                    partCounts[part.Type] = (partCounts.TryGetValue(part.Type, out var c) ? c : 0) + 1;
                    switch (part.Type)
                    {
                        case UmpPartType.MEDIA_HEADER:
                            OnMediaHeader(MediaHeader.Parser.ParseFrom(part.Data), pending, requestedKeys);
                            break;

                        case UmpPartType.MEDIA:
                            {
                                var (headerId, offset) = UmpReader.DecodeVarInt(part.Data, 0);
                                Interlocked.Add(ref _mediaBytes, part.Data.Length - offset);
                                if (pending.TryGetValue((int)headerId, out var segment))
                                {
                                    segment.Append(part.Data, offset, part.Data.Length - offset);
                                    BufferFor(segment.FormatKey).NotifyChanged();
                                }
                                break;
                            }

                        case UmpPartType.MEDIA_END:
                            {
                                var (headerId, _) = UmpReader.DecodeVarInt(part.Data, 0);
                                if (pending.Remove((int)headerId, out var segment))
                                {
                                    if (segment.ContentLength > 0 && segment.Size != segment.ContentLength)
                                    {
                                        SabrLog($"Dropping truncated seq={segment.SequenceNumber} itag={segment.FormatKey.Itag} got={segment.Size} want={segment.ContentLength}");
                                        BufferFor(segment.FormatKey).Discard(segment);
                                    }
                                    else
                                    {
                                        segment.MarkComplete();
                                        BufferFor(segment.FormatKey).NotifyChanged();
                                        OnSegmentsChanged?.Invoke();
                                    }
                                }
                                break;
                            }

                        case UmpPartType.NEXT_REQUEST_POLICY:
                            OnNextRequestPolicy(NextRequestPolicy.Parser.ParseFrom(part.Data));
                            break;

                        case UmpPartType.FORMAT_INITIALIZATION_METADATA:
                            {
                                var metadata = FormatInitializationMetadata.Parser.ParseFrom(part.Data);
                                var key = UMPFormatKey.Of(metadata.FormatId?.Itag ?? 0, metadata.FormatId?.Lmt ?? 0, metadata.FormatId?.Xtags);
                                _formatInitialization[key] = metadata;
                                SabrLog($"FormatInit itag={metadata.FormatId?.Itag} mime='{metadata.MimeType}' " +
                                    $"initRange={metadata.InitRange?.Start}-{metadata.InitRange?.End} " +
                                    $"indexRange={metadata.IndexRange?.Start}-{metadata.IndexRange?.End} " +
                                    $"endSeg={metadata.EndSegmentNumber} endMs={metadata.EndTimeMs}");
                                _listener?.OnFormatInitialization(metadata);
                                break;
                            }

                        case UmpPartType.LIVE_METADATA:
                            {
                                var metadata = LiveMetadata.Parser.ParseFrom(part.Data);
                                var first = !_reestimated;
                                _reestimated = true;
                                _liveMetadata = metadata;
                                LiveMetadataAtMs = NowMs();

                                if (first) ReestimateInexactDurations();
                                if (IsLive)
                                {
                                    var minS = metadata.MinSeekableTimescale > 0 ? (double)metadata.MinSeekableTimeTicks / metadata.MinSeekableTimescale : double.NaN;
                                    var maxS = metadata.MaxSeekableTimescale > 0 ? (double)metadata.MaxSeekableTimeTicks / metadata.MaxSeekableTimescale : double.NaN;
                                    LiveLog($"LiveMetadata headSeq={metadata.HeadSequenceNumber} headTimeMs={metadata.HeadSequenceTimeMs} " +
                                        $"minSeekable={metadata.MinSeekableTimeTicks}/{metadata.MinSeekableTimescale} ({minS:F1}s) " +
                                        $"maxSeekable={metadata.MaxSeekableTimeTicks}/{metadata.MaxSeekableTimescale} ({maxS:F1}s)");

                                    if (!_mediaBaseSet && metadata.MinSeekableTimescale > 0)
                                    {
                                        Interlocked.Exchange(ref _mediaBaseUs, metadata.MinSeekableTimeTicks * 1_000_000L / metadata.MinSeekableTimescale);
                                        _mediaBaseSet = true;
                                        LiveLog($"mediaBaseUs={MediaBaseUs / 1000}ms (head {metadata.HeadSequenceTimeMs}ms)");
                                    }
                                }
                                _listener?.OnLiveMetadata(metadata);
                                break;
                            }

                        case UmpPartType.SABR_CONTEXT_UPDATE:
                            {
                                var update = SabrContextUpdate.Parser.ParseFrom(part.Data);
                                SabrLog($"SabrContextUpdate type={update.Type} scope={update.Scope} sendByDefault={update.SendByDefault} valueBytes={update.Value.Length}");
                                _sabrContexts[update.Type] = new SabrContext() { Type = update.Type, Value = update.Value };
                                if (update.SendByDefault) _activeSabrContexts[update.Type] = true;
                                break;
                            }

                        case UmpPartType.SABR_CONTEXT_SENDING_POLICY:
                            {
                                var policy = SabrContextSendingPolicy.Parser.ParseFrom(part.Data);
                                SabrLog($"SabrContextSendingPolicy start=[{string.Join(",", policy.StartPolicy)}] stop=[{string.Join(",", policy.StopPolicy)}] discard=[{string.Join(",", policy.DiscardPolicy)}]");
                                foreach (var type in policy.StartPolicy) _activeSabrContexts[type] = true;
                                foreach (var type in policy.StopPolicy) _activeSabrContexts.TryRemove(type, out _);
                                foreach (var type in policy.DiscardPolicy)
                                {
                                    _sabrContexts.TryRemove(type, out _);
                                    _activeSabrContexts.TryRemove(type, out _);
                                }
                                break;
                            }

                        case UmpPartType.STREAM_PROTECTION_STATUS:
                            {
                                var status = StreamProtectionStatus.Parser.ParseFrom(part.Data).Status;
                                SabrLog($"StreamProtectionStatus={status} rn={Volatile.Read(ref _requestNumber)} " +
                                    $"sentContexts=[{string.Join(",", _activeSabrContexts.Keys.OrderBy(x => x))}] heldContexts=[{string.Join(",", _sabrContexts.Keys.OrderBy(x => x))}] " +
                                    $"cookie={_playbackCookie?.Length ?? 0}b poTokenBytes={ResolvePoToken()?.Length ?? 0}");
                                if (status == PROTECTION_STATUS_ATTESTATION_REQUIRED)
                                    throw new SabrBlockedException("po token rejected (attestation required)");
                                break;
                            }

                        case UmpPartType.SABR_REDIRECT:
                            redirect = SabrRedirect.Parser.ParseFrom(part.Data).Url;
                            break;

                        case UmpPartType.SABR_SEEK:
                            {
                                var seek = SabrSeek.Parser.ParseFrom(part.Data);
                                var scale = seek.SeekMediaTimescale;
                                seekToUs = scale > 0 ? seek.SeekMediaTime * 1_000_000L / scale : null;
                                LiveLog($"SabrSeek to {seek.SeekMediaTime}/{scale} ({(seekToUs ?? 0) / 1000}ms) source={SeekSourceName(seek.SeekSource)}");
                                break;
                            }

                        case UmpPartType.SABR_ERROR:
                            {
                                var error = SabrError.Parser.ParseFrom(part.Data);
                                throw new SabrException($"SABR error {error.Code} {error.Type}");
                            }

                        case UmpPartType.RELOAD_PLAYER_RESPONSE:
                            throw new SabrReloadRequiredException("Server asked for a fresh player response");

                        case UmpPartType.SNACKBAR_MESSAGE:
                            SabrLog($"Snackbar message id={SnackbarMessage.Parser.ParseFrom(part.Data).Id} (ignored)");
                            break;

                        case UmpPartType.CUEPOINT_LIST:
                            OnCuepointList(CuepointList.Parser.ParseFrom(part.Data));
                            break;
                    }
                }
            }
            finally
            {
                SabrLog($"Response rn={Volatile.Read(ref _requestNumber)} parts=" +
                    string.Join(",", partCounts.Select(x => $"{UmpPartType.Name(x.Key)}x{x.Value}")));

                foreach (var segment in pending.Values)
                {
                    var buffer = BufferFor(segment.FormatKey);
                    buffer.Discard(segment);
                    buffer.NotifyChanged();
                }
            }

            if (redirect != null)
            {
                Logger.i(TAG, "SABR redirect issued");
                _streamingUrl = redirect;
                _consecutiveRedirects++;
                if (_consecutiveRedirects >= MAX_REDIRECTS)
                    throw new SabrException($"SABR redirected {_consecutiveRedirects} times without delivering media");
                Interlocked.Exchange(ref _backoffUntilMs, Interlocked.Read(ref _serverBackoffUntilMs));
                lock (_pumpLock) _resumePositionUs = requestedPositionUs;
                return true;
            }

            if (seekToUs != null) ApplySabrSeek(seekToUs.Value, requestedPositionUs);
            return false;
        }

        private void OnMediaHeader(MediaHeader header, Dictionary<int, SabrSegment> pending, HashSet<UMPFormatKey> requestedKeys)
        {
            var key = UMPFormatKey.Of(header.Itag, header.Lmt, header.Xtags);
            var buffer = BufferFor(key);

            if (requestedKeys.Contains(key))
            {
                _demandedHeaders++;
                AdoptServerFormat(key);
            }
            else if (!_demandedKeys.ContainsKey(key)) _foreignHeaders++;

            var existing = header.IsInitSegment ? buffer.InitSegment : buffer.Get(header.SequenceNumber);
            if (existing != null && existing.IsComplete)
            {
                pending[header.HeaderId] = new SabrSegment(key, header.SequenceNumber, header.IsInitSegment, 0, 0, 0);
                return;
            }

            var timescale = header.TimeRange?.Timescale ?? 0;
            long startUs;
            long durationUs;
            if (timescale > 0)
            {
                startUs = TicksToUs(header.TimeRange!.StartTicks, timescale);
                durationUs = TicksToUs(header.TimeRange.DurationTicks, timescale);
            }
            else
            {
                startUs = header.StartMs * 1000;
                durationUs = header.DurationMs * 1000;
            }
            var exact = durationUs > 0;
            if (!header.IsInitSegment)
            {
                BackPatchPrevious(buffer, header.SequenceNumber, startUs);
                if (!exact) durationUs = EstimateSegmentUs(header.SequenceNumber, startUs, buffer);

                var clock = ActiveFormat(ROLE_VIDEO) ?? ActiveFormat(ROLE_AUDIO);
                if (clock != null && clock.Key == key) Interlocked.Add(ref _mediaUsDelivered, durationUs);
            }

            var segment = new SabrSegment(
                key,
                header.SequenceNumber,
                header.IsInitSegment,
                startUs,
                durationUs,
                (int)header.ContentLength,
                timescale > 0 ? header.TimeRange!.StartTicks : 0,
                timescale);
            if (exact) segment.SetDuration(durationUs, true);
            pending[header.HeaderId] = segment;
            buffer.Announce(segment);

            SabrLog($"MediaHeader itag={header.Itag} seq={header.SequenceNumber} init={header.IsInitSegment} " +
                $"startMs={startUs / 1000} durMs={durationUs / 1000} tr={header.TimeRange?.StartTicks}/{header.TimeRange?.DurationTicks}@{header.TimeRange?.Timescale} " +
                $"hdrStartMs={header.StartMs} hdrDurMs={header.DurationMs} len={header.ContentLength}");
        }

        private long EstimateSegmentUs(int sequence, long startUs, SabrTrackBuffer buffer)
        {
            var cadence = LiveCadenceUs(buffer);
            if (cadence > 0) return cadence;

            var lm = _liveMetadata;
            if (lm != null && lm.HeadSequenceNumber > sequence)
            {
                var segments = lm.HeadSequenceNumber - sequence;
                var spanUs = lm.HeadSequenceTimeMs * 1000 - startUs;
                if (spanUs > 0) return Math.Max(spanUs / segments, 1);
            }

            var prev = buffer.Get(sequence - 1);
            if (prev != null && prev.DurationUs > 0) return prev.DurationUs;

            return DEFAULT_LIVE_SEGMENT_US;
        }

        private long LiveCadenceUs(SabrTrackBuffer buffer)
        {
            var observed = buffer.RecentStartDeltasUs(8);
            if (observed.Count > 0)
            {
                observed.Sort();
                return Math.Max(observed[observed.Count / 2], 1);
            }

            var lm = _liveMetadata;
            if (lm == null) return -1;
            var anchor = buffer.FirstAtOrAfter(-1);
            if (anchor == null) return -1;
            if (lm.HeadSequenceNumber <= anchor.SequenceNumber) return -1;

            var spanUs = lm.HeadSequenceTimeMs * 1000 - anchor.StartUs;
            if (spanUs <= 0) return -1;
            return Math.Max(spanUs / (lm.HeadSequenceNumber - anchor.SequenceNumber), 1);
        }

        private void BackPatchPrevious(SabrTrackBuffer buffer, int sequence, long startUs)
        {
            var prev = buffer.Get(sequence - 1);
            if (prev == null) return;
            if (prev.DurationExact) return;

            var deltaUs = startUs - prev.StartUs;
            if (deltaUs <= 0) return;

            var cadence = LiveCadenceUs(buffer);
            if (cadence <= 0) cadence = prev.DurationUs;
            if (cadence > 0 && deltaUs > cadence * 3 / 2) return;

            prev.SetDuration(deltaUs, true);
        }

        private void ReestimateInexactDurations()
        {
            foreach (var buffer in _buffers.Values)
            {
                foreach (var segment in buffer.Snapshot())
                {
                    if (segment.DurationExact || segment.IsInit) continue;
                    var next = buffer.Get(segment.SequenceNumber + 1);
                    if (next != null) BackPatchPrevious(buffer, next.SequenceNumber, next.StartUs);
                    if (segment.DurationExact) continue;
                    segment.SetDuration(EstimateSegmentUs(segment.SequenceNumber, segment.StartUs, buffer), false);
                }
            }
        }

        private void OnNextRequestPolicy(NextRequestPolicy policy)
        {
            if (!policy.PlaybackCookie.IsEmpty) _playbackCookie = policy.PlaybackCookie;
            if (policy.TargetVideoReadaheadMs > 0) Interlocked.Exchange(ref _targetVideoReadaheadMs, policy.TargetVideoReadaheadMs);
            if (policy.TargetAudioReadaheadMs > 0) Interlocked.Exchange(ref _targetAudioReadaheadMs, policy.TargetAudioReadaheadMs);
            if (policy.BackoffTimeMs > 0)
            {
                SabrLog($"SERVER BACKOFF {policy.BackoffTimeMs}ms at rn={Volatile.Read(ref _requestNumber)} " +
                    $"sentContexts=[{string.Join(",", _activeSabrContexts.Keys.OrderBy(x => x))}] heldContexts=[{string.Join(",", _sabrContexts.Keys.OrderBy(x => x))}] " +
                    $"cookie={_playbackCookie?.Length ?? 0}b poTokenBytes={ResolvePoToken()?.Length ?? 0}");
                Interlocked.Exchange(ref _serverBackoffUntilMs, NowMs() + policy.BackoffTimeMs);
                Interlocked.Exchange(ref _backoffUntilMs, Math.Max(Interlocked.Read(ref _backoffUntilMs), Interlocked.Read(ref _serverBackoffUntilMs)));
            }
        }

        private static FormatId ToFormatId(UMPFormat format)
        {
            var id = new FormatId() { Itag = format.Itag, Lmt = format.LastModified };
            if (!string.IsNullOrEmpty(format.Xtags)) id.Xtags = format.Xtags;
            return id;
        }

        private VideoPlaybackAbrRequest BuildRequest(UMPFormat? video, UMPFormat? audio, long positionMs)
        {
            var now = NowMs();
            var abrState = new ClientAbrState()
            {
                PlayerTimeMs = positionMs,
                BandwidthEstimate = BandwidthEstimate(),
                NetworkLatencyMs = Random.Shared.NextInt64(7, 97),
                TimeSinceLastActionMs = now - Interlocked.Read(ref _lastActionMs),
                TimeSinceLastManualFormatSelectionMs = now - _createdAtMs,
                LastManualDirection = 0,
                DrcEnabled = true,
                Visibility = 0,
                PreferVp9 = false
            };

            var lastRequest = Interlocked.Read(ref _lastRequestMs);
            if (lastRequest > 0) abrState.TimeSinceLastRequestMs = now - lastRequest;

            var videoAlternates = _videoDemand?.Alternates ?? new List<UMPFormat>();
            var audioAlternates = _audioDemand?.Alternates ?? new List<UMPFormat>();

            if (video != null)
            {
                var cap = videoAlternates.OrderByDescending(x => x.Height).FirstOrDefault() ?? video;
                abrState.ClientViewportWidth = ViewportWidth > 0 ? ViewportWidth : cap.Width;
                abrState.ClientViewportHeight = ViewportHeight > 0 ? ViewportHeight : cap.Height;

                if (videoAlternates.Count <= 1)
                {
                    abrState.LastManualSelectedResolution = video.Height;
                    abrState.StickyResolution = video.Height;
                    abrState.SelectedQualityHeight = video.Height;
                }

                if (audio == null) abrState.EnabledTrackTypesBitfield = MediaType.Video;
            }
            else if (audio != null)
            {
                abrState.EnabledTrackTypesBitfield = MediaType.Audio;
            }

            var streamerContext = new StreamerContext() { ClientInfo = _clientInfo };
            foreach (var pair in _sabrContexts)
            {
                if (_activeSabrContexts.ContainsKey(pair.Key)) streamerContext.SabrContexts.Add(pair.Value);
                else streamerContext.UnsentSabrContexts.Add(pair.Key);
            }

            var poToken = ResolvePoToken();
            if (poToken != null) streamerContext.PoToken = poToken;
            var cookie = _playbackCookie;
            if (cookie != null) streamerContext.PlaybackCookie = cookie;

            var request = new VideoPlaybackAbrRequest()
            {
                ClientAbrState = abrState,
                VideoPlaybackUstreamerConfig = ByteString.CopyFrom(_ustreamerConfig),
                StreamerContext = streamerContext
            };

            ExpireCuepoints(positionMs);
            foreach (var id in _adCuepoints.Keys)
                request.AdCuepoints.Add(new AdCuepointConfig() { CuepointId = id, MagicValue = AD_CUEPOINT_MAGIC });

            if (videoAlternates.Count == 0) { if (video != null) request.PreferredVideoFormatIds.Add(ToFormatId(video)); }
            else foreach (var f in videoAlternates) request.PreferredVideoFormatIds.Add(ToFormatId(f));

            if (audioAlternates.Count == 0) { if (audio != null) request.PreferredAudioFormatIds.Add(ToFormatId(audio)); }
            else foreach (var f in audioAlternates) request.PreferredAudioFormatIds.Add(ToFormatId(f));

            var held = videoAlternates.Concat(audioAlternates).ToList();
            if (held.Count == 0) held = new[] { video, audio }.Where(x => x != null).Cast<UMPFormat>().ToList();
            foreach (var format in held)
            {
                if (!_formatInitialization.ContainsKey(format.Key)) continue;
                var buffer = BufferFor(format);
                if (buffer.SegmentCount == 0) continue;
                request.SelectedFormatIds.Add(ToFormatId(format));

                var fromUs = DemandFromUs(format);
                var lastSequence = buffer.LastCompletedSequence(fromUs);
                if (lastSequence < 0) continue;

                var range = new BufferedRange()
                {
                    FormatId = ToFormatId(format),
                    EndSegmentIndex = lastSequence
                };

                var startSeq = FirstSequenceOfRun(buffer, fromUs);
                var first = buffer.Get(startSeq);
                if (first == null) continue;
                var startUs = first.StartUs;
                var exactEnd = IsLive ? buffer.ExactEndFromSequence(startSeq) : long.MinValue;
                var end = exactEnd != long.MinValue ? exactEnd : buffer.BufferedEndUs(fromUs);
                var durationUs = end == long.MinValue ? 0 : Math.Max(end - startUs, 0);
                range.StartSegmentIndex = startSeq;
                range.StartTimeMs = startUs / 1000;
                range.DurationMs = durationUs / 1000;

                if (first.Timescale > 0)
                {
                    range.TimeRange = new TimeRange()
                    {
                        StartTicks = first.StartTicks,
                        DurationTicks = UsToTicks(durationUs, first.Timescale),
                        Timescale = first.Timescale
                    };
                }
                request.BufferedRanges.Add(range);
            }

            return request;
        }

        private void ClampToSeekableWindow()
        {
            var lm = _liveMetadata;
            if (lm == null) return;
            lock (_pumpLock)
            {
                if (_seekPendingUs != null) return;
            }

            var minScale = lm.MinSeekableTimescale;
            var maxScale = lm.MaxSeekableTimescale;
            if (minScale <= 0 || maxScale <= 0) return;

            var windowStartUs = lm.MinSeekableTimeTicks * 1_000_000L / minScale;
            var windowEndUs = lm.MaxSeekableTimeTicks * 1_000_000L / maxScale;
            var position = PlaybackPositionUs;

            if (position < windowStartUs)
            {
                LiveLog($"Position {position / 1000}ms fell below the seekable window; moving to its start {windowStartUs / 1000}ms");
                ApplySabrSeek(windowStartUs, position);
                return;
            }

            if (position > windowEndUs + SABR_SEEK_SLACK_US)
            {
                LiveLog($"Position {position / 1000}ms is past the servable head; pulling back to {windowEndUs / 1000}ms");
                ApplySabrSeek(windowEndUs, position);
            }
        }

        private static int FirstSequenceOfRun(SabrTrackBuffer buffer, long fromUs)
        {
            var last = buffer.LastCompletedSequence(fromUs);
            if (last < 0) return Math.Max(buffer.LowestSequence, 1);
            var first = last;
            while (first > 0 && buffer.Get(first - 1)?.IsComplete == true) first--;
            return Math.Max(first, 0);
        }

        private ByteString? ResolvePoToken()
        {
            if (_poTokenResolved) return _poTokenDecoded;
            _poTokenResolved = true;

            var token = _poTokenRaw;
            if (token == null)
            {
                SabrLog("poToken: none set on source");
                return null;
            }

            var decoded = SabrStreamSpec.DecodeBase64Lenient(token);
            _poTokenDecoded = decoded != null ? ByteString.CopyFrom(decoded) : null;
            if (_poTokenDecoded == null)
                Logger.e(TAG, "Po token is not valid base64; requests will be unattested and YouTube will " +
                    "block the session once the attestation grace period expires");

            SabrLog($"poToken: len={token.Length} decodedBytes={_poTokenDecoded?.Length ?? 0} prefix='{(token.Length > 12 ? token.Substring(0, 12) : token)}'");
            return _poTokenDecoded;
        }

        private string AppendRequestNumber(string url)
        {
            var separator = url.Contains('?') ? '&' : '?';
            return $"{url}{separator}rn={Interlocked.Increment(ref _requestNumber)}";
        }
    }
}
