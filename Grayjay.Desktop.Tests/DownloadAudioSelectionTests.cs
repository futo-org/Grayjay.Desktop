using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using Grayjay.ClientServer.Controllers;
using Grayjay.ClientServer.Exceptions;
using Grayjay.ClientServer.Models.Downloads;
using Grayjay.ClientServer.Parsers;
using Grayjay.ClientServer.Serializers;
using Grayjay.ClientServer.States;
using Grayjay.ClientServer.Transcoding;
using Grayjay.Engine.Models.Detail;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Models.Video;
using Grayjay.Engine.Models.Video.Additions;
using Grayjay.Engine.Models.Video.Sources;
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
public class DownloadAudioSelectionTests
{
    private const string MasterWithAudioGroup = """
        #EXTM3U
        #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="aud",NAME="English",LANGUAGE="en",DEFAULT=NO,URI="audio/en.m3u8"
        #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="aud",NAME="Deutsch",LANGUAGE="de",DEFAULT=YES,URI="audio/de.m3u8"
        #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="muxed",NAME="Main",DEFAULT=YES
        #EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360,CODECS="avc1.4d401e,mp4a.40.2",AUDIO="aud"
        video/360.m3u8
        #EXT-X-STREAM-INF:BANDWIDTH=400000,RESOLUTION=426x240,CODECS="avc1.4d401e,mp4a.40.2",AUDIO="muxed"
        video/240.m3u8
        #EXT-X-STREAM-INF:BANDWIDTH=64000,CODECS="mp4a.40.2"
        audio/only.m3u8
        """;

    private const string AudioOnlyMaster = """
        #EXTM3U
        #EXT-X-STREAM-INF:BANDWIDTH=64000,CODECS="mp4a.40.2"
        low.m3u8
        #EXT-X-STREAM-INF:BANDWIDTH=128000,CODECS="mp4a.40.2"
        high.m3u8
        """;

    private const string MediaPlaylist = """
        #EXTM3U
        #EXT-X-VERSION:3
        #EXT-X-TARGETDURATION:4
        #EXT-X-MEDIA-SEQUENCE:0
        #EXTINF:4.0,
        segment0.ts
        #EXT-X-ENDLIST
        """;

    private sealed class HeaderModifier : IRequestModifier
    {
        public bool AllowByteSkip => true;

        public Request ModifyRequest(string url, Grayjay.Engine.Models.HttpHeaders headers)
        {
            headers.Set("X-Audio-Token", "audio");
            return null!;
        }
    }

    private static T InvokeStatic<T>(string name, params object?[] arguments)
    {
        var method = typeof(VideoDownload).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        return (T)Invoke(() => method.Invoke(null, arguments))!;
    }

    private static void SelectSources(VideoDownload download, PlatformVideoDetails details)
    {
        var method = typeof(VideoDownload).GetMethod("SelectSources", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Invoke(() => method.Invoke(download, new object?[] { new ManagedHttpClient(), details, CancellationToken.None }));
    }

    private static object? Invoke(Func<object?> invoke)
    {
        try
        {
            return invoke();
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static PlatformVideoDetails Details(VideoDescriptor descriptor)
    {
        return new PlatformVideoDetails
        {
            ID = new PlatformID { Value = "audio-selection-" + Guid.NewGuid().ToString("N") },
            Name = "Audio selection",
            Url = "https://test/video",
            Video = descriptor,
            Subtitles = Array.Empty<Grayjay.Engine.Models.Subtitles.SubtitleSource>()
        };
    }

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

    private static async Task StopServer(WebApplication app)
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    private static Task NotFound(HttpContext context)
    {
        context.Response.StatusCode = 404;
        return Task.CompletedTask;
    }

    private static Task WriteText(HttpContext context, string text)
    {
        context.Response.ContentType = "application/vnd.apple.mpegurl";
        return context.Response.WriteAsync(text);
    }

    [TestMethod]
    public void VariantWithAudioGroupGetsRenditionsDefaultFirst()
    {
        var master = HLS.ParseMasterPlaylist(MasterWithAudioGroup, "https://example.com/path/master.m3u8");

        var renditions = master.GetAudioSourcesForVariant("https://example.com/path/video/360.m3u8");
        Assert.AreEqual(2, renditions.Count);
        Assert.AreEqual("de", renditions[0].Language);
        Assert.AreEqual("https://example.com/path/audio/de.m3u8", renditions[0].Url);
        Assert.AreEqual("en", renditions[1].Language);
        Assert.AreEqual("English", renditions[1].Name);
        Assert.AreEqual("mp4a.40.2", renditions[1].Codec);
        Assert.AreEqual("application/vnd.apple.mpegurl", renditions[1].Container);

        Assert.AreEqual(0, master.GetAudioSourcesForVariant("https://example.com/path/video/240.m3u8").Count, "A group without URIs means the audio is in the variant");
        Assert.AreEqual(0, master.GetAudioSourcesForVariant("https://example.com/path/audio/only.m3u8").Count);

        var audioOnly = master.GetAudioOnlyVariantSources();
        Assert.AreEqual(1, audioOnly.Count);
        Assert.AreEqual("https://example.com/path/audio/only.m3u8", audioOnly[0].Url);
        Assert.AreEqual(64000, audioOnly[0].Bitrate);
    }

    [TestMethod]
    public void AudioManifestParsesToPlaylists()
    {
        var media = HLS.ParseToAudioSources(MediaPlaylist, "https://example.com/audio.m3u8");
        Assert.AreEqual(1, media.Count);
        Assert.AreEqual("https://example.com/audio.m3u8", media[0].Url);

        var renditions = HLS.ParseToAudioSources(MasterWithAudioGroup, "https://example.com/path/master.m3u8");
        CollectionAssert.AreEqual(new[] { "de", "en" }, renditions.Select(source => source.Language).ToArray());

        var variants = HLS.ParseToAudioSources(AudioOnlyMaster, "https://example.com/audio/master.m3u8");
        CollectionAssert.AreEqual(new[] { 64000, 128000 }, variants.Select(source => source.Bitrate).ToArray());
    }

    [TestMethod]
    public void RenditionSelectionHonoursLanguageThenDefault()
    {
        var master = HLS.ParseMasterPlaylist(MasterWithAudioGroup, "https://example.com/path/master.m3u8");
        var renditions = master.GetAudioSourcesForVariant("https://example.com/path/video/360.m3u8");

        Assert.AreEqual("en", InvokeStatic<HLSVariantAudioUrlSource>("SelectHLSAudioSource", renditions, "en").Language);
        Assert.AreEqual("de", InvokeStatic<HLSVariantAudioUrlSource>("SelectHLSAudioSource", renditions, "fr").Language);
        Assert.IsNull(InvokeStatic<HLSVariantAudioUrlSource?>("SelectHLSAudioSource", new List<HLSVariantAudioUrlSource>(), "en"));
    }

    [TestMethod]
    public void VideoSourceHasAudioMatchesDescriptor()
    {
        var mp4 = new VideoUrlSource { Container = "video/mp4", Width = 1280, Height = 720 };
        var hls = new HLSVariantVideoUrlSource { Container = "application/vnd.apple.mpegurl", Width = 640, Height = 360 };
        var muxed = Details(new VideoDescriptor { VideoSources = new IVideoSource[] { mp4 } });
        var unmuxed = Details(new UnMuxedVideoDescriptor { VideoSources = new IVideoSource[] { mp4 }, AudioSources = Array.Empty<IAudioSource>() });

        Assert.IsTrue(InvokeStatic<bool>("VideoSourceHasAudio", muxed, mp4));
        Assert.IsFalse(InvokeStatic<bool>("VideoSourceHasAudio", unmuxed, mp4));
        Assert.IsTrue(InvokeStatic<bool>("VideoSourceHasAudio", unmuxed, hls));
        Assert.IsFalse(InvokeStatic<bool>("VideoSourceHasAudio", null, mp4));
        Assert.IsTrue(InvokeStatic<bool>("VideoSourceHasAudio", null, new VideoUrlSource { Container = "application/vnd.apple.mpegurl" }));
    }

    [TestMethod]
    public void ChosenSourcesAreFreshAndPrepareTimeIsNotPersisted()
    {
        var details = Details(new VideoDescriptor { VideoSources = Array.Empty<IVideoSource>() });
        var download = new VideoDownload(details, new VideoUrlSource { Container = "video/mp4", Url = "https://test/v.mp4" }, null);
        Assert.IsNotNull(download.PrepareTime, "Sources chosen in the dialog must not be re-picked straight away");

        string json = JsonSerializer.Serialize(new VideoDownload { PrepareTime = DateTime.Now });
        Assert.IsFalse(json.Contains("PrepareTime"), "A restored download has no live sources and must prepare again");
    }

    [TestMethod]
    public async Task MuxedRequirementIsDecidedWhileDetailsAreLive()
    {
        var videoOnly = new VideoUrlSource { Container = "video/mp4", Width = 640, Height = 360, Url = "https://test/video-only.mp4" };
        var unmuxedDetails = Details(new UnMuxedVideoDescriptor
        {
            VideoSources = new IVideoSource[] { videoOnly },
            AudioSources = Array.Empty<IAudioSource>()
        });
        var muxed = new VideoUrlSource { Container = "video/mp4", Width = 640, Height = 360, Url = "https://test/muxed.mp4" };
        var muxedDetails = Details(new VideoDescriptor { VideoSources = new IVideoSource[] { muxed } });

        Assert.IsFalse(new VideoDownload(unmuxedDetails, videoOnly, null).RequireMuxedVideo);
        Assert.IsFalse(new VideoDownload(unmuxedDetails, videoOnly, new AudioUrlSource { Container = "audio/mp4", Url = "https://test/a.m4a" }).RequireMuxedVideo);
        Assert.IsTrue(new VideoDownload(muxedDetails, muxed, null).RequireMuxedVideo);
        Assert.IsTrue(new VideoDownload(unmuxedDetails, new HLSVariantVideoUrlSource { Container = "application/vnd.apple.mpegurl", Url = "https://test/v.m3u8" }, null).RequireMuxedVideo);

        var restored = GJsonSerializer.Deserialize<VideoDownload>(GJsonSerializer.Serialize(new VideoDownload(unmuxedDetails, videoOnly, null)));
        Assert.IsNotInstanceOfType(restored.VideoDetails!.Video, typeof(UnMuxedVideoDescriptor), "Saved details lose the descriptor type");
        Assert.IsFalse(restored.RequireMuxedVideo);
        Assert.IsTrue(GJsonSerializer.Deserialize<VideoDownload>(GJsonSerializer.Serialize(new VideoDownload(muxedDetails, muxed, null))).RequireMuxedVideo);

        //The expired branch runs before Prepare, which fails here because no plugin serves the url
        var method = typeof(StateDownloads).GetMethod("DownloadVideo", BindingFlags.NonPublic | BindingFlags.Static)!;
        bool prepareFailed = false;
        try
        {
            await (Task)Invoke(() => method.Invoke(null, new object[] { restored }))!;
        }
        catch (Exception)
        {
            prepareFailed = true;
        }
        Assert.IsTrue(prepareFailed);
        Assert.IsFalse(restored.RequireMuxedVideo, "A restored video-only download must not be switched to a muxed source");
    }

    [TestMethod]
    public void LiveSourcesArePreparedAgain()
    {
        var details = Details(new VideoDescriptor { VideoSources = Array.Empty<IVideoSource>() });
        var modifiedVideo = new VideoUrlSource { Container = "video/mp4", Url = "https://test/v.mp4" };
        typeof(JSSource).GetProperty(nameof(JSSource.HasRequestModifier))!.SetValue(modifiedVideo, true);
        var executedAudio = new AudioUrlSource { Container = "audio/mp4", Url = "https://test/a.m4a" };
        typeof(JSSource).GetProperty(nameof(JSSource.HasRequestExecutor))!.SetValue(executedAudio, true);

        Assert.IsNull(new VideoDownload(details, new UMPSource(), null).PrepareTime, "UMP sources need a live plugin object");
        Assert.IsNull(new VideoDownload(details, modifiedVideo, null).PrepareTime, "A request modifier needs a live plugin object");
        Assert.IsNull(new VideoDownload(details, null, executedAudio).PrepareTime, "A request executor needs a live plugin object");
        Assert.IsNull(new VideoDownload(details, new VideoUrlSource { Container = "video/mp4", Url = "https://test/v.mp4" }, null, new FetchedSubtitle()).PrepareTime,
            "Fetched subtitles need a live plugin object");
    }

    private sealed class FetchedSubtitle : ISubtitleSource
    {
        public string Name => "English";
        public string? Url => null;
        public string? Format => "text/vtt";
        public bool HasFetch => true;
        public string? GetSubtitles() => "WEBVTT";
        public Uri? GetSubtitlesUri() => null;
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RepickOfMuxedVideoWithoutMuxedSourcesFailsRetryably(bool separateAudioAvailable)
    {
        var videoOnly = new VideoUrlSource { Container = "video/mp4", Width = 640, Height = 360, Url = "https://test/video-only.mp4" };
        var details = Details(new UnMuxedVideoDescriptor
        {
            VideoSources = new IVideoSource[] { videoOnly },
            AudioSources = separateAudioAvailable
                ? new IAudioSource[] { new AudioUrlSource { Container = "audio/mp4", Url = "https://test/audio.m4a" } }
                : Array.Empty<IAudioSource>()
        });
        var download = new VideoDownload(details, 640 * 360, null) { RequireMuxedVideo = true };
        var exception = Assert.ThrowsException<DownloadException>(() => SelectSources(download, details));
        Assert.IsTrue(exception.IsRetryable, "Fresh details may restore a source with audio on retry");
        Assert.IsNull(download.VideoSource, "A video-only source must not replace a previously muxed source");
        Assert.IsNull(download.PrepareTime, "Failed preparation must not mark the download ready");
    }

    [TestMethod]
    public async Task RepickOfMuxedVideoSkipsVideoOnlySourcesAndAttachesRendition()
    {
        (var app, var baseUrl) = await StartServer(context => WriteText(context, MasterWithAudioGroup));
        try
        {
            var videoOnly = new VideoUrlSource { Container = "video/mp4", Width = 640, Height = 360, Url = baseUrl + "/video-only.mp4" };
            var details = Details(new UnMuxedVideoDescriptor
            {
                VideoSources = new IVideoSource[] { videoOnly, new HLSManifestSource { Url = baseUrl + "/master.m3u8" } },
                AudioSources = Array.Empty<IAudioSource>()
            });

            var unrestricted = new VideoDownload(details, 640 * 360, null);
            SelectSources(unrestricted, details);
            Assert.AreSame(videoOnly, unrestricted.VideoSource, "Without the flag the first closest source is used");

            var download = new VideoDownload(details, 640 * 360, null) { RequireMuxedVideo = true, TargetLanguage = "en" };
            SelectSources(download, details);
            Assert.IsInstanceOfType(download.VideoSource, typeof(HLSVariantVideoUrlSource));
            Assert.AreEqual(baseUrl + "/video/360.m3u8", ((VideoUrlSource)download.VideoSource!).Url);
            Assert.IsInstanceOfType(download.AudioSource, typeof(HLSVariantAudioUrlSource));
            Assert.AreEqual(baseUrl + "/audio/en.m3u8", ((AudioUrlSource)download.AudioSource!).Url);
            Assert.IsNotNull(download.PrepareTime);
        }
        finally
        {
            await StopServer(app);
        }
    }

    [TestMethod]
    public async Task DialogVariantWithAudioGroupGetsRendition()
    {
        (var app, var baseUrl) = await StartServer(context => WriteText(context, MasterWithAudioGroup));
        try
        {
            var details = Details(new VideoDescriptor { VideoSources = new IVideoSource[] { new HLSManifestSource { Url = baseUrl + "/master.m3u8" } } });
            DownloadController.LoadDownload(details);
            var sources = (DownloadController.DownloadSources)typeof(DownloadController).GetField("_sources", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var method = typeof(DownloadController).GetMethod("SelectManifestVariant", BindingFlags.NonPublic | BindingFlags.Static)!;
            (IVideoSource Video, IAudioSource? Audio) Select(string variantPath, IAudioSource? chosenAudio)
            {
                int manifestIndex = sources.ManifestSources[0].FindIndex(variant => variant.Url == baseUrl + variantPath);
                Assert.IsTrue(manifestIndex >= 0);
                return ((IVideoSource, IAudioSource?))Invoke(() => method.Invoke(null, new object?[] { sources, 0, manifestIndex, chosenAudio }))!;
            }

            var separate = Select("/video/360.m3u8", null);
            Assert.AreEqual(baseUrl + "/video/360.m3u8", ((VideoUrlSource)separate.Video).Url);
            Assert.IsInstanceOfType(separate.Audio, typeof(HLSVariantAudioUrlSource));
            CollectionAssert.Contains(new[] { baseUrl + "/audio/de.m3u8", baseUrl + "/audio/en.m3u8" }, ((AudioUrlSource)separate.Audio!).Url);

            Assert.IsNull(Select("/video/240.m3u8", null).Audio, "Audio inside the variant needs no separate download");

            var chosen = new AudioUrlSource { Container = "audio/mp4", Url = "https://test/a.m4a" };
            Assert.AreSame(chosen, Select("/video/360.m3u8", chosen).Audio);
        }
        finally
        {
            await StopServer(app);
        }
    }

    [TestMethod]
    public async Task DialogVariantsResolveAgainstTheRedirectedManifest()
    {
        (var app, var baseUrl) = await StartServer(context =>
        {
            if (context.Request.Path == "/master.m3u8")
            {
                context.Response.Redirect("/cdn/token/master.m3u8");
                return Task.CompletedTask;
            }
            return context.Request.Path == "/cdn/token/master.m3u8" ? WriteText(context, MasterWithAudioGroup) : NotFound(context);
        });
        try
        {
            var details = Details(new VideoDescriptor { VideoSources = new IVideoSource[] { new HLSManifestSource { Url = baseUrl + "/master.m3u8" } } });
            DownloadController.LoadDownload(details);
            var sources = (DownloadController.DownloadSources)typeof(DownloadController).GetField("_sources", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            int manifestIndex = sources.ManifestSources[0].FindIndex(variant => variant.Url == baseUrl + "/cdn/token/video/360.m3u8");
            Assert.IsTrue(manifestIndex >= 0, "Relative variant URIs must resolve against the URL the manifest was served from");

            var method = typeof(DownloadController).GetMethod("SelectManifestVariant", BindingFlags.NonPublic | BindingFlags.Static)!;
            var selected = ((IVideoSource Video, IAudioSource? Audio))Invoke(() => method.Invoke(null, new object?[] { sources, 0, manifestIndex, null }))!;
            Assert.IsInstanceOfType(selected.Audio, typeof(HLSVariantAudioUrlSource));
            StringAssert.StartsWith(((AudioUrlSource)selected.Audio!).Url, baseUrl + "/cdn/token/audio/");
        }
        finally
        {
            await StopServer(app);
        }
    }

    [TestMethod]
    public void RequiredAudioWithoutCandidatesThrowsRetryable()
    {
        var details = Details(new UnMuxedVideoDescriptor
        {
            VideoSources = new IVideoSource[] { new VideoUrlSource { Container = "video/mp4", Width = 640, Height = 360, Url = "https://test/v.mp4" } },
            AudioSources = Array.Empty<IAudioSource>()
        });
        var download = new VideoDownload(details, 640 * 360, 128000);
        Assert.IsTrue(download.RequireAudioSource);

        var exception = Assert.ThrowsException<DownloadException>(() => SelectSources(download, details));
        Assert.IsTrue(exception.IsRetryable);
    }

    [TestMethod]
    public void RequiredAudioIsSatisfiedByMuxedVideo()
    {
        var muxed = new VideoUrlSource { Container = "video/mp4", Width = 640, Height = 360, Url = "https://test/v.mp4" };
        var details = Details(new VideoDescriptor { VideoSources = new IVideoSource[] { muxed } });
        var download = new VideoDownload(details, 640 * 360, 128000);

        SelectSources(download, details);
        Assert.AreSame(muxed, download.VideoSource);
        Assert.IsNull(download.AudioSource);
    }

    [TestMethod]
    public async Task AudioManifestIsResolvedDuringSelection()
    {
        (var app, var baseUrl) = await StartServer(context => WriteText(context, context.Request.Path == "/audio.m3u8" ? MediaPlaylist : MasterWithAudioGroup));
        try
        {
            var details = Details(new UnMuxedVideoDescriptor
            {
                VideoSources = Array.Empty<IVideoSource>(),
                AudioSources = new IAudioSource[] { new HLSManifestAudioSource { Url = baseUrl + "/audio.m3u8", Name = "Main", Language = "en", Bitrate = 96000 } }
            });
            var download = new VideoDownload(details, null, 96000);

            SelectSources(download, details);
            var audio = download.AudioSource as HLSVariantAudioUrlSource;
            Assert.IsNotNull(audio, "An HLS audio manifest must be resolved, not skipped");
            Assert.AreEqual(baseUrl + "/audio.m3u8", audio.Url);
            Assert.AreEqual("en", audio.Language);
            Assert.AreEqual("Main", audio.Name);
            Assert.AreEqual(96000, audio.Bitrate);
        }
        finally
        {
            await StopServer(app);
        }
    }

    [TestMethod]
    public async Task AudioManifestFetchFailureResolvesToNothing()
    {
        (var app, var baseUrl) = await StartServer(NotFound);
        try
        {
            var sources = InvokeStatic<List<HLSVariantAudioUrlSource>>("ResolveHLSAudioSources", new ManagedHttpClient(),
                new HLSManifestAudioSource { Url = baseUrl + "/missing.m3u8" }, CancellationToken.None, null);
            Assert.AreEqual(0, sources.Count);
        }
        finally
        {
            await StopServer(app);
        }
    }

    private static (int ExitCode, string Error) RunFfmpeg(params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo.FileName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            Assert.Inconclusive("FFmpeg is required for media integration tests: " + exception.Message);
            throw;
        }
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        output.GetAwaiter().GetResult();
        return (process.ExitCode, error.GetAwaiter().GetResult());
    }

    private static byte[] RequireAudioSegment()
    {
        try
        {
            if (!FFMPEG.IsFFMPEGAvailable())
            {
                Assert.Inconclusive("FFmpeg is required for media integration tests");
            }
        }
        catch (InvalidOperationException exception)
        {
            Assert.Inconclusive("FFmpeg is required for media integration tests: " + exception.Message);
        }
        string path = Path.Combine(Path.GetTempPath(), "audio-segment-" + Guid.NewGuid().ToString("N") + ".ts");
        try
        {
            var result = RunFfmpeg("-nostdin", "-y", "-v", "error", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000",
                "-t", "2", "-c:a", "aac", "-b:a", "32k", "-f", "mpegts", path);
            Assert.AreEqual(0, result.ExitCode, result.Error);
            return File.ReadAllBytes(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<VideoDownload> DownloadAudio(IAudioSource audioSource)
    {
        var details = Details(new UnMuxedVideoDescriptor { VideoSources = Array.Empty<IVideoSource>(), AudioSources = new[] { audioSource } });
        var download = new VideoDownload(details, null, audioSource);
        await download.Download(new ManagedHttpClient(), null!);
        return download;
    }

    private static void AssertDecodableAudio(VideoDownload download)
    {
        try
        {
            Assert.IsNotNull(download.AudioFilePath);
            Assert.IsTrue(File.Exists(download.AudioFilePath));
            var result = RunFfmpeg("-nostdin", "-v", "error", "-i", download.AudioFilePath, "-map", "0:a:0", "-f", "null", "-");
            Assert.AreEqual(0, result.ExitCode, result.Error);
        }
        finally
        {
            if (download.AudioFilePath != null && File.Exists(download.AudioFilePath))
            {
                File.Delete(download.AudioFilePath);
            }
        }
    }

    [TestMethod]
    public async Task ChosenAudioManifestDownloads()
    {
        byte[] segment = RequireAudioSegment();
        (var app, var baseUrl) = await StartServer(context => context.Request.Path.Value switch
        {
            "/master.m3u8" => WriteText(context, MasterWithAudioGroup),
            "/audio/en.m3u8" => WriteText(context, MediaPlaylist),
            "/audio/segment0.ts" => context.Response.Body.WriteAsync(segment).AsTask(),
            _ => NotFound(context)
        });
        try
        {
            var download = await DownloadAudio(new HLSManifestAudioSource { Url = baseUrl + "/master.m3u8", Language = "en" });
            AssertDecodableAudio(download);
        }
        finally
        {
            await StopServer(app);
        }
    }

    [TestMethod]
    public async Task AudioRequestsUseTheAudioModifier()
    {
        byte[] segment = RequireAudioSegment();
        (var app, var baseUrl) = await StartServer(context =>
        {
            if (context.Request.Headers["X-Audio-Token"] != "audio")
            {
                context.Response.StatusCode = 403;
                return Task.CompletedTask;
            }
            return context.Request.Path.Value == "/audio.m3u8" ? WriteText(context, MediaPlaylist) : context.Response.Body.WriteAsync(segment).AsTask();
        });
        try
        {
            var download = await DownloadAudio(new HLSVariantAudioUrlSource
            {
                Container = "application/vnd.apple.mpegurl",
                Codec = "mp4a.40.2",
                Language = "en",
                Url = baseUrl + "/audio.m3u8",
                Modifier = new HeaderModifier()
            });
            AssertDecodableAudio(download);
        }
        finally
        {
            await StopServer(app);
        }
    }

    [TestMethod]
    public async Task AudioOnlyPlaylistItemsKeepTheirOwnAudio()
    {
        (var app, var baseUrl) = await StartServer(context => WriteText(context, "audio of " + context.Request.Path.Value));
        var playlistId = "playlist-" + Guid.NewGuid().ToString("N");
        var completed = new List<VideoLocal>();
        try
        {
            foreach (var item in new[] { "first", "second", "third" })
            {
                var details = Details(new UnMuxedVideoDescriptor
                {
                    VideoSources = Array.Empty<IVideoSource>(),
                    AudioSources = new IAudioSource[] { new AudioUrlSource { Container = "audio/mp4", Codec = "mp4a.40.2", Bitrate = 128000, Language = "en", Url = $"{baseUrl}/{item}.m4a" } }
                });
                details.Url = $"{baseUrl}/watch/{item}";
                var download = new VideoDownload((PlatformVideo)details, null, 128000, null, "playlist", playlistId);
                SelectSources(download, details);
                await download.Download(new ManagedHttpClient(), null!);
                download.Validate();
                download.Complete();

                var local = StateDownloads.GetDownloadedVideo(details.Url)!;
                completed.Add(local);
                Assert.AreEqual(playlistId, local.GroupID);
                Assert.AreEqual(0, local.VideoSources.Count);
                Assert.AreEqual(1, local.AudioSources.Count);
                Assert.AreEqual($"audio of /{item}.m4a", File.ReadAllText(local.AudioSources[0].FilePath));
            }
            Assert.AreEqual(3, completed.Select(local => local.AudioSources[0].FilePath).Distinct().Count());
        }
        finally
        {
            foreach (var local in completed)
            {
                local.DeleteFiles();
                StateDownloads._downloaded.Delete(local);
            }
            await StopServer(app);
        }
    }
}
