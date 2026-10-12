using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Grayjay.ClientServer.Serializers;
using Grayjay.ClientServer.States;
using Grayjay.ClientServer.Store;
using Grayjay.Engine.Models;
using Grayjay.Engine.Models.Detail;
using Grayjay.Engine.Models.Video;
using Grayjay.Engine.Models.Video.Sources;
using PlatformID = Grayjay.Engine.Models.General.PlatformID;

namespace Grayjay.Desktop.Tests;

[TestClass]
public class DownloadStoreRecoveryTests
{
    private string _directory = null!;
    private static readonly MethodInfo CleanupMethod = typeof(StateDownloads).GetMethod(
        "CleanupFiles", BindingFlags.NonPublic | BindingFlags.Static, new[] { typeof(string), typeof(ManagedStore<VideoLocal>) })!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "download-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        DeleteDirectory(_directory);
    }

    private static void DeleteDirectory(string path)
    {
        foreach (string file in Directory.EnumerateFiles(path))
        {
            File.Delete(file);
        }
        foreach (string child in Directory.EnumerateDirectories(path))
        {
            DeleteDirectory(child);
        }
        Directory.Delete(path);
    }

    private static void RunCleanup(string downloadsDirectory, ManagedStore<VideoLocal> store)
    {
        try
        {
            CleanupMethod.Invoke(null, new object[] { downloadsDirectory, store });
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private ManagedStore<VideoLocal> OpenStore(bool keepUnloadable = true)
    {
        var store = new ManagedStore<VideoLocal>("downloaded", new DirectoryInfo(_directory))
            .WithUnique(local => local.VideoDetails.ID);
        if (keepUnloadable)
        {
            store = store.WithKeepUnloadable();
        }
        return store.Load();
    }

    private string StoreDirectory => Path.Combine(_directory, "downloaded");

    private static VideoLocal Local(string id, string? mediaPath = null, VideoDescriptor? descriptor = null)
    {
        var local = new VideoLocal(new PlatformVideoDetails
        {
            ID = new PlatformID { Value = id },
            Name = "Recovery: " + id,
            Url = "https://test/" + id,
            Video = descriptor
        });
        if (mediaPath != null)
        {
            local.VideoSources.Add(new LocalVideoSource { Container = "video/mp4", Width = 640, Height = 360, FilePath = mediaPath, FileSize = 4 });
        }
        return local;
    }

    private static VideoLocal SaveAndLoad(VideoLocal local) => GJsonSerializer.Deserialize<VideoLocal>(GJsonSerializer.Serialize(local));

    private static UnMuxedVideoDescriptor DescriptorWithUnsupportedEntries() => new UnMuxedVideoDescriptor
    {
        VideoSources = new IVideoSource[]
        {
            new VideoUrlSource { Container = "video/mp4", Url = "https://test/plain.mp4", Width = 1280, Height = 720, Name = "plain" },
            new DashManifestSource { Url = "https://test/manifest.mpd", Width = 1920, Height = 1080, Name = "dash" },
            new VideoUrlWidevineSource { Container = "video/mp4", Url = "https://test/protected.mp4", Width = 3840, Height = 2160, Name = "widevine", LicenseUri = "https://test/license" }
        },
        AudioSources = new IAudioSource[]
        {
            new AudioUrlSource { Container = "audio/mp4", Url = "https://test/plain.m4a", Name = "plain audio", Bitrate = 128000 },
            new AudioUrlWidevineSource { Container = "audio/mp4", Url = "https://test/protected.m4a", Name = "widevine audio", Bitrate = 256000, LicenseUri = "https://test/license" }
        }
    };

    [TestMethod]
    public void UnsupportedDescriptorEntriesLoadAsDescriptionsAndKeepTheLocalSource()
    {
        var local = Local("unsupported", "/media/unsupported.mp4", DescriptorWithUnsupportedEntries());

        var restored = SaveAndLoad(SaveAndLoad(local));

        Assert.AreEqual(1, restored.VideoSources.Count);
        Assert.AreEqual("/media/unsupported.mp4", restored.VideoSources[0].FilePath);
        Assert.AreEqual(640, restored.VideoSources[0].Width);
        //VideoDescriptor has no discriminator, so the descriptor restores as the base type, as on master
        var descriptor = restored.VideoDetails.Video;
        Assert.IsNotNull(descriptor);
        Assert.AreEqual(3, descriptor.VideoSources.Length);
        Assert.IsInstanceOfType(descriptor.VideoSources[0], typeof(VideoUrlSource));
        Assert.IsInstanceOfType(descriptor.VideoSources[1], typeof(VideoSourceDescription));
        Assert.IsInstanceOfType(descriptor.VideoSources[2], typeof(VideoSourceDescription));
        Assert.AreEqual("dash", descriptor.VideoSources[1].Name);
        Assert.AreEqual(1920, descriptor.VideoSources[1].Width);
        Assert.AreEqual(2160, descriptor.VideoSources[2].Height);
    }

    [TestMethod]
    public void UnsupportedSelectedSourcesLoadAsDescriptions()
    {
        var download = new Grayjay.ClientServer.Models.Downloads.VideoDownload
        {
            VideoSource = new DashManifestSource { Url = "https://test/manifest.mpd", Width = 1920, Height = 1080, Name = "dash" },
            AudioSource = new AudioUrlWidevineSource { Container = "audio/mp4", Url = "https://test/protected.m4a", Name = "widevine audio", Bitrate = 256000, LicenseUri = "https://test/license" }
        };

        string json = GJsonSerializer.Serialize(download);
        var restored = GJsonSerializer.Deserialize<Grayjay.ClientServer.Models.Downloads.VideoDownload>(json);
        restored = GJsonSerializer.Deserialize<Grayjay.ClientServer.Models.Downloads.VideoDownload>(GJsonSerializer.Serialize(restored));

        var video = restored.VideoSource as VideoSourceDescription;
        var audio = restored.AudioSource as AudioSourceDescription;
        Assert.IsNotNull(video, json);
        Assert.IsNotNull(audio, json);
        Assert.AreEqual(1080, video.Height);
        Assert.AreEqual("widevine audio", audio.Name);
        Assert.AreEqual(256000, audio.Bitrate);
    }

    [TestMethod]
    public void UnknownTypeLoadsAsDescription()
    {
        string json = """
            {
              "VideoSource": { "Type": "FutureVideoSource", "Width": 800, "Height": 600, "Name": "future", "Extra": { "Nested": [1, 2] } },
              "AudioSource": { "Extra": true, "Type": "FutureAudioSource", "Name": "future audio", "Language": "fr" }
            }
            """;

        var restored = GJsonSerializer.Deserialize<Grayjay.ClientServer.Models.Downloads.VideoDownload>(json);

        var video = restored.VideoSource as VideoSourceDescription;
        var audio = restored.AudioSource as AudioSourceDescription;
        Assert.IsNotNull(video);
        Assert.IsNotNull(audio);
        Assert.AreEqual(800, video.Width);
        Assert.AreEqual("future", video.Name);
        Assert.AreEqual("future audio", audio.Name);
        Assert.AreEqual("fr", audio.Language);
    }

    [DataTestMethod]
    [DataRow("""{ "VideoSource": { "Type": 3, "Width": 1 } }""")]
    [DataRow("""{ "VideoSource": { "Type": null, "Width": 1 } }""")]
    [DataRow("""{ "AudioSource": { "Type": ["AudioUrlSource"] } }""")]
    public void NonStringTypeStillFails(string json)
    {
        Assert.ThrowsException<JsonException>(() => GJsonSerializer.Deserialize<Grayjay.ClientServer.Models.Downloads.VideoDownload>(json));
    }

    [TestMethod]
    public void UnloadableRecordIsKeptAndReported()
    {
        var store = OpenStore();
        store.Save(Local("valid", "/media/valid.mp4"));
        string brokenPath = Path.Combine(StoreDirectory, "broken");
        byte[] brokenBytes = "{\"VideoDetails\": {\"ID\": "u8.ToArray();
        File.WriteAllBytes(brokenPath, brokenBytes);

        var reloaded = OpenStore();

        Assert.AreEqual(1, reloaded.Count());
        Assert.AreEqual("valid", reloaded.GetObjects()[0].VideoDetails.ID.Value);
        Assert.IsTrue(reloaded.HasUnloadable);
        CollectionAssert.AreEqual(new[] { "broken" }, reloaded.UnloadableIds);
        CollectionAssert.AreEqual(brokenBytes, File.ReadAllBytes(brokenPath));

        reloaded.Save(Local("added", "/media/added.mp4"));
        var third = OpenStore();
        Assert.AreEqual(2, third.Count());
        CollectionAssert.AreEqual(new[] { "broken" }, third.UnloadableIds);
        CollectionAssert.AreEqual(brokenBytes, File.ReadAllBytes(brokenPath));

        File.Delete(brokenPath);
        var repaired = OpenStore();
        Assert.IsFalse(repaired.HasUnloadable);
        Assert.AreEqual(0, repaired.UnloadableIds.Count);
    }

    [TestMethod]
    public void StoresWithoutTheOptionStillDeleteUnloadableRecords()
    {
        Directory.CreateDirectory(StoreDirectory);
        string brokenPath = Path.Combine(StoreDirectory, "broken");
        File.WriteAllText(brokenPath, "not json");

        var store = OpenStore(keepUnloadable: false);

        Assert.AreEqual(0, store.Count());
        Assert.IsFalse(store.HasUnloadable);
        Assert.IsFalse(File.Exists(brokenPath));
    }

    [TestMethod]
    public void LoneBackupIsLeftAloneAndNotReported()
    {
        Directory.CreateDirectory(StoreDirectory);
        string backupPath = Path.Combine(StoreDirectory, "orphan.bak");
        File.WriteAllText(backupPath, "backup");

        var store = OpenStore();

        Assert.AreEqual(0, store.Count());
        Assert.IsFalse(store.HasUnloadable);
        Assert.IsTrue(File.Exists(backupPath));
    }

    [TestMethod]
    public void CleanupKeepsAllMediaWhileARecordCannotBeLoaded()
    {
        string downloads = Path.Combine(_directory, "downloads");
        Directory.CreateDirectory(downloads);
        string knownMedia = Path.Combine(downloads, "known.mp4");
        string unreadableRecordMedia = Path.Combine(downloads, "unreadable.mp4");
        string dashCache = Path.Combine(downloads, "unreadable.mp4.dash");
        File.WriteAllText(knownMedia, "data");
        File.WriteAllText(unreadableRecordMedia, "data");
        Directory.CreateDirectory(dashCache);
        File.WriteAllText(Path.Combine(dashCache, "manifest.mpd"), "<MPD/>");

        var store = OpenStore();
        store.Save(Local("known", knownMedia));
        File.WriteAllText(Path.Combine(StoreDirectory, "unreadable"), "{ truncated");
        var reloaded = OpenStore();

        RunCleanup(downloads, reloaded);

        Assert.IsTrue(File.Exists(knownMedia));
        Assert.IsTrue(File.Exists(unreadableRecordMedia));
        Assert.IsTrue(File.Exists(Path.Combine(dashCache, "manifest.mpd")));
        Assert.IsTrue(File.Exists(Path.Combine(StoreDirectory, "unreadable")));
    }

    [TestMethod]
    public void CleanupStillRemovesOrphansWhenEveryRecordLoads()
    {
        string downloads = Path.Combine(_directory, "downloads");
        Directory.CreateDirectory(downloads);
        string knownMedia = Path.Combine(downloads, "known.mp4");
        string orphanMedia = Path.Combine(downloads, "orphan.mp4");
        File.WriteAllText(knownMedia, "data");
        File.WriteAllText(orphanMedia, "data");

        var store = OpenStore();
        store.Save(Local("known", knownMedia, DescriptorWithUnsupportedEntries()));
        var reloaded = OpenStore();

        Assert.IsFalse(reloaded.HasUnloadable);
        RunCleanup(downloads, reloaded);

        Assert.IsTrue(File.Exists(knownMedia));
        Assert.IsFalse(File.Exists(orphanMedia));
    }

    private string WriteRecord(VideoLocal local, DateTime writtenUtc, string? fileId = null)
    {
        Directory.CreateDirectory(StoreDirectory);
        string path = Path.Combine(StoreDirectory, fileId ?? Guid.NewGuid().ToString());
        File.WriteAllText(path, GJsonSerializer.Serialize(local));
        File.SetLastWriteTimeUtc(path, writtenUtc);
        return path;
    }

    private static string MediaOf(VideoLocal local) => local.VideoSources[0].FilePath;

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DuplicateRecordsLoadTheNewestAndKeepTheOther(bool newestWrittenFirst)
    {
        var now = DateTime.UtcNow;
        string olderPath;
        string newerPath;
        if (newestWrittenFirst)
        {
            newerPath = WriteRecord(Local("good", "/media/good-redownload.mp4"), now);
            olderPath = WriteRecord(Local("good", "/media/good.mp4"), now.AddHours(-1));
        }
        else
        {
            olderPath = WriteRecord(Local("good", "/media/good.mp4"), now.AddHours(-1));
            newerPath = WriteRecord(Local("good", "/media/good-redownload.mp4"), now);
        }
        WriteRecord(Local("other", "/media/other.mp4"), now.AddHours(-2));

        var store = OpenStore();

        Assert.AreEqual(2, store.Count());
        var good = store.GetObjects().Single(local => local.VideoDetails.ID.Value == "good");
        Assert.AreEqual("/media/good-redownload.mp4", MediaOf(good));
        Assert.AreEqual(1, store.DuplicateObjects.Count);
        Assert.AreEqual("/media/good.mp4", MediaOf(store.DuplicateObjects[0]));
        Assert.IsFalse(store.HasUnloadable);
        Assert.IsTrue(File.Exists(olderPath));
        Assert.IsTrue(File.Exists(newerPath));

        store.Save(Local("good", "/media/third.mp4"));
        Assert.AreEqual(2, store.Count());
        Assert.AreEqual(3, Directory.GetFiles(StoreDirectory).Length);

        File.Delete(olderPath);
        var reloaded = store.Load();
        Assert.AreEqual(0, reloaded.DuplicateObjects.Count);
        Assert.AreEqual("/media/good-redownload.mp4", MediaOf(reloaded.GetObjects().Single(local => local.VideoDetails.ID.Value == "good")));
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void NewestOfThreeDuplicatesIsLoaded(int newestIndex)
    {
        var now = DateTime.UtcNow;
        for (int index = 0; index < 3; index++)
        {
            var writtenUtc = index == newestIndex ? now : now.AddHours(-1 - index);
            WriteRecord(Local("good", $"/media/good-{index}.mp4"), writtenUtc);
        }

        var store = OpenStore();

        Assert.AreEqual(1, store.Count());
        Assert.AreEqual($"/media/good-{newestIndex}.mp4", MediaOf(store.GetObjects()[0]));
        CollectionAssert.AreEquivalent(
            Enumerable.Range(0, 3).Where(index => index != newestIndex).Select(index => $"/media/good-{index}.mp4").ToList(),
            store.DuplicateObjects.Select(MediaOf).ToList());
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EqualWriteTimesPickTheFirstFileIdInOrdinalOrder(bool firstIdWrittenFirst)
    {
        var writtenUtc = DateTime.UtcNow.AddMinutes(-5);
        if (firstIdWrittenFirst)
        {
            WriteRecord(Local("good", "/media/a.mp4"), writtenUtc, "aaaa");
            WriteRecord(Local("good", "/media/b.mp4"), writtenUtc, "bbbb");
        }
        else
        {
            WriteRecord(Local("good", "/media/b.mp4"), writtenUtc, "bbbb");
            WriteRecord(Local("good", "/media/a.mp4"), writtenUtc, "aaaa");
        }

        var store = OpenStore();

        Assert.AreEqual("/media/a.mp4", MediaOf(store.GetObjects()[0]));
        Assert.AreEqual("/media/b.mp4", MediaOf(store.DuplicateObjects[0]));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DeletingARecordAlsoDeletesItsOlderDuplicates(bool deleteByKey)
    {
        var now = DateTime.UtcNow;
        string olderPath = WriteRecord(Local("good", "/media/good.mp4"), now.AddHours(-1));
        string newerPath = WriteRecord(Local("good", "/media/good-redownload.mp4"), now);
        string otherOlderPath = WriteRecord(Local("other", "/media/other.mp4"), now.AddHours(-1));
        string otherNewerPath = WriteRecord(Local("other", "/media/other-redownload.mp4"), now);

        var store = OpenStore();
        Assert.AreEqual(2, store.DuplicateObjects.Count);
        if (deleteByKey)
        {
            Assert.IsNotNull(store.DeleteBy(local => local.VideoDetails.ID.Value, "good"));
        }
        else
        {
            Assert.IsTrue(store.Delete(store.GetObjects().Single(local => local.VideoDetails.ID.Value == "good")));
        }

        Assert.IsFalse(File.Exists(olderPath));
        Assert.IsFalse(File.Exists(newerPath));
        Assert.IsTrue(File.Exists(otherOlderPath));
        Assert.IsTrue(File.Exists(otherNewerPath));
        Assert.AreEqual("/media/other.mp4", MediaOf(store.DuplicateObjects.Single()));

        var reloaded = OpenStore();
        Assert.AreEqual(1, reloaded.Count());
        Assert.AreEqual("other", reloaded.GetObjects()[0].VideoDetails.ID.Value);
    }

    [TestMethod]
    public void StoresWithoutTheOptionLoadDuplicatesAsBefore()
    {
        var now = DateTime.UtcNow;
        WriteRecord(Local("good", "/media/good.mp4"), now.AddHours(-1));
        WriteRecord(Local("good", "/media/good-redownload.mp4"), now);

        var store = OpenStore(keepUnloadable: false);

        Assert.AreEqual(2, store.Count());
        Assert.AreEqual(0, store.DuplicateObjects.Count);
    }

    [TestMethod]
    public void CleanupKeepsTheMediaOfDuplicateRecords()
    {
        string downloads = Path.Combine(_directory, "downloads");
        Directory.CreateDirectory(downloads);
        string originalMedia = Path.Combine(downloads, "good.mp4");
        string redownloadMedia = Path.Combine(downloads, "good-redownload.mp4");
        string orphanMedia = Path.Combine(downloads, "orphan.mp4");
        File.WriteAllText(originalMedia, "data");
        File.WriteAllText(redownloadMedia, "data");
        File.WriteAllText(orphanMedia, "data");
        var now = DateTime.UtcNow;
        WriteRecord(Local("good", originalMedia), now.AddHours(-1));
        WriteRecord(Local("good", redownloadMedia), now);

        var store = OpenStore();
        Assert.AreEqual(1, store.DuplicateObjects.Count);
        RunCleanup(downloads, store);

        Assert.IsTrue(File.Exists(originalMedia));
        Assert.IsTrue(File.Exists(redownloadMedia));
        Assert.IsFalse(File.Exists(orphanMedia));
    }

    [DataTestMethod]
    [DataRow("_downloadingPlaylists")]
    [DataRow("_downloading")]
    [DataRow("_downloaded")]
    public void DownloadStoresKeepUnloadableRecords(string storeName)
    {
        object store = typeof(StateDownloads).GetField(storeName, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        object keep = store.GetType().GetField("_keepUnloadable", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(store)!;
        Assert.IsTrue((bool)keep, storeName);
    }
}
