using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using Grayjay.ClientServer.Exceptions;
using Grayjay.ClientServer.Models.Downloads;
using Grayjay.ClientServer.Pagers;
using Grayjay.ClientServer.States;
using Grayjay.Engine.Models.Detail;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Models.Video;
using Grayjay.Engine.Models.Video.Sources;
using Grayjay.Engine.Pagers;
using Grayjay.Engine.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlatformID = Grayjay.Engine.Models.General.PlatformID;

namespace Grayjay.Desktop.Tests;

[TestClass]
[DoNotParallelize]
public class DownloadPreparationStallTests
{
    private const string MasterPlaylist = "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360\nvideo/360.m3u8\n";
    private const string MediaPlaylist = "#EXTM3U\n#EXT-X-TARGETDURATION:4\n#EXTINF:4.0,\nsegment0.ts\n#EXT-X-ENDLIST\n";
    private static readonly TimeSpan ShortIdle = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan LongIdle = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    private readonly TaskCompletionSource _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<(WebApplication App, string BaseUrl)> StartServer(Func<HttpContext, Task> handler)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.Run(new RequestDelegate(handler));
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return (app, address);
    }

    private async Task StopServer(WebApplication app)
    {
        _release.TrySetResult();
        await app.StopAsync();
        await app.DisposeAsync();
    }

    //Sends the headers and the first line, then holds the body until the test releases it
    private async Task WritePartialThenStall(HttpContext context, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        int firstLine = text.IndexOf('\n') + 1;
        context.Response.ContentType = "application/vnd.apple.mpegurl";
        context.Response.ContentLength = bytes.Length;
        await context.Response.Body.WriteAsync(bytes.AsMemory(0, firstLine));
        await context.Response.Body.FlushAsync();
        await Task.WhenAny(_release.Task, Task.Delay(Timeout.Infinite, context.RequestAborted));
        if (!context.RequestAborted.IsCancellationRequested)
        {
            await context.Response.Body.WriteAsync(bytes.AsMemory(firstLine));
        }
    }

    private static async Task WriteSlowly(HttpContext context, string text, int chunks, TimeSpan delay)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        context.Response.ContentType = "application/vnd.apple.mpegurl";
        context.Response.ContentLength = bytes.Length;
        int offset = 0;
        for (int chunk = 0; chunk < chunks; chunk++)
        {
            int end = (chunk == chunks - 1) ? bytes.Length : bytes.Length * (chunk + 1) / chunks;
            await context.Response.Body.WriteAsync(bytes.AsMemory(offset, end - offset));
            await context.Response.Body.FlushAsync();
            offset = end;
            if (chunk < chunks - 1)
            {
                await Task.Delay(delay);
            }
        }
    }

    private static PlatformVideoDetails Details(VideoDescriptor descriptor, string url = "https://test/video")
    {
        return new PlatformVideoDetails
        {
            ID = new PlatformID { Value = "preparation-stall-" + Guid.NewGuid().ToString("N") },
            Name = "Preparation stall",
            Url = url,
            Video = descriptor,
            Subtitles = Array.Empty<Grayjay.Engine.Models.Subtitles.SubtitleSource>()
        };
    }

    private static VideoDescriptor HlsVideo(string baseUrl) => new VideoDescriptor
    {
        VideoSources = new IVideoSource[] { new HLSManifestSource { Url = baseUrl + "/master.m3u8" } }
    };

    private static UnMuxedVideoDescriptor HlsAudio(string baseUrl) => new UnMuxedVideoDescriptor
    {
        VideoSources = Array.Empty<IVideoSource>(),
        AudioSources = new IAudioSource[] { new HLSManifestAudioSource { Url = baseUrl + "/audio.m3u8", Language = "en", Bitrate = 96000 } }
    };

    private static void SetIdleTimeout(VideoDownload download, TimeSpan timeout)
    {
        typeof(VideoDownload).GetProperty("ReadIdleTimeout", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(download, timeout);
    }

    private static void SelectSources(VideoDownload download, PlatformVideoDetails details, CancellationToken cancel)
    {
        var method = typeof(VideoDownload).GetMethod("SelectSources", BindingFlags.NonPublic | BindingFlags.Instance)!;
        try
        {
            method.Invoke(download, new object?[] { new ManagedHttpClient(), details, cancel });
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static Task DownloadVideo(VideoDownload download)
    {
        var method = typeof(StateDownloads).GetMethod("DownloadVideo", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (Task)method.Invoke(null, new object[] { download })!;
    }

    //Fails the test instead of hanging when the operation is still blocked at the deadline
    private async Task<Exception?> CompletesWithin(Func<Task> operation)
    {
        var running = Task.Run(operation);
        var finished = await Task.WhenAny(running, Task.Delay(Deadline));
        if (finished != running)
        {
            _release.TrySetResult();
            await running.ContinueWith(_ => { });
            Assert.Fail($"Still blocked after {Deadline.TotalSeconds} seconds");
        }
        try
        {
            await running;
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    [TestMethod]
    public async Task MasterManifestStallIsBoundedByTheIdleTimeout()
    {
        var (app, baseUrl) = await StartServer(context => WritePartialThenStall(context, MasterPlaylist));
        try
        {
            var details = Details(HlsVideo(baseUrl));
            var download = new VideoDownload(details, 640 * 360, null);
            SetIdleTimeout(download, ShortIdle);

            var error = await CompletesWithin(() => Task.Run(() => SelectSources(download, details, CancellationToken.None)));

            Assert.IsInstanceOfType(error, typeof(DownloadException), error?.ToString());
            Assert.IsNull(download.VideoSource);
        }
        finally
        {
            await StopServer(app);
        }
    }

    [TestMethod]
    public async Task AudioManifestStallIsBoundedByTheIdleTimeout()
    {
        var (app, baseUrl) = await StartServer(context => WritePartialThenStall(context, MediaPlaylist));
        try
        {
            var details = Details(HlsAudio(baseUrl));
            var download = new VideoDownload(details, null, 96000);
            SetIdleTimeout(download, ShortIdle);

            var error = await CompletesWithin(() => Task.Run(() => SelectSources(download, details, CancellationToken.None)));

            Assert.IsInstanceOfType(error, typeof(DownloadException), error?.ToString());
            Assert.IsNull(download.AudioSource);
        }
        finally
        {
            await StopServer(app);
        }
    }

    [TestMethod]
    public async Task SelectedAudioManifestStallIsBoundedDuringDownload()
    {
        var (app, baseUrl) = await StartServer(context => WritePartialThenStall(context, MediaPlaylist));
        try
        {
            var details = Details(HlsAudio(baseUrl));
            var download = new VideoDownload(details, null, new HLSManifestAudioSource { Url = baseUrl + "/audio.m3u8", Language = "en" });
            SetIdleTimeout(download, ShortIdle);

            var error = await CompletesWithin(() => download.Download(new ManagedHttpClient(), _ => { }, CancellationToken.None));

            Assert.IsInstanceOfType(error, typeof(DownloadException), error?.ToString());
        }
        finally
        {
            await StopServer(app);
        }
    }

    [TestMethod]
    public async Task SlowButSteadyManifestsStillSucceed()
    {
        var (app, baseUrl) = await StartServer(context => WriteSlowly(context, context.Request.Path == "/audio.m3u8" ? MediaPlaylist : MasterPlaylist, 8, TimeSpan.FromMilliseconds(200)));
        try
        {
            var videoDetails = Details(HlsVideo(baseUrl));
            var videoDownload = new VideoDownload(videoDetails, 640 * 360, null);
            SetIdleTimeout(videoDownload, ShortIdle);
            var stopwatch = Stopwatch.StartNew();
            SelectSources(videoDownload, videoDetails, CancellationToken.None);
            Assert.IsTrue(stopwatch.Elapsed > ShortIdle, "The body must take longer than the idle timeout");
            Assert.AreEqual(baseUrl + "/video/360.m3u8", (videoDownload.VideoSource as HLSVariantVideoUrlSource)?.Url);

            var audioDetails = Details(HlsAudio(baseUrl));
            var audioDownload = new VideoDownload(audioDetails, null, 96000);
            SetIdleTimeout(audioDownload, ShortIdle);
            SelectSources(audioDownload, audioDetails, CancellationToken.None);
            Assert.AreEqual(baseUrl + "/audio.m3u8", (audioDownload.AudioSource as HLSVariantAudioUrlSource)?.Url);
        }
        finally
        {
            await StopServer(app);
        }
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancelInterruptsManifestStall(bool audio)
    {
        var (app, baseUrl) = await StartServer(context => WritePartialThenStall(context, audio ? MediaPlaylist : MasterPlaylist));
        try
        {
            var details = Details(audio ? HlsAudio(baseUrl) : HlsVideo(baseUrl));
            var download = audio ? new VideoDownload(details, null, 96000) : new VideoDownload(details, 640 * 360, null);
            SetIdleTimeout(download, LongIdle);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

            var error = await CompletesWithin(() => Task.Run(() => SelectSources(download, details, cancellation.Token)));

            Assert.IsInstanceOfType(error, typeof(OperationCanceledException), error?.ToString());
        }
        finally
        {
            await StopServer(app);
        }
    }

    [TestMethod]
    public async Task CancellingAQueuedDownloadInterruptsPreparation()
    {
        var (app, baseUrl) = await StartServer(context => WritePartialThenStall(context, MasterPlaylist));
        try
        {
            var details = Details(HlsVideo(baseUrl));
            var pager = new RefPager<PlatformContent>(new AdhocPager<PlatformContent>(page => Array.Empty<PlatformContent>(), new PlatformContent[] { details }));
            details.Url = $"https://grayjay.internal/refPager?pagerId={pager.ID}&itemId={pager.PreviousResults[0].RefID}";
            var download = new VideoDownload(details, 640 * 360, null);
            SetIdleTimeout(download, LongIdle);

            var stopwatch = Stopwatch.StartNew();
            var error = await CompletesWithin(async () =>
            {
                var running = Task.Run(() => DownloadVideo(download));
                await Task.Delay(300);
                download.Cancel();
                await running;
            });

            Assert.IsInstanceOfType(error, typeof(OperationCanceledException), error?.ToString());
            Assert.AreEqual(DownloadState.PREPARING, download.State);
            GC.KeepAlive(pager);
        }
        finally
        {
            await StopServer(app);
        }
    }
}
