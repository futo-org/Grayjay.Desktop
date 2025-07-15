using System.Net;
using Grayjay.ClientServer.Controllers;
using Grayjay.ClientServer.Proxy;
using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using SQLitePCL;

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
            _app.MapMethods("/details/SourceHLS", [ "HEAD" ], async (HttpContext context, int videoIndex = -1, int audioIndex = -1, bool videoIsLocal = false, bool audioIsLocal = false, bool subtitleIsLocal = false) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";

                var activeDevice = StateCasting.Instance.ActiveDevice;
                if (activeDevice == null)
                    return Results.BadRequest("No active casting device.");

                var contentLength = (await DetailsController.GenerateSourceHLS(context.GetState(), videoIndex, audioIndex, new ProxySettings(false, proxyAddress: activeDevice.LocalEndPoint?.Address))).Length;
                context.Response.Headers["Content-Length"] = contentLength.ToString();
                context.Response.Headers["Content-Type"] = "application/x-mpegurl";
                return Results.StatusCode(200);
            });
            _app.MapGet("/details/SourceHLS", async (HttpContext context, int videoIndex = -1, int audioIndex = -1, bool videoIsLocal = false, bool audioIsLocal = false, bool subtitleIsLocal = false) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";

                var activeDevice = StateCasting.Instance.ActiveDevice;
                if (activeDevice == null)
                    return Results.BadRequest("No active casting device.");

                return Results.Content(await DetailsController.GenerateSourceHLS(context.GetState(), videoIndex, audioIndex, new ProxySettings(false, proxyAddress: activeDevice.LocalEndPoint?.Address, exposeLocalAsAny: true)), "application/x-mpegurl");
            });

            AddCorsHandler("/proxy/HLS", [ "GET", "HEAD", "OPTIONS" ]);
            _app.MapMethods("/proxy/HLS", [ "HEAD" ], async (HttpContext context, string url, bool proxyMedia) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";

                var activeDevice = StateCasting.Instance.ActiveDevice;
                if (activeDevice == null)
                    return Results.BadRequest("No active casting device.");

                var proxy = HttpProxy.Get(false);
                var contentLength = (await ProxyController.GenerateProxiedHLS(url, proxyMedia, $"{context.Request.Scheme}://{context.Request.Host.Value}")).GenerateM3U8().Length;
                context.Response.Headers["Content-Length"] = contentLength.ToString();
                context.Response.Headers["Content-Type"] = "application/x-mpegurl";
                return Results.StatusCode(200);
            });
            _app.MapGet("/proxy/HLS", async (HttpContext context, string url, bool proxyMedia) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";

                var activeDevice = StateCasting.Instance.ActiveDevice;
                if (activeDevice == null)
                    return Results.BadRequest("No active casting device.");

                return Results.Content((await ProxyController.GenerateProxiedHLS(url, proxyMedia, $"{context.Request.Scheme}://{context.Request.Host.Value}")).GenerateM3U8(), "application/x-mpegurl");
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
