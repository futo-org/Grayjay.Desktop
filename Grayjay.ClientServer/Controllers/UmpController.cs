using Grayjay.ClientServer.Sabr;
using Grayjay.Engine.Models.Video.Sources;
using Microsoft.AspNetCore.Mvc;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class UmpController : ControllerBase
    {
        private static readonly TimeSpan SEGMENT_TIMEOUT = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan INIT_TIMEOUT = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan LIVE_INIT_TIMEOUT = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan LIVE_READY_TIMEOUT = TimeSpan.FromSeconds(20);
        private const long COVER_SLACK_US = 500_000;

        public class FormatInfo
        {
            public string Key { get; set; }
            public int Itag { get; set; }
            public string Xtags { get; set; }
            public string MimeType { get; set; }
            public string Codecs { get; set; }
            public string CodecName { get; set; }
            public int Bitrate { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public int Fps { get; set; }
            public int AudioChannels { get; set; }
            public int AudioSampleRate { get; set; }
            public string? Language { get; set; }
            public string? LanguageName { get; set; }
            public bool Original { get; set; }
            public bool IsDrc { get; set; }
            public string Label { get; set; }

            public static FormatInfo From(UMPFormat format) => new FormatInfo()
            {
                Key = UmpPlayback.KeyOf(format),
                Itag = format.Itag,
                Xtags = format.Xtags ?? "",
                MimeType = format.ContainerMimeType,
                Codecs = format.Codecs ?? "",
                CodecName = format.CodecName,
                Bitrate = format.Bitrate,
                Width = format.Width,
                Height = format.Height,
                Fps = format.Fps,
                AudioChannels = format.AudioChannels,
                AudioSampleRate = format.AudioSampleRate,
                Language = format.Language,
                LanguageName = string.IsNullOrWhiteSpace(format.Language) ? null : UMPFormat.LanguageDisplayName(format.Language),
                Original = format.IsOriginalAudio,
                IsDrc = format.IsDrc,
                Label = format.IsVideo ? format.VideoLabel : format.AudioLabel
            };
        }

        public class InfoResult
        {
            public string Id { get; set; }
            public bool IsLive { get; set; }
            public long DurationMs { get; set; }
            public long MediaBaseMs { get; set; }
            public long? WindowStartMs { get; set; }
            public long? WindowEndMs { get; set; }
            public long? LiveEdgeStartMs { get; set; }
            public List<FormatInfo> VideoFormats { get; set; }
            public List<FormatInfo> AudioFormats { get; set; }
            public string? ActiveVideoKey { get; set; }
            public string? ActiveAudioKey { get; set; }
            public string? SubtitleUrl { get; set; }
        }

        public class ConfigureRequest
        {
            public List<string>? VideoKeys { get; set; }
            public List<string>? AudioKeys { get; set; }
            public int ViewportWidth { get; set; }
            public int ViewportHeight { get; set; }
            public long StartMs { get; set; }
        }

        public class ErrorResult
        {
            public string Kind { get; set; }
            public string Message { get; set; }
        }

        private void NoStore()
        {
            Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            Response.Headers["Access-Control-Expose-Headers"] = "X-Seq, X-Start-Us, X-Duration-Us, X-Key, X-Itag, X-Xtags, X-Mime, X-Codecs, X-End";
        }

        private IActionResult? CheckFatal(UmpPlayback playback)
        {
            var error = playback.Session.FatalError;
            if (error == null)
                return null;
            var kind = error switch
            {
                SabrBlockedException => "blocked",
                SabrReloadRequiredException => "reload",
                SabrFormatSubstitutedException => "substituted",
                _ => "error"
            };
            return StatusCode(410, new ErrorResult() { Kind = kind, Message = error.Message });
        }

        private UmpPlayback GetPlayback(string id) =>
            UmpPlaybackRegistry.Get(id) ?? throw new BadHttpRequestException("UMP playback not found", 404);

        [HttpGet]
        public async Task<IActionResult> Info(string id, bool waitLive = false, CancellationToken cancellationToken = default)
        {
            NoStore();
            var playback = GetPlayback(id);
            if (waitLive && playback.IsLive)
                await playback.AwaitLiveReadyAsync(LIVE_READY_TIMEOUT, cancellationToken);
            var fatal = CheckFatal(playback);
            if (fatal != null) return fatal;

            var window = playback.IsLive ? playback.LiveSeekableWindowUs() : null;
            return Ok(new InfoResult()
            {
                Id = playback.Id,
                IsLive = playback.IsLive,
                DurationMs = playback.IsLive ? playback.LiveWindowUs() / 1000 : Math.Max(0, playback.DurationUs / 1000),
                MediaBaseMs = playback.MediaBaseUs / 1000,
                WindowStartMs = window?.StartUs / 1000,
                WindowEndMs = window?.EndUs / 1000,
                LiveEdgeStartMs = playback.IsLive && playback.Session.LiveMetadata != null ? playback.LiveEdgeStartUs() / 1000 : null,
                VideoFormats = playback.VideoFormats.Select(FormatInfo.From).ToList(),
                AudioFormats = playback.AudioFormats.Select(FormatInfo.From).ToList(),
                ActiveVideoKey = playback.Session.ActiveFormat(SabrSession.ROLE_VIDEO) is { } av ? UmpPlayback.KeyOf(av) : null,
                ActiveAudioKey = playback.Session.ActiveFormat(SabrSession.ROLE_AUDIO) is { } aa ? UmpPlayback.KeyOf(aa) : null,
                SubtitleUrl = playback.SubtitleUrl
            });
        }

        [HttpPost]
        public IActionResult Configure(string id, [FromBody] ConfigureRequest request)
        {
            NoStore();
            var playback = GetPlayback(id);
            var fatal = CheckFatal(playback);
            if (fatal != null) return fatal;

            var video = (request.VideoKeys ?? new List<string>())
                .Select(key => playback.VideoFormats.FirstOrDefault(x => UmpPlayback.KeyOf(x) == key))
                .Where(x => x != null).Cast<UMPFormat>().ToList();
            var audio = (request.AudioKeys ?? new List<string>())
                .Select(key => playback.AudioFormats.FirstOrDefault(x => UmpPlayback.KeyOf(x) == key))
                .Where(x => x != null).Cast<UMPFormat>().ToList();
            if (video.Count == 0 && audio.Count == 0)
                return BadRequest(new ErrorResult() { Kind = "unsupported", Message = "None of the UMP formats can be decoded" });

            playback.Configure(video, audio, request.ViewportWidth, request.ViewportHeight, request.StartMs * 1000);
            return Ok();
        }

        private static int RoleOf(string role) => role == "audio" ? SabrSession.ROLE_AUDIO : SabrSession.ROLE_VIDEO;

        private void SetFormatHeaders(UMPFormat format)
        {
            Response.Headers["X-Key"] = UmpPlayback.KeyOf(format);
            Response.Headers["X-Itag"] = format.Itag.ToString();
            Response.Headers["X-Xtags"] = format.Xtags ?? "";
            Response.Headers["X-Mime"] = format.ContainerMimeType;
            Response.Headers["X-Codecs"] = format.Codecs ?? "";
        }

        [HttpGet]
        public async Task<IActionResult> Init(string id, string role, string? key = null, CancellationToken cancellationToken = default)
        {
            NoStore();
            var playback = GetPlayback(id);
            var fatal = CheckFatal(playback);
            if (fatal != null) return fatal;

            var roleId = RoleOf(role);
            var format = (key != null ? playback.FindFormatByKey(key) : null)
                ?? playback.Session.ActiveFormat(roleId)
                ?? playback.AcceptableFor(roleId).FirstOrDefault();
            if (format == null)
                return NotFound();

            var buffer = playback.Session.BufferFor(format);
            if (buffer.InitSegment is not { IsComplete: true })
                playback.Session.WakePump();
            var init = await buffer.AwaitInitAsync(playback.IsLive ? LIVE_INIT_TIMEOUT : INIT_TIMEOUT, cancellationToken);
            fatal = CheckFatal(playback);
            if (fatal != null) return fatal;

            SetFormatHeaders(format);
            if (init == null)
            {
                if (playback.IsLive)
                    return NoContent();
                return StatusCode(504);
            }
            return File(init.ToByteArray(), format.ContainerMimeType);
        }

        [HttpGet]
        public async Task<IActionResult> Segment(string id, string role, long loadMs, long playheadMs, int? seq = null, string? key = null, CancellationToken cancellationToken = default)
        {
            NoStore();
            var playback = GetPlayback(id);
            var fatal = CheckFatal(playback);
            if (fatal != null) return fatal;

            var session = playback.Session;
            var roleId = RoleOf(role);
            var acceptable = playback.AcceptableFor(roleId);
            if (acceptable.Count == 0)
                return NotFound();

            var baseUs = playback.MediaBaseUs;
            var loadUs = loadMs * 1000 + baseUs;
            session.SetPlaybackPosition(playheadMs * 1000 + baseUs);
            session.SetDemand(roleId, acceptable, loadUs, playback);

            var active = session.ActiveFormat(roleId) ?? acceptable[0];
            var requested = key != null ? acceptable.FirstOrDefault(x => UmpPlayback.KeyOf(x) == key) : null;
            var order = new List<UMPFormat>();
            if (requested != null) order.Add(requested);
            if (!order.Contains(active)) order.Add(active);
            order.AddRange(acceptable.Where(x => !order.Contains(x)));

            (UMPFormat Format, SabrSegment Segment)? Locate()
            {
                foreach (var f in order)
                {
                    var b = session.BufferFor(f);
                    if (seq != null && ReferenceEquals(f, requested))
                    {
                        var bySeq = b.Get(seq.Value);
                        if (bySeq != null) return (f, bySeq);
                    }
                    var covering = b.FirstCovering(loadUs);
                    if (covering != null && covering.StartUs <= loadUs + COVER_SLACK_US) return (f, covering);
                }
                return null;
            }

            var found = Locate();
            if (found == null)
            {
                if (session.IsComplete(active))
                {
                    SetFormatHeaders(active);
                    Response.Headers["X-End"] = "1";
                    return NoContent();
                }

                var front = session.BufferFor(active).FirstAtOrAfter(-1);
                if (front != null && front.StartUs > loadUs + COVER_SLACK_US)
                {
                    if (session.IsLive) found = (active, front);
                    else session.Restart(loadUs);
                }
                else
                    session.WakePump();

                var deadline = DateTime.UtcNow + SEGMENT_TIMEOUT;
                while (found == null && DateTime.UtcNow < deadline)
                {
                    var changed = session.BufferFor(session.ActiveFormat(roleId) ?? active).ChangedTask;
                    found = Locate();
                    if (found != null || session.FatalError != null) break;
                    if (session.IsComplete(session.ActiveFormat(roleId) ?? active)) break;
                    try
                    {
                        await changed.WaitAsync(TimeSpan.FromMilliseconds(250), cancellationToken);
                    }
                    catch (TimeoutException) { }
                }
            }

            fatal = CheckFatal(playback);
            if (fatal != null) return fatal;

            if (found == null)
            {
                var current = session.ActiveFormat(roleId) ?? active;
                if (session.IsComplete(current))
                {
                    SetFormatHeaders(current);
                    Response.Headers["X-End"] = "1";
                    return NoContent();
                }
                return StatusCode(504);
            }

            var (format, segment) = found.Value;
            if (!segment.IsComplete)
            {
                var complete = await session.BufferFor(format).AwaitCompleteAsync(segment, SEGMENT_TIMEOUT, cancellationToken);
                fatal = CheckFatal(playback);
                if (fatal != null) return fatal;
                if (!complete)
                    return StatusCode(504);
            }

            SetFormatHeaders(format);
            Response.Headers["X-Seq"] = segment.SequenceNumber.ToString();
            Response.Headers["X-Start-Us"] = (segment.StartUs - baseUs).ToString();
            Response.Headers["X-Duration-Us"] = segment.DurationUs.ToString();
            var endSegment = session.EndSegmentNumber(format);
            if (!session.IsLive && endSegment > 0 && segment.SequenceNumber >= endSegment)
                Response.Headers["X-End"] = "1";
            return File(segment.ToByteArray(), format.ContainerMimeType);
        }

        [HttpPost]
        public IActionResult Seek(string id, long ms)
        {
            NoStore();
            var playback = GetPlayback(id);
            var fatal = CheckFatal(playback);
            if (fatal != null) return fatal;
            playback.Session.SeekTo(ms * 1000 + playback.MediaBaseUs);
            return Ok();
        }

        [HttpPost]
        public IActionResult Close(string id)
        {
            UmpPlaybackRegistry.Release(id);
            return Ok();
        }
    }
}
