using System.Text.Json;
using Grayjay.ClientServer.Models.Downloads;
using Grayjay.ClientServer.Serializers;
using Grayjay.Engine.Models;
using Grayjay.Engine.Models.Video.Additions;
using Grayjay.Engine.Models.Video.Sources;

namespace Grayjay.Desktop.Tests;

[TestClass]
public class SourceConverterTests
{
    [TestMethod]
    public void HlsVariantSourcesSurviveSaveAndRestore()
    {
        var download = new VideoDownload
        {
            VideoSource = new HLSVariantVideoUrlSource { Container = "application/vnd.apple.mpegurl", Url = "https://test/video.m3u8", Width = 640, Height = 360, Modifier = new TestModifier() },
            AudioSource = new HLSVariantAudioUrlSource { Container = "application/vnd.apple.mpegurl", Url = "https://test/audio.m3u8", Language = "en", Modifier = new TestModifier() }
        };

        string json = GJsonSerializer.Serialize(download);
        var restored = GJsonSerializer.Deserialize<VideoDownload>(json);

        var video = restored.VideoSource as VideoUrlSource;
        var audio = restored.AudioSource as AudioUrlSource;
        Assert.IsNotNull(video, json);
        Assert.IsNotNull(audio, json);
        Assert.AreEqual("https://test/video.m3u8", video.Url);
        Assert.AreEqual("application/vnd.apple.mpegurl", video.Container);
        Assert.AreEqual(640, video.Width);
        Assert.AreEqual(360, video.Height);
        Assert.AreEqual("application/vnd.apple.mpegurl", audio.Container);
        Assert.AreEqual("en", audio.Language);
    }

    [TestMethod]
    public void SavedJsonWithTypeAfterOtherPropertiesLoads()
    {
        string json = """
            {
              "VideoSource": { "Modifier": { "AllowByteSkip": true, "Nested": [1, { "Type": "Ignored" }] }, "Type": "VideoUrlSource", "Url": "https://test/video.m3u8", "Width": 1280, "Height": 720 },
              "AudioSource": { "Modifier": null, "Type": "AudioUrlSource", "Url": "https://test/audio.m3u8", "Language": "de" }
            }
            """;

        var restored = GJsonSerializer.Deserialize<VideoDownload>(json);

        var video = restored.VideoSource as VideoUrlSource;
        var audio = restored.AudioSource as AudioUrlSource;
        Assert.IsNotNull(video);
        Assert.IsNotNull(audio);
        Assert.AreEqual("https://test/video.m3u8", video.Url);
        Assert.AreEqual(1280, video.Width);
        Assert.AreEqual(720, video.Height);
        Assert.AreEqual("https://test/audio.m3u8", audio.Url);
        Assert.AreEqual("de", audio.Language);
    }

    [TestMethod]
    public void SourceWithoutTypeStillFails()
    {
        Assert.ThrowsException<JsonException>(() => GJsonSerializer.Deserialize<VideoDownload>("""{ "VideoSource": { "Url": "https://test/video.mp4" } }"""));
        Assert.ThrowsException<JsonException>(() => GJsonSerializer.Deserialize<VideoDownload>("""{ "AudioSource": { "Url": "https://test/audio.m4a" } }"""));
        Assert.ThrowsException<JsonException>(() => GJsonSerializer.Deserialize<VideoDownload>("""{ "VideoSource": { "Type": null, "Url": "https://test/video.mp4" } }"""));
    }

    [TestMethod]
    public void PlainSourcesStillRoundTrip()
    {
        var download = new VideoDownload
        {
            VideoSource = new VideoUrlSource { Container = "video/mp4", Codec = "avc1.64001F", Url = "https://test/video.mp4", Width = 1920, Height = 1080, Bitrate = 4000000 },
            AudioSource = new AudioUrlSource { Container = "audio/mp4", Codec = "mp4a.40.2", Url = "https://test/audio.m4a", Language = "en", Bitrate = 128000 }
        };

        var restored = GJsonSerializer.Deserialize<VideoDownload>(GJsonSerializer.Serialize(download));

        var video = restored.VideoSource as VideoUrlSource;
        var audio = restored.AudioSource as AudioUrlSource;
        Assert.IsNotNull(video);
        Assert.IsNotNull(audio);
        Assert.AreEqual(typeof(VideoUrlSource), video.GetType());
        Assert.AreEqual(typeof(AudioUrlSource), audio.GetType());
        Assert.AreEqual("https://test/video.mp4", video.Url);
        Assert.AreEqual("avc1.64001F", video.Codec);
        Assert.AreEqual(1920, video.Width);
        Assert.AreEqual(1080, video.Height);
        Assert.AreEqual(4000000, video.Bitrate);
        Assert.AreEqual("mp4a.40.2", audio.Codec);
        Assert.AreEqual("en", audio.Language);
        Assert.AreEqual(128000, audio.Bitrate);
    }

    [TestMethod]
    public void RangeSourcesRestoreWithTheirWrittenType()
    {
        var download = new VideoDownload
        {
            VideoSource = new VideoUrlRangeSource { Container = "video/mp4", Url = "https://test/video.mp4", ITagID = 137, InitEnd = 740, IndexStart = 741, IndexEnd = 1200 },
            AudioSource = new AudioUrlRangeSource { Container = "audio/mp4", Codec = "mp4a.40.2", Url = "https://test/audio.m4a", ITagID = 140, InitEnd = 631, IndexStart = 632, IndexEnd = 900 }
        };

        var restored = GJsonSerializer.Deserialize<VideoDownload>(GJsonSerializer.Serialize(download));

        var video = restored.VideoSource as VideoUrlRangeSource;
        Assert.IsNotNull(video);
        Assert.AreEqual("https://test/video.mp4", video.Url);
        Assert.AreEqual(137, video.ITagID);
        Assert.AreEqual(1200, video.IndexEnd);
        //Audio is written through IAudioSource, as on master, so it restores as its interface Type
        Assert.AreEqual(typeof(AudioUrlSource), restored.AudioSource?.GetType());
        Assert.AreEqual("audio/mp4", restored.AudioSource!.Container);
        Assert.AreEqual("mp4a.40.2", restored.AudioSource.Codec);
    }

    [TestMethod]
    public void AudioSourcesAreWrittenWithTypeFirst()
    {
        IAudioSource[] sources =
        {
            new AudioUrlSource { Container = "audio/mp4", Url = "https://test/audio.m4a", Language = "en" },
            new AudioUrlRangeSource { Container = "audio/mp4", Url = "https://test/audio.m4a", ITagID = 140 },
            new HLSVariantAudioUrlSource { Container = "application/vnd.apple.mpegurl", Url = "https://test/audio.m3u8", Language = "en", Modifier = new TestModifier() }
        };
        foreach (IAudioSource source in sources)
        {
            string json = GJsonSerializer.Serialize(new VideoDownload { AudioSource = source });
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement audio = document.RootElement.GetProperty("AudioSource");
            //Master's reader requires Type as the first property, so downgrades depend on this order
            Assert.AreEqual("Type", audio.EnumerateObject().First().Name, json);
            Assert.IsFalse(audio.TryGetProperty("Modifier", out _), json);
        }
    }

    private sealed class TestModifier : IRequestModifier
    {
        public bool AllowByteSkip => true;

        public Request ModifyRequest(string url, HttpHeaders headers) => null!;
    }
}
