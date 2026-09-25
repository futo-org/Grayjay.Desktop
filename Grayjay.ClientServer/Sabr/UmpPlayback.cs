using System.Collections.Concurrent;
using Grayjay.ClientServer.Sabr.Proto;
using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC;
using Grayjay.Engine.Models.Video.Sources;

namespace Grayjay.ClientServer.Sabr
{
    public class UmpPlayback : ISabrSessionListener, IDisposable
    {
        private const string TAG = "UmpPlayback";
        private const long LIVE_TARGET_OFFSET_US = 15_000_000L;

        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string WindowId { get; }
        public string? Tag { get; set; }
        public string? SubtitleUrl { get; set; }
        public UMPSource Source { get; }
        public SabrSession Session { get; }
        public bool IsLive => Source.IsLive;
        public long DurationUs => Session.DurationUs;

        public List<UMPFormat> VideoFormats { get; }
        public List<UMPFormat> AudioFormats { get; }

        private List<UMPFormat> _videoAcceptable = new();
        private List<UMPFormat> _audioAcceptable = new();
        private readonly object _lock = new object();
        private volatile bool _configured = false;
        private volatile bool _bootstrapped = false;
        private readonly AsyncSignal _liveSignal = new AsyncSignal();
        private volatile bool _loaderShown = false;

        public UmpPlayback(string windowId, UMPSource source, SabrSession.Transferable? restoreState = null, SabrSession.Transferable? continueState = null)
        {
            WindowId = windowId;
            Source = source;
            VideoFormats = source.VideoFormats.ToList();
            AudioFormats = source.AudioFormats.ToList();
            Session = SabrStreamSpec.FromSource(source).CreateSession();
            if (continueState != null)
                Session.Continue(continueState);
            else if (restoreState != null)
                Session.Restore(restoreState);
            Session.SetListener(this);
        }

        public long MediaBaseUs => IsLive ? Session.MediaBaseUs : 0;

        public List<UMPFormat> AcceptableFor(int role)
        {
            lock (_lock) return role == SabrSession.ROLE_VIDEO ? _videoAcceptable : _audioAcceptable;
        }

        public static string KeyOf(UMPFormat format) => $"{format.Itag}:{format.Xtags ?? ""}";

        public UMPFormat? FindFormatByKey(string key) =>
            VideoFormats.Concat(AudioFormats).FirstOrDefault(x => KeyOf(x) == key);

        public void Configure(List<UMPFormat> videoAcceptable, List<UMPFormat> audioAcceptable, int viewportWidth, int viewportHeight, long startUs)
        {
            lock (_lock)
            {
                _videoAcceptable = videoAcceptable;
                _audioAcceptable = audioAcceptable;
            }
            Session.ViewportWidth = viewportWidth;
            Session.ViewportHeight = viewportHeight;

            var fromUs = startUs + MediaBaseUs;
            var first = !_configured;
            _configured = true;

            if (videoAcceptable.Count > 0)
                Session.SetDemand(SabrSession.ROLE_VIDEO, videoAcceptable, fromUs, this);
            else
                Session.ClearDemand(SabrSession.ROLE_VIDEO, this);
            if (audioAcceptable.Count > 0)
                Session.SetDemand(SabrSession.ROLE_AUDIO, audioAcceptable, fromUs, this);
            else
                Session.ClearDemand(SabrSession.ROLE_AUDIO, this);

            if (first)
            {
                if (!IsLive)
                {
                    Session.SetPlaybackPosition(fromUs);
                    if (fromUs > 0)
                        Session.Restart(fromUs, true);
                }
                Session.Start();
            }
        }

        public async Task<bool> AwaitLiveReadyAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (!IsLive) return true;
            var deadline = DateTime.UtcNow + timeout;
            while (!(_bootstrapped && Session.LiveMetadata != null))
            {
                if (Session.FatalError != null) return false;
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) return false;
                await _liveSignal.WaitAsync(remaining < TimeSpan.FromMilliseconds(500) ? remaining : TimeSpan.FromMilliseconds(500), cancellationToken);
            }
            return true;
        }

        public long LiveWindowUs()
        {
            var live = Session.LiveMetadata;
            if (live == null) return 0;
            var headUs = live.HeadSequenceTimeMs * 1000L - Session.MediaBaseUs;
            return headUs > 0 ? headUs : 0;
        }

        public (long StartUs, long EndUs)? LiveSeekableWindowUs()
        {
            var lm = Session.LiveMetadata;
            if (lm == null || lm.MinSeekableTimescale <= 0 || lm.MaxSeekableTimescale <= 0) return null;
            var start = lm.MinSeekableTimeTicks * 1_000_000L / lm.MinSeekableTimescale - Session.MediaBaseUs;
            var end = lm.MaxSeekableTimeTicks * 1_000_000L / lm.MaxSeekableTimescale - Session.MediaBaseUs;
            return (Math.Max(0, start), Math.Max(0, end));
        }

        public long LiveEdgeStartUs() => Math.Max(0, LiveWindowUs() - LIVE_TARGET_OFFSET_US);

        public void OnLiveMetadata(LiveMetadata metadata)
        {
            if (IsLive && !_bootstrapped)
            {
                _bootstrapped = true;
                var edgeUs = LiveEdgeStartUs() + Session.MediaBaseUs;
                Session.Restart(edgeUs, true);
                Session.SetPlaybackPosition(edgeUs);
            }
            _liveSignal.Set();
        }

        public void OnFormatInitialization(FormatInitializationMetadata metadata) { }

        public void OnSessionError(Exception error)
        {
            Logger.w(TAG, "SABR session error", error);
            _liveSignal.Set();
        }

        public void OnBackoff(long delayMs)
        {
            if (delayMs < 1500) return;
            _loaderShown = true;
            StateWebsocket.VideoLoader("", (int)Math.Min(int.MaxValue, delayMs), WindowId, Tag);
        }

        public void OnBackoffEnded()
        {
            if (!_loaderShown) return;
            _loaderShown = false;
            StateWebsocket.VideoLoaderFinish(WindowId, Tag);
        }

        public void Dispose()
        {
            if (_loaderShown)
                OnBackoffEnded();
            Session.SetListener(null);
            Session.Release();
        }
    }

    public static class UmpPlaybackRegistry
    {
        private static readonly ConcurrentDictionary<string, UmpPlayback> _playbacks = new();

        public static UmpPlayback Create(string windowId, UMPSource source, SabrSession.Transferable? restoreState = null, SabrSession.Transferable? continueState = null)
        {
            var playback = new UmpPlayback(windowId, source, restoreState, continueState);
            _playbacks[playback.Id] = playback;
            return playback;
        }

        public static UmpPlayback? Get(string id)
        {
            return _playbacks.TryGetValue(id, out var playback) ? playback : null;
        }

        public static void Release(string id)
        {
            if (_playbacks.TryRemove(id, out var playback))
                playback.Dispose();
        }

        public static void ReleaseWindow(string windowId, string? exceptId = null)
        {
            foreach (var playback in _playbacks.Values.Where(x => x.WindowId == windowId && x.Id != exceptId).ToList())
                Release(playback.Id);
        }
    }
}
