using System.Net;
using Grayjay.ClientServer.ExceptionHandlers;
using Grayjay.ClientServer.Proxy;
using Grayjay.ClientServer.States;
using Grayjay.ClientServer.WebSockets;
using System.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Grayjay.Desktop.POC;
using Grayjay.ClientServer.Settings;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Grayjay.ClientServer
{
    public class GrayjayServer
    {
        private const bool ShowAspLogs = false;

        WebApplication _app;

        public static GrayjayServer Instance { get; private set; }

        public IWindowProvider WindowProvider { get; private set; }
        public IWindowProvider GetWindowProviderOrThrow() => WindowProvider ?? throw new InvalidOperationException("No WindowProvider set");
        public bool ServerMode = true;
        public bool HeadlessMode = true;

        public WebSocketEndpoint WebSocket { get; private set; } = new WebSocketEndpoint("/ws");
        public ManualResetEventSlim StartedResetEvent = new ManualResetEventSlim(false);
        public Uri? BaseUri { get; private set; } = null;
        public string? BaseUrl { get; private set; } = null;

        public GrayjayServer(IWindowProvider windowProvider = null, bool isHeadlessMode = false, bool isServerMode = false)
        {
            WindowProvider = windowProvider;
            HeadlessMode = isHeadlessMode;
            ServerMode = isServerMode;
            Instance = this;
        }

        public async Task RunServerAsync(string proxyUrl = null, CancellationToken cancellationToken = default)
        {
            Logger.i(nameof(GrayjayServer), $"RunServerAsync: Called with (proxyUrl = {proxyUrl}).");

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                if (ServerMode)
                {
                    Logger.Info<GrayjayServer>("Running in Server mode, listening to all ips on port 11338");
                    serverOptions.Listen(IPAddress.Any, 11338);
                }
                else
                    serverOptions.Listen(IPAddress.Loopback, 0);
            });

            builder.Services
                .AddExceptionHandler<ScriptExceptionHandler>()
                .AddLogging((logBuilder) =>
                {
                    logBuilder.ClearProviders();
                    logBuilder.AddProvider(new GrayjayLoggerProvider());
                })
                .AddControllers()
                /*
                .AddJsonOptions((options) =>
                {
                    var converters = GJsonSerializer.Options.Converters;
                    foreach (var converter in converters)
                        options.JsonSerializerOptions.Converters.Add(converter);
                })*/
                .AddApplicationPart(typeof(GrayjayServer).Assembly);
            _app = builder.Build();

            _app.UseExceptionHandler(o => { });
            _app.UseWebSockets();
            _app.UseRouting();
            _app.MapControllers();
            _app.Lifetime.ApplicationStarted.Register(() => 
            {
                var server = _app.Services.GetRequiredService<IServer>();
                var addressFeature = server.Features.Get<IServerAddressesFeature>()!;
                var address = addressFeature.Addresses.First();

                Logger.i(nameof(GrayjayServer), $"RunServerAsync: Server running on {address}.");
                BaseUri = new Uri(address);
                BaseUrl = address;
                if (BaseUrl.EndsWith('/'))
                    BaseUrl = BaseUrl.Substring(0, BaseUrl.Length - 1);

                StartedResetEvent.Set();
            });

            _app.MapGet("/", () => Results.Redirect("/web/index.html"));
            _app.MapGet("/dev", () => Results.Redirect("/Developer/Index"));
            _app.MapGet("/Developer/source.js", () => Results.Redirect("/Developer/Source"));
            _app.MapGet("/Developer/dev_bridge.js", () => Results.Redirect("/Developer/DevBridge"));
            _app.MapGet("/Developer/source_docs.js", () => Results.Redirect("/Developer/SourceDocs"));
            _app.MapGet("/Developer/source_doc_urls.js", () => Results.Redirect("/Developer/SourceDocUrls"));

            if (proxyUrl != null)
            {
                _app.MapWhen((context) => context.Request.Path.StartsWithSegments("/web"), (builder) =>
                {
                    builder.Run(async (context) =>
                    {
                        string query = context.Request.QueryString.Value;
                        string url = proxyUrl + context.Request.Path + ((string.IsNullOrEmpty(query)) ? "" : "?" + query);

                        using (HttpClient client = new HttpClient())
                        {
                            foreach (var header in context.Request.Headers)
                                client.DefaultRequestHeaders.Add(header.Key, header.Value.ToList());
                            HttpResponseMessage resp = await client.GetAsync(url);
                            int code = (int)resp.StatusCode;
                            if (code != 200)
                                context.Response.StatusCode = code;
                            else
                            {
                                context.Response.ContentType = resp.Content.Headers.ContentType.MediaType;
                                context.Response.Headers.Add("Referrer-Policy", "no-referrer");
                                await resp.Content.ReadAsStream().CopyToAsync(context.Response.Body);
                            }
                        }
                    });
                });
            }
            else
            {
                string staticFilesPath = Path.GetFullPath("wwwroot");
                if (OperatingSystem.IsMacOS())
                {
                    string? executableDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
                    if (!File.Exists(staticFilesPath) && executableDirectory != null)
                        staticFilesPath = Path.Combine(executableDirectory, "wwwroot");
                    if (!File.Exists(staticFilesPath) && executableDirectory != null)
                        staticFilesPath = Path.Combine(executableDirectory, "../Resources/wwwroot");
                }
                if (OperatingSystem.IsLinux())
                {
                    string? executableDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
                    if (!File.Exists(staticFilesPath) && executableDirectory != null)
                        staticFilesPath = Path.Combine(executableDirectory, "wwwroot");
                }

                Logger.i(nameof(GrayjayServer), $"RunServerAsync: Static files path '" + Path.GetFullPath(staticFilesPath) + "'.");
                _app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(staticFilesPath),
                    OnPrepareResponse = (act) =>
                    {
                        act.Context.Response.Headers.Append("Referrer-Policy", "no-referrer");
                    }
                });

                _app.MapFallback(context =>
                {
                    context.Response.ContentType = "text/html";
                    return context.Response.SendFileAsync(Path.Combine(staticFilesPath, "web", "index.html"));
                });
            }

            Logger.i(nameof(GrayjayServer), $"RunServerAsync: MapWhen Websocket.");
            _app.MapWhen(x => x.Request.Path.StartsWithSegments(WebSocket.Url), (builder) =>
            {
                builder.Run(async (context) =>
                {
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        var websocket = await context.WebSockets.AcceptWebSocketAsync();

                        //TODO: New thread
                        await WebSocket.HandleClient(websocket);
                    }
                    else
                        context.Response.StatusCode = 400;
                });
            });

            Logger.i(nameof(GrayjayServer), $"RunServerAsync: Start StateCasting.");
            //TODO: Start everything
            StateCasting.Instance.Start();
            if (GrayjaySettings.Instance.Synchronization.Enabled)
                await StateSync.Instance.StartAsync();

            _app.UseMiddleware<RequestLoggingMiddleware>();
            await Task.WhenAll(_app.RunAsync(cancellationToken), StateDownloads.StartDownloadCycle());
        }
        public async Task StopServer()
        {
            //TODO: Stop other things?
            WebSocket.Dispose();
            StateSync.Cleanup();
            StateCasting.Instance.Dispose();
            await GrayjayCastingServer.StopAsync();
            await _app.StopAsync();
            HttpProxy.Stop();
        }
    }
}
