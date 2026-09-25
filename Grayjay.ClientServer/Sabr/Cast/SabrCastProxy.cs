using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Grayjay.ClientServer.Sabr.Proto;
using Grayjay.Desktop.POC;
using Grayjay.Engine.Models.Video.Sources;

namespace Grayjay.ClientServer.Sabr.Cast
{
    public class SabrCastProxy : ISabrSessionListener
    {
        private const string TAG = "SabrCastProxy";

        private const int LIVE_END_STALL_SEGMENTS = 30;
        private const long PREPARE_TIMEOUT_MS = 20_000L;
        private const long SEGMENT_TIMEOUT_MS = 20_000L;
        private const long DEFAULT_SEG_MS = 5_000L;
        private const int LIVE_WINDOW_SEGMENTS = 10;
        private const string LIVE_MIN_PLAYBACK_RATE = "0.97";
        private const string LIVE_MAX_PLAYBACK_RATE = "1.03";
        private const long LIVE_LATENCY_MIN_MS = 4_000L;
        private const int LIVE_RECOVER_SEGMENTS = 3;
        private const long LIVE_RECOVER_MIN_INTERVAL_MS = 10_000L;
        private const long LIVE_MIN_READAHEAD_MS = 20_000L;
        private const long LIVE_RATE_SAMPLE_MS = 30_000L;
        private const long LIVE_PLAYHEAD_JUMP_MS = 1_000L;
        private const long LIVE_HEALTH_LOG_INTERVAL_MS = 5_000L;
        private const long LIVE_WATCHDOG_INTERVAL_MS = 2_000L;
        private const long LIVE_MAX_REPORTED_LAG_US = 12_000_000L;
        private const int LIVE_PRESENTATION_SEGMENTS = 6;
        private const int LIVE_READAHEAD_SEGMENTS = 4;
        private const int LIVE_EVICT_GUARD = 3;
        private const int LIVE_START_SEGMENTS = LIVE_PRESENTATION_SEGMENTS + 2;
        private const int LIVE_MIN_START_SEGMENTS = 3;
        private const int LIVE_MAX_LAG_SEGMENTS = 40;
        private const long LIVE_DEEPEN_TIMEOUT_MS = 8_000L;
        private const long ANCHOR_TOLERANCE_US = 100_000L;
        private const long SEEK_COALESCE_MS = 3_000L;
        private const long MIN_RESTART_INTERVAL_MS = 2_000L;
        private const long FORWARD_GAP_SLACK_US = 30_000_000L;
        private const long VOD_KEEP_BEHIND_US = 60_000_000L;
        private const long LIVE_KEEP_BEHIND_US = 90_000_000L;
        private const long LIVE_HEAD_EXTRAPOLATE_MAX_MS = 60_000L;

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private readonly SabrSession _session;
        private readonly UMPFormat? _video;
        private readonly UMPFormat? _audio;

        public string Id { get; } = Guid.NewGuid().ToString("N");
        public bool IsLive => _session.IsLive;
        public string VideoId => _session.VideoId;
        public UMPFormat? VideoFormat => _video;
        public UMPFormat? AudioFormat => _audio;

        private volatile int _videoFirstSeq = 0;
        private volatile int _audioFirstSeq = 0;
        private volatile int _videoLastSeq = int.MaxValue;
        private volatile int _audioLastSeq = int.MaxValue;
        private long _videoSegMs = 0;
        private long _audioSegMs = 0;
        private SidxTiming? _videoTiming;
        private SidxTiming? _audioTiming;
        private CastTimeline? _videoTimeline;
        private CastTimeline? _audioTimeline;

        private long _durationMs = 0;
        public double DurationSeconds => _durationMs / 1000.0;

        private long _liveAnchorWallMs = 0;
        private long _liveAnchorMediaMs = 0;
        private long _liveEpochMs = 0;

        private string? _lastGoodManifest;

        private readonly SortedDictionary<int, long> _videoAnchors = new();
        private readonly SortedDictionary<int, long> _audioAnchors = new();

        private readonly object _seekLock = new object();
        private long _videoSeekUs = -1;
        private long _audioSeekUs = -1;
        private long _videoSeekAtMs = 0;
        private long _audioSeekAtMs = 0;
        private long _lastSeekUs = long.MinValue;
        private long _lastSeekAtMs = 0;

        private byte[]? _videoInit;
        private byte[]? _audioInit;

        public Action<long?>? OnBackoff { get; set; }
        public Action<Exception>? OnFatalError { get; set; }
        public Action? OnReceiverLost { get; set; }
        public Func<long?>? PlayheadUs { get; set; }

        private long _lastRecoverAtMs = 0;
        private long _rateAnchorUs = long.MinValue;
        private long _rateAnchorAtMs = 0;
        private double _measuredRate = double.NaN;
        private long _lastPlayheadUs = long.MinValue;
        private long _lastPlayheadAtMs = 0;
        private long _lastSlipMs = 0;
        private int _jumps = 0;
        private long _lastHealthLogMs = 0;
        private long _lastManifestAtMs = 0;

        private volatile bool _released = false;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly object _manifestLock = new object();

        private bool IsDead => _released || _session.IsReleased || _session.FatalError != null;

        private readonly LiveCastPlanner _planner = new LiveCastPlanner(new LiveCastPlanner.Config()
        {
            WindowSegments = LIVE_WINDOW_SEGMENTS,
            MaxLagSegments = LIVE_MAX_LAG_SEGMENTS,
            MinStartSegments = LIVE_MIN_START_SEGMENTS
        });

        public SabrCastProxy(SabrSession session, UMPFormat? video, UMPFormat? audio)
        {
            _session = session;
            _video = video;
            _audio = audio;
            _session.KeepBehindUs = IsLive ? LiveKeepBehindUs() : VOD_KEEP_BEHIND_US;
            if (IsLive) _session.MinReadaheadMs = LIVE_MIN_READAHEAD_MS;
            _session.SetListener(this);
        }

        public void OnLiveMetadata(LiveMetadata metadata) => _planner.OnLiveEdge(metadata.HeadSequenceNumber);
        public void OnFormatInitialization(FormatInitializationMetadata metadata) { }
        public void OnSessionError(Exception error)
        {
            var fatal = _session.FatalError;
            if (fatal == null || _released) return;
            Logger.e(TAG, "SABR cast session failed fatally", fatal);
            OnFatalError?.Invoke(fatal);
        }
        void ISabrSessionListener.OnBackoff(long delayMs) => OnBackoff?.Invoke(delayMs);
        void ISabrSessionListener.OnBackoffEnded() => OnBackoff?.Invoke(null);

        private static Task Delay(long ms, CancellationToken token) => Task.Delay(TimeSpan.FromMilliseconds(ms), token).ContinueWith(_ => { });

        private static async Task WaitChanged(SabrTrackBuffer buffer, long ms)
        {
            try { await buffer.ChangedTask.WaitAsync(TimeSpan.FromMilliseconds(ms)); } catch (TimeoutException) { }
        }

        public async Task<bool> PrepareAsync(long resumeUs = 0)
        {
            if (_video == null && _audio == null) return false;

            _session.Start();
            _durationMs = _session.DurationUs > 0 ? _session.DurationUs / 1000 : 0;

            var from = IsLive ? 0L : Math.Max(resumeUs, 0);
            var tailGuardUs = 4 * DEFAULT_SEG_MS * 1000;
            if (!IsLive && _session.DurationUs > 0)
                from = Math.Min(from, Math.Max(_session.DurationUs - tailGuardUs, 0));

            if (!await DemandFromAsync(from)) return false;

            if (IsLive && !await AwaitLiveMetadataAsync())
            {
                Logger.w(TAG, "No live metadata within the prepare timeout; refusing to cast");
                return false;
            }

            await ReadTimingsAsync();

            if (!IsLive)
            {
                if (from > 0 && FirstSequences() == null)
                {
                    Logger.w(TAG, $"Could not establish the segment numbering from {from / 1000}ms; restarting at 0");
                    from = 0;
                    _session.Restart(0, true);
                    if (!await DemandFromAsync(0)) return false;
                    await ReadTimingsAsync();
                }

                var firstSeqs = FirstSequences();
                if (firstSeqs == null)
                {
                    Logger.e(TAG, "Could not establish the segment numbering even from 0; refusing to cast");
                    return false;
                }
                _videoFirstSeq = firstSeqs.Value.Video;
                _audioFirstSeq = firstSeqs.Value.Audio;
            }
            else
            {
                if (_video != null) _videoFirstSeq = Math.Max(_session.BufferFor(_video).LowestSequence, 0);
                if (_audio != null) _audioFirstSeq = Math.Max(_session.BufferFor(_audio).LowestSequence, 0);
            }
            if (_video != null) _videoLastSeq = LastSeqFor(_video, _videoFirstSeq, _videoTiming);
            if (_audio != null) _audioLastSeq = LastSeqFor(_audio, _audioFirstSeq, _audioTiming);

            if (IsLive && _session.LiveMetadata is { } lm)
            {
                var segMs = Math.Max(Math.Max(_videoSegMs, _audioSegMs), 1);
                var minMs = _session.MediaBaseUs / 1000;
                var edgeMs = Math.Max(lm.HeadSequenceTimeMs - LIVE_START_SEGMENTS * segMs, minMs);
                var ends = Roles().Select(x => _session.BufferFor(x).BufferedEndFromFrontUs()).ToList();
                var landedMs = (ends.Count == 0 || ends.Any(x => x == long.MinValue)) ? 0L : ends.Min() / 1000;
                if (edgeMs - landedMs > segMs)
                {
                    var edgeUs = edgeMs * 1000;
                    if (_video != null) _session.SetDemand(SabrSession.ROLE_VIDEO, _video, edgeUs);
                    if (_audio != null) _session.SetDemand(SabrSession.ROLE_AUDIO, _audio, edgeUs);
                    _session.SetPlaybackPosition(edgeUs);
                    _session.Restart(edgeUs, true);
                    if (_video != null)
                    {
                        var buffer = _session.BufferFor(_video);
                        if (await buffer.AwaitAnnouncedAsync(-1, TimeSpan.FromMilliseconds(PREPARE_TIMEOUT_MS)) == null) return false;
                        _videoFirstSeq = Math.Max(buffer.LowestSequence, 0);
                        _videoLastSeq = LastSeqFor(_video, _videoFirstSeq, _videoTiming);
                    }
                    if (_audio != null)
                    {
                        var buffer = _session.BufferFor(_audio);
                        if (await buffer.AwaitAnnouncedAsync(-1, TimeSpan.FromMilliseconds(PREPARE_TIMEOUT_MS)) == null) return false;
                        _audioFirstSeq = Math.Max(buffer.LowestSequence, 0);
                        _audioLastSeq = LastSeqFor(_audio, _audioFirstSeq, _audioTiming);
                    }
                }
            }

            if (IsLive)
            {
                _videoTimeline = new CastTimeline(1000);
                _audioTimeline = new CastTimeline(1000);

                if (_session.LiveMetadata is { } lm2) _planner.OnLiveEdge(lm2.HeadSequenceNumber);
                _session.PlayheadProvider = ReportedPlayheadUs;

                if (!await AwaitLiveHeadAsync())
                {
                    Logger.w(TAG, "No live segment completed within the prepare timeout");
                    return false;
                }

                var range = LiveRange();
                if (range != null)
                {
                    if (_video != null) FillTimeline(SabrSession.ROLE_VIDEO, range.Value);
                    if (_audio != null) FillTimeline(SabrSession.ROLE_AUDIO, range.Value);
                }

                if (_video != null && (_videoTimeline?.IsEmpty ?? true))
                {
                    Logger.w(TAG, "Live video window is empty after prepare");
                    return false;
                }
                if (_audio != null && (_audioTimeline?.IsEmpty ?? true))
                {
                    Logger.w(TAG, "Live audio window is empty after prepare");
                    return false;
                }

                var landed = Roles().Select(x => _session.BufferFor(x).BufferedExactEndUs()).Where(x => x != long.MinValue).ToList();
                var landedMs = landed.Count > 0 ? landed.Min() / 1000 : 0;
                if (landedMs <= 0)
                {
                    Logger.w(TAG, "Live prepare could not establish a media anchor");
                    return false;
                }
                _liveAnchorMediaMs = landedMs;
                _liveAnchorWallMs = NowMs();

                var starts = new[] { _videoTimeline, _audioTimeline }.Where(x => x != null && !x.IsEmpty)
                    .Select(x => x!.StartUs(x.FirstNumber)).Where(x => x != null).Select(x => x!.Value).ToList();
                _liveEpochMs = starts.Count > 0 ? starts.Min() / 1000 : 0;

                SabrSession.LiveLog($"PREPARE landed anchorMediaMs={_liveAnchorMediaMs} epochMs={_liveEpochMs} segMs={SegmentMs()}");
                StartLiveWatchdog();
                return true;
            }

            if (_video != null) _videoTimeline = BuildVodTimeline(_video, _videoFirstSeq, _videoLastSeq, _videoTiming);
            if (_audio != null) _audioTimeline = BuildVodTimeline(_audio, _audioFirstSeq, _audioLastSeq, _audioTiming);

            if (_videoTimeline is { IsEmpty: false }) _videoLastSeq = _videoTimeline.LastNumber;
            if (_audioTimeline is { IsEmpty: false }) _audioLastSeq = _audioTimeline.LastNumber;

            var timelineMs = new[] { _videoTimeline, _audioTimeline }.Where(x => x != null && !x.IsEmpty).Select(x => x!.TotalUs() / 1000).DefaultIfEmpty(0).Max();
            if (timelineMs > 0) _durationMs = timelineMs;
            if (_durationMs <= 0)
                _durationMs = Roles().Select(x => _session.FormatInitializationFor(x)?.EndTimeMs ?? 0).DefaultIfEmpty(0).Max();
            return _durationMs > 0;
        }

        private IEnumerable<UMPFormat> Roles()
        {
            if (_video != null) yield return _video;
            if (_audio != null) yield return _audio;
        }

        private async Task<SabrTrackBuffer?> AwaitRoleAsync(UMPFormat format)
        {
            var buffer = _session.BufferFor(format);
            if (await buffer.AwaitAnnouncedAsync(-1, TimeSpan.FromMilliseconds(PREPARE_TIMEOUT_MS), _cts.Token) != null) return buffer;

            if (_session.FatalError != null) throw _session.FatalError;

            var requested = Roles().Select(x => x.Key).ToHashSet();
            var foreign = _session.ObservedFormatKeys().Where(x => !requested.Contains(x) && _session.BufferFor(x).FirstAtOrAfter(-1) != null).ToList();
            if (foreign.Count > 0)
                throw new SabrFormatSubstitutedException(
                    $"Requested itag={format.Itag} (lmt={format.LastModified}) but the server returned " +
                    string.Join(", ", foreign.Select(x => $"itag={x.Itag} lmt={x.LastModified}")) +
                    ". The plugin's formats are out of sync with the app.");
            return null;
        }

        private async Task<bool> AwaitLiveMetadataAsync()
        {
            var deadline = NowMs() + PREPARE_TIMEOUT_MS;
            while (NowMs() < deadline)
            {
                if (IsDead) return false;
                if (_session.LiveMetadata != null) return true;
                _session.WakePump();
                await Delay(50, _cts.Token);
            }
            return _session.LiveMetadata != null;
        }

        private async Task<bool> AwaitLiveHeadAsync()
        {
            var roles = Roles().ToList();
            if (roles.Count == 0) return false;

            var hardDeadline = NowMs() + PREPARE_TIMEOUT_MS;
            var minSegments = LIVE_EVICT_GUARD + 1;

            while (NowMs() < hardDeadline)
            {
                if (IsDead) return false;
                if (roles.All(x => CompletedCount(x) >= minSegments)) break;
                _session.WakePump();
                await Delay(50, _cts.Token);
            }
            if (roles.Any(x => CompletedCount(x) < minSegments)) return false;

            var deepenDeadline = NowMs() + LIVE_DEEPEN_TIMEOUT_MS;
            while (NowMs() < Math.Min(deepenDeadline, hardDeadline))
            {
                if (IsDead) return false;
                if (roles.All(x => CompletedCount(x) >= LIVE_START_SEGMENTS)) return true;
                _session.WakePump();
                await Delay(100, _cts.Token);
            }

            var depth = roles.Min(CompletedCount);
            if (depth < LIVE_START_SEGMENTS)
                Logger.w(TAG, $"Live prepare starting with a shallow window ({depth} segments)");
            return true;
        }

        private int CompletedCount(UMPFormat format)
        {
            var buffer = _session.BufferFor(format);
            var head = buffer.LastCompletedFromFront();
            var low = buffer.LowestSequence;
            if (head < 0 || low < 0 || head < low) return 0;
            return head - low + 1;
        }

        private int? DeriveFirstSeq(SabrTrackBuffer buffer, SidxTiming? timing, long demandedFromUs)
        {
            var lowest = Math.Max(buffer.LowestSequence, 0);
            var front = buffer.FirstAtOrAfter(-1);

            var frontIsStart = front != null && front.StartUs < Math.Max(front.DurationUs, 1) / 2;
            if (timing == null) return (demandedFromUs <= 0 && frontIsStart) ? lowest : null;

            var index = front != null ? timing.IndexOfStartUs(front.StartUs) : null;
            if (front == null || index == null)
            {
                Logger.w(TAG, $"Could not anchor the numbering (seq={front?.SequenceNumber}, startMs={front?.StartUs / 1000})");
                return (demandedFromUs <= 0 && frontIsStart) ? lowest : null;
            }

            var firstSeq = front.SequenceNumber - index.Value;
            if (firstSeq != lowest)
                SabrSession.SabrLog($"Anchored firstSeq={firstSeq} from seq={front.SequenceNumber} at index={index} (buffer's lowest is {lowest})");
            return firstSeq;
        }

        private async Task ReadTimingsAsync()
        {
            if (_video != null)
            {
                var buffer = _session.BufferFor(_video);
                if (!IsLive) _videoTiming = await ParseInitTimingAsync(_video, buffer);
                Interlocked.Exchange(ref _videoSegMs, MeasureSegMs(_video, buffer, _videoTiming));
            }
            if (_audio != null)
            {
                var buffer = _session.BufferFor(_audio);
                if (!IsLive) _audioTiming = await ParseInitTimingAsync(_audio, buffer);
                Interlocked.Exchange(ref _audioSegMs, MeasureSegMs(_audio, buffer, _audioTiming));
            }
            _planner.SegmentMs = SegmentMs();
            if (IsLive) _session.KeepBehindUs = LiveKeepBehindUs();
        }

        private long LiveKeepBehindUs()
        {
            var segMs = Math.Max(Math.Max(Interlocked.Read(ref _videoSegMs), Interlocked.Read(ref _audioSegMs)), 1);
            var windowUs = (LIVE_WINDOW_SEGMENTS + 2) * segMs * 1000;
            return Math.Max(LIVE_KEEP_BEHIND_US, windowUs + LIVE_MAX_REPORTED_LAG_US);
        }

        private long _lastDemandedFromUs = 0;

        private (int Video, int Audio)? FirstSequences()
        {
            var v = 0;
            var a = 0;
            if (_video != null)
            {
                var d = DeriveFirstSeq(_session.BufferFor(_video), _videoTiming, _lastDemandedFromUs);
                if (d == null) return null;
                v = d.Value;
            }
            if (_audio != null)
            {
                var d = DeriveFirstSeq(_session.BufferFor(_audio), _audioTiming, _lastDemandedFromUs);
                if (d == null) return null;
                a = d.Value;
            }
            return (v, a);
        }

        private async Task<bool> DemandFromAsync(long fromUs)
        {
            _lastDemandedFromUs = fromUs;
            _session.SetPlaybackPosition(fromUs);
            if (_video != null) _session.SetDemand(SabrSession.ROLE_VIDEO, _video, fromUs);
            if (_audio != null) _session.SetDemand(SabrSession.ROLE_AUDIO, _audio, fromUs);

            if (_video != null && await AwaitRoleAsync(_video) == null) return false;
            if (_audio != null && await AwaitRoleAsync(_audio) == null) return false;
            return true;
        }

        private CastTimeline? BuildVodTimeline(UMPFormat format, int firstSeq, int lastSeq, SidxTiming? timing)
        {
            if (timing != null && timing.SegmentCount > 0)
                return CastTimeline.FromSidx(firstSeq, timing).TruncateTo(lastSeq);

            var meta = _session.FormatInitializationFor(format);
            var endSeq = meta?.EndSegmentNumber ?? 0;
            var endMs = meta?.EndTimeMs ?? 0;
            var segMs = format.Key == _video?.Key ? _videoSegMs : _audioSegMs;
            if (endSeq > 0 && segMs > 0)
            {
                Logger.w(TAG, $"No sidx for itag={format.Itag}, building uniform timeline from endSegment={endSeq} endMs={endMs}");
                return CastTimeline.Uniform(firstSeq, endSeq, segMs, endMs);
            }

            Logger.w(TAG, $"No sidx and no endSegmentNumber for itag={format.Itag}; falling back to duration-based template");
            return null;
        }

        private long ReceiverPlayheadUs()
        {
            if (!IsLive) return long.MinValue;
            var reported = PlayheadUs?.Invoke();
            if (reported == null || reported.Value < 0) return long.MinValue;

            var windowStartUs = WindowFloorUs();
            if (windowStartUs <= 0) return long.MinValue;
            return windowStartUs + reported.Value;
        }

        private long ReportedPlayheadUs()
        {
            var head = _session.LiveMetadata?.HeadSequenceTimeMs ?? 0;
            if (head <= 0) return ReceiverPlayheadUs();
            var sinceMetadataMs = Math.Clamp(NowMs() - _session.LiveMetadataAtMs, 0, LIVE_HEAD_EXTRAPOLATE_MAX_MS);
            var headUs = (head + sinceMetadataMs) * 1000L;

            var receiver = ReceiverPlayheadUs();
            var playhead = (receiver != long.MinValue && receiver <= headUs) ? receiver : long.MinValue;
            var floor = headUs - LIVE_MAX_REPORTED_LAG_US;

            if (playhead == long.MinValue) return floor;
            return Math.Min(Math.Max(playhead, floor), headUs);
        }

        private void PublishReceiverPlayhead()
        {
            var playhead = ReceiverPlayheadUs();
            if (playhead != long.MinValue) _session.SetPlaybackPosition(playhead);
        }

        private void NoteReceiverRequest(int role, int sequence)
        {
            if (!IsLive) return;

            var format = FormatFor(role);
            if (format == null) return;
            var head = _session.BufferFor(format).HighestSequence;
            if (sequence < 0 || (head >= 0 && sequence > head + 2 * LIVE_READAHEAD_SEGMENTS))
            {
                Logger.w(TAG, $"Ignoring an implausible receiver request role={role} seq={sequence} (head={head})");
                return;
            }

            _planner.NoteRequest(role, sequence);
            PublishReceiverPlayhead();
            _session.WakePump();
        }

        private LiveCastPlanner.Track? TrackFor(int role)
        {
            var format = FormatFor(role);
            if (format == null) return null;
            var run = _session.BufferFor(format).PublishableRun();
            if (run == null) return null;
            return new LiveCastPlanner.Track(run.Value.First, run.Value.Last);
        }

        private bool FillTimeline(int role, LiveCastPlanner.Range range)
        {
            var format = FormatFor(role);
            var timeline = TimelineFor(role);
            if (format == null || timeline == null) return false;
            var buffer = _session.BufferFor(format);

            for (var sequence = range.First; sequence <= range.Last; sequence++)
            {
                var segment = buffer.Get(sequence);
                if (segment == null || !segment.IsComplete || !segment.DurationExact) return false;

                var startMs = (segment.StartUs + 500) / 1000;
                var next = buffer.Get(sequence + 1);
                var endMs = next != null ? (next.StartUs + 500) / 1000 : startMs + (segment.DurationUs + 500) / 1000;
                timeline.Put(sequence, startMs, Math.Max(endMs - startMs, 1));
            }
            return true;
        }

        private CastTimeline? TimelineFor(int role) => role == SabrSession.ROLE_VIDEO ? _videoTimeline : _audioTimeline;

        private static bool IsWebmMime(string mime)
        {
            var m = mime.ToLowerInvariant();
            return m.Contains("webm") || m.Contains("matroska");
        }

        private async Task<SidxTiming?> ParseInitTimingAsync(UMPFormat format, SabrTrackBuffer buffer)
        {
            var init = await AwaitInitAsync(buffer);
            if (init == null) return null;
            var bytes = init.ToByteArray();

            var webm = IsWebmMime(format.ContainerMimeType);
            SidxTiming? timing = null;
            try
            {
                timing = webm ? WebmCuesParser.Parse(bytes) : Mp4SidxParser.Parse(bytes);
            }
            catch (Exception ex)
            {
                Logger.w(TAG, $"Failed to parse the {(webm ? "WebM Cues" : "MP4 sidx")} for itag={format.Itag}", ex);
            }

            if (timing == null)
                Logger.w(TAG, $"No {(webm ? "Cues" : "sidx")} in the init for itag={format.Itag}; falling back to a uniform timeline");
            else
                SabrSession.SabrLog($"Timeline for itag={format.Itag}: {timing.SegmentCount} segments @{timing.Timescale} from {(webm ? "WebM Cues" : "MP4 sidx")}");
            return timing;
        }

        private int LastSeqFor(UMPFormat format, int firstSeq, SidxTiming? timing)
        {
            if (IsLive) return int.MaxValue;

            var end = _session.FormatInitializationFor(format)?.EndSegmentNumber ?? 0;
            if (timing == null) return end > 0 ? end : int.MaxValue;

            var fromTiming = firstSeq + timing.SegmentCount - 1;
            if (end > 0 && end != fromTiming)
                Logger.w(TAG, $"Segment count disagrees with the server for itag={format.Itag}: index says last={fromTiming} (first={firstSeq} + {timing.SegmentCount}), server says last={end}");
            return end > 0 ? Math.Min(end, fromTiming) : fromTiming;
        }

        private long MeasureSegMs(UMPFormat format, SabrTrackBuffer buffer, SidxTiming? timing)
        {
            var meta = _session.FormatInitializationFor(format);
            var endMs = meta?.EndTimeMs ?? 0;

            if (timing != null && timing.SegmentCount > 0 && endMs > 0)
                return Math.Max(endMs / timing.SegmentCount, 1);

            if (IsLive)
            {
                var lm = _session.LiveMetadata;
                var front = buffer.FirstAtOrAfter(-1);
                if (lm != null && front != null)
                {
                    var segments = lm.HeadSequenceNumber - front.SequenceNumber;
                    var spanMs = lm.HeadSequenceTimeMs - front.StartUs / 1000;
                    if (segments > 0 && spanMs > 0) return Math.Max(spanMs / segments, 1);
                }
            }

            var first = buffer.FirstAtOrAfter(-1);
            if (first != null && first.DurationExact && first.DurationUs > 0) return first.DurationUs / 1000;

            var endSeq = meta?.EndSegmentNumber ?? 0;
            if (endSeq > 0 && endMs > 0) return Math.Max(endMs / (endSeq + 1), 1);
            return DEFAULT_SEG_MS;
        }

        private int _lastLiveHeadSeq = -1;
        private long _lastLiveHeadAtMs = 0;
        private volatile bool _liveEndedLatched = false;

        private bool LiveEnded()
        {
            if (_liveEndedLatched) return true;

            var lm = _session.LiveMetadata;
            if (lm == null) return false;
            var head = lm.HeadSequenceNumber;
            var now = NowMs();
            var segMs = SegmentMs();
            var stallMs = Math.Clamp(segMs * LIVE_END_STALL_SEGMENTS, 120_000L, 300_000L);

            var freshMs = Math.Max(2 * segMs, 5_000L);
            if (now - _session.LiveMetadataAtMs > freshMs)
            {
                _lastLiveHeadSeq = head;
                _lastLiveHeadAtMs = now;
                return false;
            }

            if (head != _lastLiveHeadSeq)
            {
                _lastLiveHeadSeq = head;
                _lastLiveHeadAtMs = now;
                return false;
            }
            if (_lastLiveHeadAtMs == 0)
            {
                _lastLiveHeadAtMs = now;
                return false;
            }
            var ended = now - _lastLiveHeadAtMs > stallMs;
            if (ended) _liveEndedLatched = true;
            return ended;
        }

        public double? ServableStartSeconds()
        {
            if (!IsLive) return null;
            var segMs = SegmentMs();
            var depthMs = LiveWindowDepthMs(_planner.Window);

            var targetMs = LIVE_PRESENTATION_SEGMENTS * segMs;
            var startMs = Math.Clamp(depthMs - targetMs, 2 * segMs, Math.Max(2 * segMs, depthMs - segMs));
            return startMs / 1000.0;
        }

        private void GuardReceiverCushion(long publishedEndMs, long segMs)
        {
            if (!IsLive || _liveEndedLatched) return;
            var playheadUs = ReceiverPlayheadUs();
            if (playheadUs == long.MinValue) return;

            var now = NowMs();
            var cushionMs = publishedEndMs - playheadUs / 1000;
            var targetMs = LIVE_PRESENTATION_SEGMENTS * segMs;

            NoteReceiverPlayhead(playheadUs, now);
            LogCastHealth(publishedEndMs, cushionMs, targetMs, playheadUs, now);
            SampleReceiverRate(playheadUs, now);

            if (cushionMs < LIVE_RECOVER_SEGMENTS * segMs)
            {
                if (now - _lastRecoverAtMs < LIVE_RECOVER_MIN_INTERVAL_MS) return;
                _lastRecoverAtMs = now;
                _rateAnchorUs = long.MinValue;

                Logger.w(TAG, $"Receiver has {cushionMs}ms of media left; seeking it back in");
                OnReceiverLost?.Invoke();
            }
        }

        private long SegmentMs() => Math.Max(Math.Max(Interlocked.Read(ref _videoSegMs), Interlocked.Read(ref _audioSegMs)), 1);

        private void StartLiveWatchdog()
        {
            if (!IsLive) return;
            _ = Task.Run(async () =>
            {
                while (!_released)
                {
                    await Delay(LIVE_WATCHDOG_INTERVAL_MS, _cts.Token);
                    if (_released) break;
                    try { TickLiveWatchdog(); }
                    catch (Exception ex) { Logger.w(TAG, "Live watchdog tick failed", ex); }
                }
            });
        }

        private void TickLiveWatchdog()
        {
            lock (_manifestLock)
            {
                if (_released || !IsLive || _liveEndedLatched) return;
                var shared = _planner.Window;
                if (shared == null) return;
                var publishedEndMs = PublishedEndMs(shared.Value);
                if (publishedEndMs <= 0) return;
                GuardReceiverCushion(publishedEndMs, SegmentMs());
            }
        }

        private long PublishedEndMs(LiveCastPlanner.Range range)
        {
            var ends = new[] { _videoTimeline, _audioTimeline }.Where(x => x != null)
                .Select(tl => tl!.StartUs(range.Last) is { } s ? s + (tl.DurationUs(range.Last) ?? 0) : (long?)null)
                .Where(x => x != null).Select(x => x!.Value).ToList();
            return ends.Count > 0 ? ends.Min() / 1000 : 0;
        }

        private void NoteReceiverPlayhead(long playheadUs, long nowMs)
        {
            var prevUs = _lastPlayheadUs;
            var prevAtMs = _lastPlayheadAtMs;
            _lastPlayheadUs = playheadUs;
            _lastPlayheadAtMs = nowMs;
            if (prevUs == long.MinValue) return;

            var wallMs = nowMs - prevAtMs;
            if (wallMs <= 0) return;

            _lastSlipMs = (playheadUs - prevUs) / 1000 - wallMs;
            if (Math.Abs(_lastSlipMs) < LIVE_PLAYHEAD_JUMP_MS) return;

            _jumps++;
            Logger.w(TAG, $"Receiver playhead moved {_lastSlipMs}ms more than {wallMs}ms of wall time allows (jump #{_jumps}); it did not get there by playing");
        }

        private void SampleReceiverRate(long playheadUs, long nowMs)
        {
            if (_rateAnchorUs == long.MinValue)
            {
                _rateAnchorUs = playheadUs;
                _rateAnchorAtMs = nowMs;
                return;
            }
            var wallMs = nowMs - _rateAnchorAtMs;
            if (wallMs < LIVE_RATE_SAMPLE_MS) return;

            var mediaMs = (playheadUs - _rateAnchorUs) / 1000;
            _rateAnchorUs = playheadUs;
            _rateAnchorAtMs = nowMs;
            _measuredRate = mediaMs < 0 ? double.NaN : (double)mediaMs / wallMs;
        }

        private void LogCastHealth(long publishedEndMs, long cushionMs, long targetMs, long playheadUs, long now)
        {
            if (now - _lastHealthLogMs < LIVE_HEALTH_LOG_INTERVAL_MS) return;
            _lastHealthLogMs = now;

            var head = _session.LiveMetadata?.HeadSequenceTimeMs ?? 0;
            var edgeLagMs = head > 0 ? head - publishedEndMs : -1;
            var rate = double.IsNaN(_measuredRate) ? "?" : _measuredRate.ToString("F4", CultureInfo.InvariantCulture);
            SabrSession.LiveLog($"cast health: cushion={cushionMs}ms/{targetMs}ms edgeLag={edgeLagMs}ms head={publishedEndMs}ms receiver={playheadUs / 1000}ms " +
                $"rate={rate} slip={_lastSlipMs}ms jumps={_jumps} sinceManifest={(_lastManifestAtMs == 0 ? -1 : now - _lastManifestAtMs)}ms");
        }

        private bool ReceiverIsStuck()
        {
            if (!IsLive || _liveEndedLatched) return false;
            var playhead = ReceiverPlayheadUs();
            if (playhead == long.MinValue) return false;

            var lm = _session.LiveMetadata;
            if (lm == null || lm.MinSeekableTimescale <= 0) return false;
            var windowStartUs = lm.MinSeekableTimeTicks * 1_000_000L / lm.MinSeekableTimescale;
            return playhead < windowStartUs;
        }

        public string? BuildManifest(string videoInitUrl, string videoMediaUrl, string audioInitUrl, string audioMediaUrl, string? timeUrl = null)
        {
            lock (_manifestLock)
            {
                if (ReceiverIsStuck())
                {
                    Logger.w(TAG, "The receiver has fallen out of the bottom of the DVR window; asking it to seek");
                    OnReceiverLost?.Invoke();
                    return null;
                }

                if (IsLive) _lastManifestAtMs = NowMs();

                LiveCastPlanner.Range? shared = null;
                if (IsLive)
                {
                    shared = LiveRange();
                    if (shared == null) return _lastGoodManifest;
                    var filledV = _video == null || FillTimeline(SabrSession.ROLE_VIDEO, shared.Value);
                    var filledA = _audio == null || FillTimeline(SabrSession.ROLE_AUDIO, shared.Value);
                    if (!filledV || !filledA) return _lastGoodManifest;
                }

                LiveWindow? videoWindow = null, audioWindow = null;
                if (IsLive && _video != null)
                {
                    videoWindow = LiveWindowFor(SabrSession.ROLE_VIDEO, shared!.Value);
                    if (videoWindow == null) return _lastGoodManifest;
                }
                if (IsLive && _audio != null)
                {
                    audioWindow = LiveWindowFor(SabrSession.ROLE_AUDIO, shared!.Value);
                    if (audioWindow == null) return _lastGoodManifest;
                }

                var ended = IsLive && LiveEnded();
                var livePtoMs = _liveEpochMs;

                string? videoTemplate = null, audioTemplate = null;
                if (_video != null)
                {
                    videoTemplate = SegmentTemplate(SabrSession.ROLE_VIDEO, _videoSegMs, _videoFirstSeq, videoInitUrl, videoMediaUrl, livePtoMs, videoWindow);
                    if (videoTemplate == null) return _lastGoodManifest;
                }
                if (_audio != null)
                {
                    audioTemplate = SegmentTemplate(SabrSession.ROLE_AUDIO, _audioSegMs, _audioFirstSeq, audioInitUrl, audioMediaUrl, livePtoMs, audioWindow);
                    if (audioTemplate == null) return _lastGoodManifest;
                }

                var sb = new StringBuilder();
                sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");

                if (IsLive)
                {
                    var segMs = SegmentMs();
                    var publishedEndMs = shared != null ? PublishedEndMs(shared.Value) : 0;
                    var depthMs = LiveWindowDepthMs(shared);
                    var availIso = ToIso8601(AvailabilityStartMs(publishedEndMs));
                    var presentationDelayMs = Math.Clamp(LIVE_PRESENTATION_SEGMENTS * segMs, segMs, Math.Max(segMs, depthMs - segMs));
                    var latencyMinMs = Math.Min(Math.Max(LIVE_LATENCY_MIN_MS, presentationDelayMs - 2 * segMs), presentationDelayMs);

                    sb.Append("<MPD xmlns=\"urn:mpeg:dash:schema:mpd:2011\" profiles=\"urn:mpeg:dash:profile:isoff-live:2011\" ");
                    if (ended)
                    {
                        var endMs = Math.Max(publishedEndMs - _liveEpochMs, 0);
                        sb.Append($"type=\"static\" availabilityStartTime=\"{availIso}\" ");
                        if (endMs > 0) sb.Append($"mediaPresentationDuration=\"{ToIsoDuration(endMs)}\" ");
                        sb.Append("minBufferTime=\"PT4S\">\n");
                    }
                    else
                    {
                        sb.Append($"type=\"dynamic\" availabilityStartTime=\"{availIso}\" ");
                        sb.Append($"minimumUpdatePeriod=\"{ToIsoDuration(segMs)}\" ");
                        sb.Append($"timeShiftBufferDepth=\"{ToIsoDuration(depthMs)}\" ");
                        sb.Append($"suggestedPresentationDelay=\"{ToIsoDuration(presentationDelayMs)}\" ");
                        sb.Append("minBufferTime=\"PT4S\">\n");

                        sb.Append("<ServiceDescription id=\"0\">\n");
                        sb.Append($"<Latency target=\"{presentationDelayMs}\" min=\"{latencyMinMs}\" max=\"{presentationDelayMs}\"/>\n");
                        sb.Append($"<PlaybackRate min=\"{LIVE_MIN_PLAYBACK_RATE}\" max=\"{LIVE_MAX_PLAYBACK_RATE}\"/>\n");
                        sb.Append("</ServiceDescription>\n");
                    }
                }
                else
                {
                    sb.Append("<MPD xmlns=\"urn:mpeg:dash:schema:mpd:2011\" profiles=\"urn:mpeg:dash:profile:isoff-main:2011\" ");
                    sb.Append($"type=\"static\" mediaPresentationDuration=\"{ToIsoDuration(_durationMs)}\" minBufferTime=\"PT10S\">\n");
                }
                sb.Append($"<Period id=\"0\" start=\"{ToIsoDuration(0)}\">\n");

                if (_video != null)
                {
                    sb.Append($"<AdaptationSet mimeType=\"{_video.ContainerMimeType}\" contentType=\"video\" segmentAlignment=\"true\" startWithSAP=\"1\">\n");
                    sb.Append($"<Representation id=\"v\" codecs=\"{_video.Codecs}\" bandwidth=\"{Math.Max(_video.Bitrate, 1)}\" width=\"{_video.Width}\" height=\"{_video.Height}\"");
                    if (_video.Fps > 0) sb.Append($" frameRate=\"{_video.Fps}\"");
                    sb.Append(">\n");
                    sb.Append(videoTemplate);
                    sb.Append("</Representation>\n</AdaptationSet>\n");
                }

                if (_audio != null)
                {
                    var lang = string.IsNullOrEmpty(_audio.Language) || _audio.Language == "Unknown" ? "und" : _audio.Language;
                    sb.Append($"<AdaptationSet mimeType=\"{_audio.ContainerMimeType}\" contentType=\"audio\" lang=\"{lang}\" segmentAlignment=\"true\">\n");
                    sb.Append($"<Representation id=\"a\" codecs=\"{_audio.Codecs}\" bandwidth=\"{Math.Max(_audio.Bitrate, 1)}\"");
                    if (_audio.AudioSampleRate > 0) sb.Append($" audioSamplingRate=\"{_audio.AudioSampleRate}\"");
                    sb.Append(">\n");
                    if (_audio.AudioChannels > 0)
                        sb.Append($"<AudioChannelConfiguration schemeIdUri=\"urn:mpeg:dash:23003:3:audio_channel_configuration:2011\" value=\"{_audio.AudioChannels}\"/>\n");
                    sb.Append(audioTemplate);
                    sb.Append("</Representation>\n</AdaptationSet>\n");
                }

                sb.Append("</Period>\n");

                if (IsLive)
                {
                    if (timeUrl != null)
                        sb.Append($"<UTCTiming schemeIdUri=\"urn:mpeg:dash:utc:http-iso:2014\" value=\"{timeUrl}\"/>\n");
                    else
                        sb.Append($"<UTCTiming schemeIdUri=\"urn:mpeg:dash:utc:direct:2014\" value=\"{ToIso8601(NowMs())}\"/>\n");
                }

                sb.Append("</MPD>\n");
                var manifest = sb.ToString();

                if (IsLive && shared != null)
                {
                    if (_planner.Commit(shared.Value))
                    {
                        Logger.w(TAG, $"The window moved to {shared}, away from what the receiver holds; seeking it back in");
                        OnReceiverLost?.Invoke();
                    }

                    var retain = _planner.RetainFrom();
                    if (retain > 0)
                    {
                        _videoTimeline?.DropBefore(retain);
                        _audioTimeline?.DropBefore(retain);
                        foreach (var anchors in new[] { _videoAnchors, _audioAnchors })
                            lock (anchors)
                                foreach (var key in anchors.Keys.Where(x => x < retain).ToList())
                                    anchors.Remove(key);
                    }

                    SabrSession.LiveLog($"manifest window startNumber={shared.Value.First} head={shared.Value.Last} count={shared.Value.Last - shared.Value.First + 1} edge={_planner.LiveEdge} retain={retain}");
                }

                _lastGoodManifest = manifest;
                return manifest;
            }
        }

        private long LiveWindowDepthMs(LiveCastPlanner.Range? range)
        {
            var depths = new List<long>();
            if (range != null)
            {
                foreach (var role in new[] { SabrSession.ROLE_VIDEO, SabrSession.ROLE_AUDIO })
                {
                    var timeline = TimelineFor(role);
                    if (timeline == null) continue;
                    var startUs = timeline.StartUs(range.Value.First);
                    var lastStart = timeline.StartUs(range.Value.Last);
                    if (startUs == null || lastStart == null) continue;
                    var endUs = lastStart.Value + (timeline.DurationUs(range.Value.Last) ?? 0);
                    depths.Add((endUs - startUs.Value) / 1000);
                }
            }
            return depths.Count > 0 ? Math.Max(depths.Min(), 1) : SegmentMs();
        }

        private string? SegmentTemplate(int role, long segMs, int startNumber, string initUrl, string mediaUrl, long livePtoMs, LiveWindow? liveWindow)
        {
            var media = mediaUrl.Contains('?') ? $"{mediaUrl}&amp;n=$Number$" : $"{mediaUrl}?n=$Number$";

            if (IsLive)
            {
                if (liveWindow == null) return null;
                var pto = livePtoMs > 0 ? $" presentationTimeOffset=\"{livePtoMs}\"" : "";
                return $"<SegmentTemplate timescale=\"1000\"{pto} startNumber=\"{liveWindow.StartSequence}\" initialization=\"{initUrl}\" media=\"{media}\">\n" +
                    liveWindow.Xml + "</SegmentTemplate>\n";
            }

            var timeline = TimelineFor(role);
            if (timeline != null && !timeline.IsEmpty)
            {
                var xml = timeline.SegmentTimelineXml(timeline.FirstNumber, timeline.LastNumber);
                if (xml != null)
                {
                    var pto = timeline.PresentationOffsetTicks > 0 ? $" presentationTimeOffset=\"{timeline.PresentationOffsetTicks}\"" : "";
                    return $"<SegmentTemplate timescale=\"{timeline.Timescale}\"{pto} startNumber=\"{timeline.FirstNumber}\" initialization=\"{initUrl}\" media=\"{media}\">\n" +
                        xml + "</SegmentTemplate>\n";
                }
            }

            var dur = Math.Max(segMs, 1);
            return $"<SegmentTemplate timescale=\"1000\" duration=\"{dur}\" startNumber=\"{startNumber}\" initialization=\"{initUrl}\" media=\"{media}\"/>\n";
        }

        private long _lastAvailabilityStartMs = 0;

        private long AvailabilityStartMs(long publishedEndMs)
        {
            if (_lastAvailabilityStartMs > 0) return _lastAvailabilityStartMs;

            var broadcastHeadMs = _session.LiveMetadata?.HeadSequenceTimeMs ?? 0;
            if (broadcastHeadMs > 0)
                return _lastAvailabilityStartMs = NowMs() - (broadcastHeadMs - _liveEpochMs);

            if (publishedEndMs > 0)
                return _lastAvailabilityStartMs = NowMs() - (publishedEndMs - _liveEpochMs);

            var heads = Roles().Select(x => _session.BufferFor(x).BufferedExactEndUs()).Where(x => x != long.MinValue).ToList();
            if (heads.Count == 0) return _liveAnchorWallMs - _liveAnchorMediaMs + _liveEpochMs;
            return _lastAvailabilityStartMs = NowMs() - (heads.Min() / 1000 - _liveEpochMs);
        }

        private long WindowFloorUs()
        {
            if (!IsLive) return 0;
            var range = _planner.Window;
            if (range == null) return 0;
            var starts = new[] { SabrSession.ROLE_VIDEO, SabrSession.ROLE_AUDIO }
                .Select(role => TimelineFor(role)?.StartUs(range.Value.First)).Where(x => x != null).Select(x => x!.Value).ToList();
            return starts.Count > 0 ? starts.Min() : 0;
        }

        private class LiveWindow
        {
            public int StartSequence { get; init; }
            public string Xml { get; init; } = "";
        }

        private LiveCastPlanner.Range? LiveRange()
        {
            var tracks = new List<LiveCastPlanner.Track>();
            if (_video != null)
            {
                var t = TrackFor(SabrSession.ROLE_VIDEO);
                if (t == null) return null;
                tracks.Add(t.Value);
            }
            if (_audio != null)
            {
                var t = TrackFor(SabrSession.ROLE_AUDIO);
                if (t == null) return null;
                tracks.Add(t.Value);
            }
            return _planner.PlanWindow(tracks);
        }

        private LiveWindow? LiveWindowFor(int role, LiveCastPlanner.Range shared)
        {
            var timeline = TimelineFor(role);
            if (timeline == null) return null;
            var xml = timeline.SegmentTimelineXml(shared.First, shared.Last);
            if (xml == null) return null;
            return new LiveWindow() { StartSequence = shared.First, Xml = xml };
        }

        private class SegmentRequest
        {
            private readonly TaskCompletionSource<byte[]?> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public volatile bool Stale;
            public void Complete(byte[]? value) => _tcs.TrySetResult(value);
            public async Task<byte[]?> AwaitAsync(long timeoutMs)
            {
                try { return await _tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)); }
                catch (TimeoutException) { return null; }
            }
        }

        private readonly ConcurrentDictionary<int, SegmentRequest> _videoInflight = new();
        private readonly ConcurrentDictionary<int, SegmentRequest> _audioInflight = new();
        private ConcurrentDictionary<int, SegmentRequest> InflightFor(int role) => role == SabrSession.ROLE_VIDEO ? _videoInflight : _audioInflight;

        private readonly SemaphoreSlim _videoInitLock = new(1, 1);
        private readonly SemaphoreSlim _audioInitLock = new(1, 1);
        private SemaphoreSlim InitLockFor(int role) => role == SabrSession.ROLE_VIDEO ? _videoInitLock : _audioInitLock;

        public async Task<byte[]?> GetInitAsync(int role)
        {
            if (IsDead) return null;
            var cached = CachedInit(role);
            if (cached != null) return cached;

            var initLock = InitLockFor(role);
            await initLock.WaitAsync();
            try
            {
                cached = CachedInit(role);
                if (cached != null) return cached;
                if (IsDead) return null;

                var format = FormatFor(role);
                if (format == null) return null;
                var buffer = _session.BufferFor(format);
                if (IsLive) return await LiveInitAsync(role, buffer);

                var init = await AwaitInitAsync(buffer);
                if (init == null) return null;
                var bytes = init.ToByteArray();
                SetCachedInit(role, bytes);
                return bytes;
            }
            finally
            {
                initLock.Release();
            }
        }

        private async Task<byte[]?> LiveInitAsync(int role, SabrTrackBuffer buffer)
        {
            var cached = CachedInit(role);
            if (cached != null) return cached;

            var announced = buffer.InitSegment;
            if (announced is { IsComplete: true })
            {
                var bytes = announced.ToByteArray();
                SetCachedInit(role, bytes);
                SabrSession.LiveLog($"live init role={role} from the announced init segment len={bytes.Length}");
                return bytes;
            }

            var deadline = NowMs() + PREPARE_TIMEOUT_MS;
            while (NowMs() < deadline)
            {
                if (IsDead) return null;
                var head = buffer.LastCompletedFromFront();
                if (head >= 0)
                {
                    for (var seq = Math.Max(buffer.LowestSequence, 0); seq <= head; seq++)
                    {
                        var seg = buffer.Get(seq);
                        if (seg == null || !seg.IsComplete) continue;
                        var bytes = seg.ToByteArray();
                        var len = InitPrefixLength(role, bytes);
                        if (len > 0)
                        {
                            var init = bytes.AsSpan(0, len).ToArray();
                            SetCachedInit(role, init);
                            SabrSession.LiveLog($"live init role={role} seq={seq} len={len}");
                            return init;
                        }
                    }
                }
                _session.WakePump();
                await Delay(50, _cts.Token);
            }
            return null;
        }

        private SortedDictionary<int, long> AnchorsFor(int role) => role == SabrSession.ROLE_VIDEO ? _videoAnchors : _audioAnchors;

        private long? TargetUsFor(int role, int sequence)
        {
            var timeline = TimelineFor(role);
            var nominalMid = timeline?.MidUs(sequence);
            if (timeline == null || nominalMid == null) return null;

            var anchors = AnchorsFor(role);
            lock (anchors)
            {
                if (anchors.Count == 0) return nominalMid;
                if (anchors.TryGetValue(sequence, out var exact)) return exact + (timeline.DurationUs(sequence) ?? 0) / 2;

                KeyValuePair<int, long>? floor = null, ceil = null;
                foreach (var pair in anchors)
                {
                    if (pair.Key <= sequence) floor = pair;
                    else { ceil = pair; break; }
                }
                var nearest = floor == null ? ceil : ceil == null ? floor : (sequence - floor.Value.Key <= ceil.Value.Key - sequence ? floor : ceil);
                if (nearest == null) return nominalMid;

                var nominalNearest = timeline.StartUs(nearest.Value.Key);
                if (nominalNearest == null) return nominalMid;
                return Math.Max(nominalMid.Value + (nearest.Value.Value - nominalNearest.Value), 0);
            }
        }

        private void CheckAnchor(int role, int sequence, SabrSegment segment)
        {
            var timeline = TimelineFor(role);
            var nominal = timeline?.StartUs(sequence);
            if (nominal == null) return;
            var drift = segment.StartUs - nominal.Value;
            if (Math.Abs(drift) < ANCHOR_TOLERANCE_US) return;

            var anchors = AnchorsFor(role);
            bool isNew;
            lock (anchors)
            {
                isNew = !anchors.ContainsKey(sequence);
                anchors[sequence] = segment.StartUs;
            }
            if (isNew)
                Logger.w(TAG, $"Timeline drift role={role} seq={sequence} nominalMs={nominal / 1000} actualMs={segment.StartUs / 1000} driftMs={drift / 1000}; anchoring");
        }

        private void RequestSeek(int role, long targetUs, bool mustRestart = false)
        {
            var format = FormatFor(role);
            if (format == null) return;
            lock (_seekLock)
            {
                var now = NowMs();
                if (role == SabrSession.ROLE_VIDEO) { _videoSeekUs = targetUs; _videoSeekAtMs = now; }
                else { _audioSeekUs = targetUs; _audioSeekAtMs = now; }

                var coveredByRestart = !mustRestart && _lastSeekUs != long.MinValue && targetUs >= _lastSeekUs;
                var buffer = _session.BufferFor(format);

                if (mustRestart && now - _lastSeekAtMs < MIN_RESTART_INTERVAL_MS &&
                    _lastSeekUs != long.MinValue && targetUs >= _lastSeekUs && targetUs - _lastSeekUs <= FORWARD_GAP_SLACK_US)
                {
                    _session.SetDemand(role, format, StreamThroughDemand(buffer, targetUs));
                    SabrSession.SabrLog($"cast seek role={role} targetMs={targetUs / 1000} suppressed (restart in flight, within streaming range)");
                    return;
                }

                if (coveredByRestart && now - _lastSeekAtMs < SEEK_COALESCE_MS && Math.Abs(targetUs - _lastSeekUs) < SeekCoalesceUs())
                {
                    _session.SetDemand(role, format, StreamThroughDemand(buffer, targetUs));
                    return;
                }

                if (coveredByRestart && now - _lastSeekAtMs < MIN_RESTART_INTERVAL_MS && targetUs - _lastSeekUs <= FORWARD_GAP_SLACK_US)
                {
                    _session.SetDemand(role, format, StreamThroughDemand(buffer, targetUs));
                    SabrSession.SabrLog($"cast seek role={role} targetMs={targetUs / 1000} suppressed (restart floor, already covered)");
                    return;
                }

                bool NearThisSeek(long t) => Math.Abs(t - targetUs) <= FORWARD_GAP_SLACK_US;
                long? freshVideo = (_videoSeekUs >= 0 && now - _videoSeekAtMs < SEEK_COALESCE_MS && NearThisSeek(_videoSeekUs)) ? _videoSeekUs : null;
                long? freshAudio = (_audioSeekUs >= 0 && now - _audioSeekAtMs < SEEK_COALESCE_MS && NearThisSeek(_audioSeekUs)) ? _audioSeekUs : null;

                var targets = new List<long>();
                if (_video != null && freshVideo != null) targets.Add(freshVideo.Value);
                if (_audio != null && freshAudio != null) targets.Add(freshAudio.Value);

                var from = Math.Max((targets.Count > 0 ? targets.Min() : targetUs) - SeekCoalesceUs(), 0);

                if (_video != null) _session.SetDemand(SabrSession.ROLE_VIDEO, _video, from);
                if (_audio != null) _session.SetDemand(SabrSession.ROLE_AUDIO, _audio, from);
                _session.SetPlaybackPosition(from);
                _session.Restart(from);

                _lastSeekUs = from;
                _lastSeekAtMs = now;
                AbandonRequestsFarFrom(from);
                SabrSession.SabrLog($"cast seek role={role} targetMs={targetUs / 1000} restartFromMs={from / 1000}");
            }
        }

        private void AbandonRequestsFarFrom(long fromUs)
        {
            var behindUs = SeekCoalesceUs();
            foreach (var role in new[] { SabrSession.ROLE_VIDEO, SabrSession.ROLE_AUDIO })
            {
                var inflight = InflightFor(role);
                foreach (var (seq, request) in inflight.ToList())
                {
                    var targetUs = TargetUsFor(role, seq);
                    if (targetUs == null) continue;
                    var stranded = targetUs < fromUs - behindUs || targetUs > fromUs + FORWARD_GAP_SLACK_US;
                    if (!stranded) continue;

                    request.Stale = true;
                    inflight.TryRemove(new KeyValuePair<int, SegmentRequest>(seq, request));
                    request.Complete(null);
                }
            }
        }

        private long SeekCoalesceUs() => 2 * SegmentMs() * 1000;

        private long StreamThroughDemand(SabrTrackBuffer buffer, long targetUs)
        {
            var floor = FetchFloorFor(buffer);
            return Math.Min(targetUs, floor != long.MinValue ? floor : targetUs);
        }

        private long FetchFloorFor(SabrTrackBuffer buffer) =>
            FetchFloor(buffer.BufferedEndFromFrontUs(), buffer.FirstAtOrAfter(-1)?.StartUs, SessionFloorUs());

        private long GapFloorFor(SabrTrackBuffer buffer) =>
            GapFloor(buffer.BufferedEndFromFrontUs(), buffer.FirstAtOrAfter(-1)?.StartUs, SessionFloorUs(), SeekCoalesceUs());

        private long SessionFloorUs() => IsLive ? long.MinValue : _session.FetchFloorUs;

        private byte[]? CachedInit(int role) => role == SabrSession.ROLE_VIDEO ? _videoInit : _audioInit;

        private void SetCachedInit(int role, byte[] value)
        {
            if (role == SabrSession.ROLE_VIDEO) _videoInit = value; else _audioInit = value;
        }

        public async Task<byte[]?> GetSegmentAsync(int role, int sequence)
        {
            if (IsDead) return null;
            if (FormatFor(role) == null) return null;
            var lastSeq = role == SabrSession.ROLE_VIDEO ? _videoLastSeq : _audioLastSeq;
            if (sequence > lastSeq) return null;

            NoteReceiverRequest(role, sequence);

            var inflight = InflightFor(role);
            SegmentRequest? owned = null;
            var request = inflight.GetOrAdd(sequence, _ => owned = new SegmentRequest());
            if (!ReferenceEquals(owned, request)) return await request.AwaitAsync(SegmentTimeoutMs());

            byte[]? result = null;
            try
            {
                result = await FetchSegmentAsync(role, sequence, request);
            }
            catch (Exception ex)
            {
                Logger.w(TAG, $"getSegment role={role} seq={sequence} failed", ex);
            }
            finally
            {
                inflight.TryRemove(new KeyValuePair<int, SegmentRequest>(sequence, request));
                request.Complete(result);
            }
            return result;
        }

        private long SegmentTimeoutMs()
        {
            if (!IsLive) return SEGMENT_TIMEOUT_MS;
            return Math.Clamp(3 * SegmentMs(), 4_000L, 10_000L);
        }

        private async Task<byte[]?> FetchSegmentAsync(int role, int sequence, SegmentRequest request)
        {
            var format = FormatFor(role);
            if (format == null) return null;
            var buffer = _session.BufferFor(format);
            var firstSeq = role == SabrSession.ROLE_VIDEO ? _videoFirstSeq : _audioFirstSeq;
            var segMs = role == SabrSession.ROLE_VIDEO ? _videoSegMs : _audioSegMs;

            var targetUs = TargetUsFor(role, sequence) ?? buffer.Get(sequence)?.StartUs;
            if (targetUs == null)
            {
                var headSeq = buffer.LastCompletedFromFront();
                var headSeg = headSeq >= 0 ? buffer.Get(headSeq) : null;
                if (headSeg != null && sequence > headSeq)
                    targetUs = headSeg.EndUs + Math.Max(sequence - headSeq - 1, 0) * segMs * 1000;
                else if (IsLive) return null;
                else targetUs = Math.Max(sequence - firstSeq, 0) * segMs * 1000;
            }

            var low = buffer.LowestSequence;
            var head = buffer.LastCompletedFromFront();
            var absent = buffer.Get(sequence) == null;

            var floor = FetchFloorFor(buffer);
            var halfSegmentUs = segMs * 500;
            var backwards = absent && ((low >= 0 && sequence < low) ||
                (low < 0 && !IsLive && floor != long.MinValue && targetUs + halfSegmentUs < floor));

            var gapFloor = GapFloorFor(buffer);
            var forwardGap = absent && !IsLive && gapFloor != long.MinValue && targetUs > gapFloor + FORWARD_GAP_SLACK_US;

            SabrSession.SabrLog($"getSegment role={role} reqSeq={sequence} buffered={!absent} head={head} low={low} targetMs={targetUs / 1000} backwards={backwards} forwardGap={forwardGap}");

            var ready = buffer.Get(sequence);
            if (ready is { IsComplete: true })
            {
                CheckAnchor(role, sequence, ready);
                return FinishSegment(role, ready);
            }

            if (backwards && IsLive)
            {
                SabrSession.LiveLog($"getSegment role={role} reqSeq={sequence} below window (low={low}) -> 404");
                OnReceiverLost?.Invoke();
                return null;
            }

            if (!IsLive && (backwards || forwardGap)) RequestSeek(role, targetUs.Value, forwardGap);

            var deadline = NowMs() + SegmentTimeoutMs();
            var seekedFromWait = false;
            while (NowMs() < deadline)
            {
                if (IsDead || request.Stale) return null;

                var segment = buffer.Get(sequence);
                if (segment == null)
                {
                    var lowNow = buffer.LowestSequence;
                    if (!IsLive && !seekedFromWait && lowNow >= 0 && sequence < lowNow)
                    {
                        seekedFromWait = true;
                        SabrSession.SabrLog($"getSegment role={role} reqSeq={sequence} fell below the refill (low={lowNow}); seeking");
                        RequestSeek(role, targetUs.Value);
                        continue;
                    }

                    if (!IsLive) _session.SetDemand(role, format, StreamThroughDemand(buffer, targetUs.Value));
                    _session.WakePump();
                    await buffer.AwaitSequenceAsync(sequence, TimeSpan.FromMilliseconds(250));
                    continue;
                }

                if (!segment.IsComplete)
                {
                    await WaitChanged(buffer, 250);
                    if (!segment.IsComplete) _session.WakePump();
                    continue;
                }

                CheckAnchor(role, sequence, segment);
                return FinishSegment(role, segment);
            }
            SabrSession.SabrLog($"getSegment role={role} reqSeq={sequence} TIMEOUT (404) head={buffer.LastCompletedFromFront()}");
            return null;
        }

        private byte[] FinishSegment(int role, SabrSegment segment)
        {
            var bytes = segment.ToByteArray();
            if (!IsLive) return bytes;
            var strip = Math.Min(InitPrefixLength(role, bytes), bytes.Length);
            return strip > 0 ? bytes.AsSpan(strip).ToArray() : bytes;
        }

        public SabrSession.Transferable? ExportTransferable()
        {
            if (_session.IsReleased || _session.FatalError != null) return null;
            return _session.ExportTransferable();
        }

        private void FailAllInflight()
        {
            foreach (var map in new[] { _videoInflight, _audioInflight })
            {
                foreach (var request in map.Values)
                {
                    request.Stale = true;
                    request.Complete(null);
                }
                map.Clear();
            }
        }

        public void Release()
        {
            if (_released) return;
            _released = true;
            try { _cts.Cancel(); } catch { }

            OnBackoff = null;
            OnFatalError = null;
            OnReceiverLost = null;
            PlayheadUs = null;

            _session.SetListener(null);
            _session.Release();
            FailAllInflight();
        }

        private UMPFormat? FormatFor(int role) => role == SabrSession.ROLE_VIDEO ? _video : _audio;

        private async Task<SabrSegment?> AwaitInitAsync(SabrTrackBuffer buffer)
        {
            var deadline = NowMs() + SEGMENT_TIMEOUT_MS;
            while (NowMs() < deadline)
            {
                if (IsDead) return null;
                var init = buffer.InitSegment;
                if (init == null)
                {
                    _session.WakePump();
                    await WaitChanged(buffer, 50);
                    continue;
                }
                if (init.IsComplete) return init;
                await WaitChanged(buffer, 250);
                if (!init.IsComplete) _session.WakePump();
            }
            return null;
        }

        private static string ToIsoDuration(long ms) => "PT" + (ms / 1000.0).ToString("F3", CultureInfo.InvariantCulture) + "S";

        private static string ToIso8601(long epochMs) => DateTimeOffset.FromUnixTimeMilliseconds(epochMs).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        private bool IsWebm(int role)
        {
            var format = FormatFor(role);
            return format != null && IsWebmMime(format.ContainerMimeType);
        }

        private int InitPrefixLength(int role, byte[] data) => IsWebm(role) ? WebmInitPrefixLength(data) : Mp4InitPrefixLength(data);

        private static long ReadU32(byte[] d, int p) => ((long)d[p] << 24) | ((long)d[p + 1] << 16) | ((long)d[p + 2] << 8) | d[p + 3];

        private static long ReadU64(byte[] d, int p)
        {
            long v = 0;
            for (var i = 0; i < 8; i++) v = (v << 8) | d[p + i];
            return v;
        }

        private static int Mp4InitPrefixLength(byte[] data)
        {
            var pos = 0;
            while (pos + 8 <= data.Length)
            {
                var size32 = ReadU32(data, pos);
                var type = Encoding.ASCII.GetString(data, pos + 4, 4);
                long boxSize;
                if (size32 == 1)
                {
                    if (pos + 16 > data.Length) return 0;
                    boxSize = ReadU64(data, pos + 8);
                }
                else if (size32 == 0) boxSize = data.Length - pos;
                else boxSize = size32;
                if (boxSize < 8) return 0;
                if (type is "styp" or "sidx" or "moof" or "mdat" or "emsg") return pos;
                pos += (int)boxSize;
            }
            return 0;
        }

        private static int WebmInitPrefixLength(byte[] data)
        {
            var pos = 0;
            while (pos + 4 <= data.Length)
            {
                var idLen = EbmlIdLength(data[pos]);
                if (idLen == 0 || pos + idLen > data.Length) return 0;
                var id = ReadEbmlId(data, pos, idLen);
                var sz = ReadEbmlSize(data, pos + idLen);
                if (sz == null) return 0;
                var contentStart = pos + idLen + sz.Value.Length;
                if (id == 0x18538067L)
                {
                    var cpos = contentStart;
                    while (cpos + 4 <= data.Length)
                    {
                        var cidLen = EbmlIdLength(data[cpos]);
                        if (cidLen == 0 || cpos + cidLen > data.Length) return 0;
                        var cid = ReadEbmlId(data, cpos, cidLen);
                        if (cid == 0x1F43B675L) return cpos;
                        var csz = ReadEbmlSize(data, cpos + cidLen);
                        if (csz == null || csz.Value.Value < 0) return 0;
                        cpos += cidLen + csz.Value.Length + (int)csz.Value.Value;
                    }
                    return 0;
                }
                if (sz.Value.Value < 0) return 0;
                pos = contentStart + (int)sz.Value.Value;
            }
            return 0;
        }

        private static int EbmlIdLength(byte b)
        {
            if ((b & 0x80) != 0) return 1;
            if ((b & 0x40) != 0) return 2;
            if ((b & 0x20) != 0) return 3;
            if ((b & 0x10) != 0) return 4;
            return 0;
        }

        private static long ReadEbmlId(byte[] d, int p, int len)
        {
            long v = 0;
            for (var i = 0; i < len; i++) v = (v << 8) | d[p + i];
            return v;
        }

        private static (long Value, int Length)? ReadEbmlSize(byte[] d, int p)
        {
            if (p >= d.Length) return null;
            int first = d[p];
            var len = 0;
            var mask = 0x80;
            while (len < 8)
            {
                len++;
                if ((first & mask) != 0) break;
                mask >>= 1;
            }
            if (len == 0 || (first & mask) == 0 || p + len > d.Length) return null;
            long value = first & (mask - 1);
            var allOnes = value == mask - 1;
            for (var i = 1; i < len; i++)
            {
                var bv = d[p + i];
                if (bv != 0xFF) allOnes = false;
                value = (value << 8) | bv;
            }
            return allOnes ? (-1L, len) : (value, len);
        }

        public static long FetchFloor(long bufferedEndUs, long? frontStartUs, long sessionFloorUs)
        {
            if (bufferedEndUs != long.MinValue) return bufferedEndUs;
            if (frontStartUs != null) return frontStartUs.Value;
            return sessionFloorUs;
        }

        public static long GapFloor(long bufferedEndUs, long? frontStartUs, long sessionFloorUs, long preRollUs)
        {
            var floor = FetchFloor(bufferedEndUs, frontStartUs, sessionFloorUs);
            if (sessionFloorUs == long.MinValue) return floor;
            var target = sessionFloorUs + preRollUs;
            return floor == long.MinValue ? target : Math.Max(floor, target);
        }
    }
}
