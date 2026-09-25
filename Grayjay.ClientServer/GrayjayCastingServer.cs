using System.Globalization;
using System.Net;
using Grayjay.ClientServer.Controllers;
using Grayjay.ClientServer.Proxy;
using Grayjay.ClientServer.Sabr;
using Grayjay.ClientServer.Sabr.Cast;
using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC;
using Grayjay.Engine.Models.Video.Additions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using SQLitePCL;
using static Grayjay.ClientServer.Controllers.DetailsController;

namespace Grayjay.ClientServer
{
    public class GrayjayCastingServer
    {
        private static object _instanceLockObject = new object();
        private static GrayjayCastingServer? _instance;
        public static GrayjayCastingServer Instance
        {
            get 
            {
                lock (_instanceLockObject)
                {
                    if (_instance == null)
                    {
                        _instance = new GrayjayCastingServer();
                        _instance._app.RunAsync();
                    }
                    return _instance;
                }
            }
        }

        public static async Task StopAsync()
        {
            GrayjayCastingServer? instance = null;
            lock (_instanceLockObject)
            {
                if (_instance != null)
                {
                    instance = _instance;
                    _instance = null;
                }
            }

            if (instance != null)
                await instance.StopServerAsync();
        }

        private WebApplication _app;
        public Uri? BaseUri { get; private set; } = null;
        public string? BaseUrl { get; private set; } = null;

        public GrayjayCastingServer()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                serverOptions.Listen(IPAddress.Any, 0);
            });

            _app = builder.Build();

            _app.Lifetime.ApplicationStarted.Register(() => 
            {
                var server = _app.Services.GetRequiredService<IServer>();
                var addressFeature = server.Features.Get<IServerAddressesFeature>()!;
                var address = addressFeature.Addresses.First();

                Logger.i(nameof(GrayjayCastingServer), $"RunServerAsync: Server running on {address}.");
                BaseUri = new Uri(address);
                BaseUrl = address;
                if (BaseUrl.EndsWith('/'))
                    BaseUrl = BaseUrl.Substring(0, BaseUrl.Length - 1);
            });

            AddCorsHandler("/ump/cast/manifest", [ "GET", "HEAD", "OPTIONS" ]);
            _app.MapMethods("/ump/cast/manifest", [ "GET", "HEAD" ], (HttpContext context, string id) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";
                context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                var cast = UmpCasting.Get(id);
                if (cast == null)
                    return Results.NotFound();
                var manifest = UmpCasting.ManifestFor(cast);
                if (string.IsNullOrEmpty(manifest))
                    return Results.StatusCode(503);
                return Results.Content(manifest, "application/dash+xml");
            });
            AddCorsHandler("/ump/cast/init", [ "GET", "OPTIONS" ]);
            _app.MapGet("/ump/cast/init", async (HttpContext context, string id, string role) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";
                var cast = UmpCasting.Get(id);
                if (cast?.Proxy == null)
                    return Results.NotFound();
                var roleId = role == "audio" ? SabrSession.ROLE_AUDIO : SabrSession.ROLE_VIDEO;
                var format = roleId == SabrSession.ROLE_AUDIO ? cast.Proxy.AudioFormat : cast.Proxy.VideoFormat;
                var data = await cast.Proxy.GetInitAsync(roleId);
                if (data == null || format == null)
                    return Results.StatusCode(504);
                return Results.Bytes(data, format.ContainerMimeType);
            });
            AddCorsHandler("/ump/cast/seg", [ "GET", "OPTIONS" ]);
            _app.MapGet("/ump/cast/seg", async (HttpContext context, string id, string role, int n) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";
                var cast = UmpCasting.Get(id);
                if (cast?.Proxy == null)
                    return Results.NotFound();
                var roleId = role == "audio" ? SabrSession.ROLE_AUDIO : SabrSession.ROLE_VIDEO;
                var format = roleId == SabrSession.ROLE_AUDIO ? cast.Proxy.AudioFormat : cast.Proxy.VideoFormat;
                var data = await cast.Proxy.GetSegmentAsync(roleId, n);
                if (data == null || format == null)
                    return Results.NotFound();
                return Results.Bytes(data, format.ContainerMimeType);
            });
            AddCorsHandler("/ump/cast/sub", [ "GET", "OPTIONS" ]);
            _app.MapGet("/ump/cast/sub", (HttpContext context, string id) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";
                var cast = UmpCasting.Get(id);
                if (cast?.SubtitleBytes == null)
                    return Results.NotFound();
                return Results.Bytes(cast.SubtitleBytes, cast.SubtitleContentType ?? "text/vtt");
            });
            AddCorsHandler("/ump/cast/time", [ "GET", "OPTIONS" ]);
            _app.MapGet("/ump/cast/time", (HttpContext context) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";
                context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                return Results.Text(DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture), "text/plain");
            });

            AddCorsHandler("/details/SourceDash", [ "GET", "HEAD", "OPTIONS" ]);
            _app.MapMethods("/details/SourceDash", [ "HEAD" ], async (HttpContext context, int videoIndex, int audioIndex, int subtitleIndex, bool videoIsLocal = false, bool audioIsLocal = false, bool subtitleIsLocal = false) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";

                var activeDevice = StateCasting.Instance.ActiveDevice;
                if (activeDevice == null)
                    return Results.BadRequest("No active casting device.");

                //TODO: Make sure this does not get recalled every HEAD/GET call
                (var task, var metadata) = DetailsController.GenerateSourceDash(context.GetState(), videoIndex, audioIndex, subtitleIndex, videoIsLocal, audioIsLocal, subtitleIsLocal, new ProxySettings(false, proxyAddress: activeDevice.LocalEndPoint?.Address));
                var contentLength = (await task).Length;
                context.Response.Headers["Content-Length"] = contentLength.ToString();
                context.Response.Headers["Content-Type"] = "application/dash+xml";
                return Results.StatusCode(200);
            });
            _app.MapGet("/details/SourceDash", async (HttpContext context, int videoIndex, int audioIndex, int subtitleIndex, bool videoIsLocal = false, bool audioIsLocal = false, bool subtitleIsLocal = false) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";

                var activeDevice = StateCasting.Instance.ActiveDevice;
                if (activeDevice == null)
                    return Results.BadRequest("No active casting device.");

                (var task, var metadata) = DetailsController.GenerateSourceDash(context.GetState(), videoIndex, audioIndex, subtitleIndex, videoIsLocal, audioIsLocal, subtitleIsLocal, new ProxySettings(false, proxyAddress: activeDevice.LocalEndPoint?.Address, exposeLocalAsAny: true));
                return Results.Content(await task, "application/dash+xml");
            });

            AddCorsHandler("/details/SourceHLS", [ "GET", "HEAD", "OPTIONS" ]);

            _app.MapMethods("/details/SourceHLS", [ "HEAD" ], async (HttpContext context, int videoIndex = -1, int audioIndex = -1, int subtitleIndex = -1, bool subtitleIsLocal = false, string? modifierId = null) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";

                var activeDevice = StateCasting.Instance.ActiveDevice;
                if (activeDevice == null)
                    return Results.BadRequest("No active casting device.");

                var content = await DetailsController.GenerateSourceHLS(context.GetState(), videoIndex, audioIndex, subtitleIndex, subtitleIsLocal, new ProxySettings(false, proxyAddress: activeDevice.LocalEndPoint?.Address), modifierId);
                context.Response.Headers["Content-Length"] = content.Length.ToString();
                context.Response.Headers["Content-Type"] = "application/x-mpegurl";
                return Results.StatusCode(200);
            });

            _app.MapGet("/details/SourceHLS", async (HttpContext context, int videoIndex = -1, int audioIndex = -1, int subtitleIndex = -1, bool subtitleIsLocal = false, string? modifierId = null) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";

                var activeDevice = StateCasting.Instance.ActiveDevice;
                if (activeDevice == null)
                    return Results.BadRequest("No active casting device.");

                var content = await DetailsController.GenerateSourceHLS(context.GetState(), videoIndex, audioIndex, subtitleIndex, subtitleIsLocal, new ProxySettings(false, proxyAddress: activeDevice.LocalEndPoint?.Address, exposeLocalAsAny: true), modifierId);
                return Results.Content(content, "application/x-mpegurl");
            });
            
            AddCorsHandler("/proxy/HLS", [ "GET", "HEAD", "OPTIONS" ]);
            _app.MapMethods("/proxy/HLS", [ "HEAD" ], async (HttpContext context, string url, bool proxyMedia, string? modifierId = null) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";

                var activeDevice = StateCasting.Instance.ActiveDevice;
                if (activeDevice == null)
                    return Results.BadRequest("No active casting device.");

                IRequestModifier? modifier = null;
                if (!string.IsNullOrEmpty(modifierId))
                    DetailsState.Modifiers.TryGetValue(modifierId, out modifier);
                var playlist = await ProxyController.GenerateProxiedHLS(
                    url,
                    proxyMedia,
                    $"{context.Request.Scheme}://{context.Request.Host.Value}",
                    state: null,
                    modifier: modifier,
                    modifierId: modifierId
                );
                var contentLength = playlist.GenerateM3U8().Length;
                context.Response.Headers["Content-Length"] = contentLength.ToString();
                context.Response.Headers["Content-Type"] = "application/x-mpegurl";
                return Results.StatusCode(200);
            });
            _app.MapGet("/proxy/HLS", async (HttpContext context, string url, bool proxyMedia, string? modifierId = null) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";

                var activeDevice = StateCasting.Instance.ActiveDevice;
                if (activeDevice == null)
                    return Results.BadRequest("No active casting device.");

                IRequestModifier? modifier = null;
                if (!string.IsNullOrEmpty(modifierId))
                    DetailsState.Modifiers.TryGetValue(modifierId, out modifier);
            
                var playlist = await ProxyController.GenerateProxiedHLS(
                    url,
                    proxyMedia,
                    $"{context.Request.Scheme}://{context.Request.Host.Value}",
                    state: null,
                    modifier: modifier,
                    modifierId: modifierId
                );
            
                return Results.Content(playlist.GenerateM3U8(), "application/x-mpegurl");
            });


            AddCorsHandler("/details/StreamLocalVideoSource", [ "GET", "HEAD", "OPTIONS" ]);
            _app.MapMethods("/details/StreamLocalVideoSource", [ "HEAD" ], (HttpContext context, int index) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";

                var local = DetailsController.EnsureLocal(context.GetState());
                var source = local.VideoSources[index];
                var contentLength = new FileInfo(source.FilePath).Length;

                context.Response.Headers["Content-Length"] = contentLength.ToString();
                context.Response.Headers["Content-Type"] = source.Container;
                return Results.StatusCode(200);
            });
            _app.MapGet("/details/StreamLocalVideoSource", (HttpContext context, int index) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";

                var local = DetailsController.EnsureLocal(context.GetState());
                var source = local.VideoSources[index];
                var stream = new FileStream(source.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return Results.File(stream, source.Container, enableRangeProcessing: true);
            });

            AddCorsHandler("/details/StreamLocalAudioSource", [ "GET", "HEAD", "OPTIONS" ]);
            _app.MapMethods("/details/StreamLocalAudioSource", [ "HEAD" ], (HttpContext context, int index) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";

                var local = DetailsController.EnsureLocal(context.GetState());
                var source = local.AudioSources[index];
                var contentLength = new FileInfo(source.FilePath).Length;
                
                context.Response.Headers["Content-Length"] = contentLength.ToString();
                context.Response.Headers["Content-Type"] = source.Container;
                return Results.StatusCode(200);
            });
            _app.MapGet("/details/StreamLocalAudioSource", (HttpContext context, int index) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";

                var local = DetailsController.EnsureLocal(context.GetState());
                var source = local.AudioSources[index];
                var stream = new FileStream(source.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return Results.File(stream, source.Container, enableRangeProcessing: true);
            });

            AddCorsHandler("/details/StreamLocalSubtitleSource", [ "GET", "HEAD", "OPTIONS" ]);
            _app.MapMethods("/details/StreamLocalSubtitleSource", [ "HEAD" ], (HttpContext context, int index) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";

                var local = DetailsController.EnsureLocal(context.GetState());
                var source = local.SubtitleSources[index];
                var contentLength = new FileInfo(source.FilePath).Length;

                context.Response.Headers["Content-Length"] = contentLength.ToString();
                context.Response.Headers["Content-Type"] = source.Format;
                return Results.StatusCode(200);
            });
            _app.MapGet("/details/StreamLocalSubtitleSource", (HttpContext context, int index) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";

                var local = DetailsController.EnsureLocal(context.GetState());
                var source = local.SubtitleSources[index];
                var stream = new FileStream(source.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return Results.File(stream, source.Format, enableRangeProcessing: true);
            });

            AddCorsHandler("/details/StreamSubtitleFile", [ "GET", "HEAD", "OPTIONS" ]);
            _app.MapMethods("/details/StreamSubtitleFile", [ "HEAD" ], (HttpContext context, int index) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";
                var video = DetailsController.EnsureVideo(context.GetState());
                var source = video.Subtitles[index];
                var uri = source.GetSubtitlesUri()!;
                if (uri.Scheme != "file")
                    throw new InvalidOperationException("Must be a file URI.");

                var contentLength = new FileInfo(uri.AbsolutePath).Length;
                context.Response.Headers["Content-Length"] = contentLength.ToString();
                context.Response.Headers["Content-Type"] = source.Format;
                return Results.StatusCode(200);
            });
            _app.MapGet("/details/StreamSubtitleFile", (HttpContext context, int index) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";

                var video = DetailsController.EnsureVideo(context.GetState());
                var source = video.Subtitles[index];
                var uri = source.GetSubtitlesUri()!;
                if (uri.Scheme != "file")
                    throw new InvalidOperationException("Must be a file URI.");
                var stream = new FileStream(uri.AbsolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return Results.File(stream, source.Format, enableRangeProcessing: true);
            });

            AddCorsHandler("/details/Subtitle", [ "GET", "HEAD", "OPTIONS" ]);

            _app.MapMethods("/details/Subtitle", [ "HEAD" ], async (HttpContext context, int subtitleIndex, bool subtitleIsLocal = false, string? modifierId = null) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";

                var (bytes, contentType) = await DetailsController.GetSubtitleBytesAsync(context.GetState(), subtitleIndex, subtitleIsLocal, modifierId);
                context.Response.Headers["Content-Length"] = bytes.Length.ToString();
                context.Response.Headers["Content-Type"] = contentType;
                return Results.StatusCode(200);
            });

            _app.MapGet("/details/Subtitle", async (HttpContext context, int subtitleIndex, bool subtitleIsLocal = false, string? modifierId = null) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";

                var (bytes, contentType) = await DetailsController.GetSubtitleBytesAsync(context.GetState(), subtitleIndex, subtitleIsLocal, modifierId);
                return Results.File(bytes, contentType);
            });

            AddCorsHandler("/details/SubtitleHLS", [ "GET", "HEAD", "OPTIONS" ]);

            _app.MapMethods("/details/SubtitleHLS", [ "HEAD" ], (
                HttpContext context,
                int subtitleIndex,
                bool subtitleIsLocal = false,
                string? modifierId = null) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";

                var state = context.GetState();
                var baseUri = $"{context.Request.Scheme}://{context.Request.Host.Value}";
                var subtitleUrl = BuildSubtitleUrl(baseUri, state, subtitleIndex, subtitleIsLocal, modifierId);

                var dur = GetVideoDurationSecondsOrFallback(state);
                var m3u8 = GenerateSingleSegmentSubtitlePlaylist(subtitleUrl, dur);

                context.Response.Headers["Content-Length"] = m3u8.Length.ToString();
                context.Response.Headers["Content-Type"] = "application/x-mpegurl";
                return Results.StatusCode(200);
            });

            _app.MapGet("/details/SubtitleHLS", (
                HttpContext context,
                int subtitleIndex,
                bool subtitleIsLocal = false,
                string? modifierId = null) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";

                var state = context.GetState();
                var baseUri = $"{context.Request.Scheme}://{context.Request.Host.Value}";
                var subtitleUrl = BuildSubtitleUrl(baseUri, state, subtitleIndex, subtitleIsLocal, modifierId);

                var dur = GetVideoDurationSecondsOrFallback(state);
                var m3u8 = GenerateSingleSegmentSubtitlePlaylist(subtitleUrl, dur);

                return Results.Content(m3u8, "application/x-mpegurl");
            });


        }

        private static string BuildSubtitleUrl(string baseUri, WindowState state, int subtitleIndex, bool subtitleIsLocal, string? modifierId)
        {
            var url = $"{baseUri}/Details/Subtitle?subtitleIndex={subtitleIndex}" +
                    $"&subtitleIsLocal={subtitleIsLocal}" +
                    $"&windowId={state.WindowID}";

            if (!string.IsNullOrEmpty(modifierId))
                url += $"&modifierId={Uri.EscapeDataString(modifierId)}";

            return url;
        }

        private static double GetVideoDurationSecondsOrFallback(WindowState state)
        {
            var v = state?.DetailsState?.VideoLoaded;
            if (v == null)
                return 60.0;

            static double? ReadDuration(object obj)
            {
                foreach (var propName in new[] { "DurationSeconds", "Duration" })
                {
                    var p = obj.GetType().GetProperty(propName);
                    if (p == null) continue;

                    var val = p.GetValue(obj);
                    if (val is int i) return i;
                    if (val is long l) return l;
                    if (val is double d) return d;
                    if (val is float f) return f;
                }
                return null;
            }

            var d1 = ReadDuration(v);
            if (d1.HasValue && d1.Value > 0) return d1.Value;

            var inner = v.GetType().GetProperty("Video")?.GetValue(v);
            if (inner != null)
            {
                var d2 = ReadDuration(inner);
                if (d2.HasValue && d2.Value > 0) return d2.Value;
            }

            return 60.0;
        }

        private static string GenerateSingleSegmentSubtitlePlaylist(string subtitleUrl, double durationSeconds)
        {
            if (durationSeconds <= 0)
                durationSeconds = 60.0;

            var targetDuration = Math.Max(1, (int)Math.Ceiling(durationSeconds));
            var dur = durationSeconds.ToString(CultureInfo.InvariantCulture);

            return
                "#EXTM3U\n" +
                "#EXT-X-VERSION:3\n" +
                $"#EXT-X-TARGETDURATION:{targetDuration}\n" +
                "#EXT-X-MEDIA-SEQUENCE:0\n" +
                $"#EXTINF:{dur},\n" +
                subtitleUrl + "\n" +
                "#EXT-X-ENDLIST\n";
        }

        private void AddCorsHandler(string url, string[] methods)
        {
            _app.MapMethods(url, [ "OPTIONS" ], context =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";
                context.Response.Headers["Access-Control-Allow-Methods"] = string.Join(", ", methods);
                context.Response.Headers["Access-Control-Allow-Headers"] = "*";
                context.Response.StatusCode = 204;
                return Task.CompletedTask;
            });
        }

        public async Task StopServerAsync()
        {
            await _app.StopAsync();
        }
    }
}
