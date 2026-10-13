using System.Reflection;
using System.Runtime.ExceptionServices;
using Grayjay.ClientServer.Controllers;
using Grayjay.ClientServer.States;
using Grayjay.ClientServer.Store;
using Grayjay.Engine.Models.Detail;
using Grayjay.Engine.Models.Video.Sources;
using PlatformID = Grayjay.Engine.Models.General.PlatformID;

namespace Grayjay.Desktop.Tests;

[TestClass]
public class DownloadFilePathTests
{
    private string _directory = null!;

    private string StoreDirectory => Path.Combine(_directory, "store");
    private string OldDownloads => Path.Combine(_directory, "old-downloads");
    private string NewDownloads => Path.Combine(_directory, "new-downloads");

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "download-paths-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(StoreDirectory);
        Directory.CreateDirectory(OldDownloads);
        Directory.CreateDirectory(NewDownloads);
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

    private static T Invoke<T>(Type type, string name, params object?[] arguments)
    {
        var method = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(candidate => candidate.Name == name && candidate.GetParameters().Length == arguments.Length);
        try
        {
            return (T)method.Invoke(null, arguments)!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private ManagedStore<VideoLocal> OpenStore()
        => new ManagedStore<VideoLocal>("downloaded", new DirectoryInfo(StoreDirectory))
            .WithUnique(local => local.VideoDetails.ID)
            .WithKeepUnloadable()
            .Load();

    private static string WriteFile(string directory, string name)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, "data " + name);
        return path;
    }

    private static VideoLocal Local(string id, string videoPath, string? audioPath = null, string? subtitlePath = null)
    {
        var local = new VideoLocal(new PlatformVideoDetails
        {
            ID = new PlatformID { Value = id },
            Name = "Paths: " + id,
            Url = "https://test/" + id
        });
        local.VideoSources.Add(new LocalVideoSource { Container = "video/mp4", Width = 640, Height = 360, FilePath = videoPath });
        if (audioPath != null)
        {
            local.AudioSources.Add(new LocalAudioSource { Container = "audio/mp4", FilePath = audioPath });
        }
        if (subtitlePath != null)
        {
            local.SubtitleSources.Add(new LocalSubtitleSource { Name = "English", Format = "text/vtt", FilePath = subtitlePath });
        }
        return local;
    }

    private static VideoLocal Record(ManagedStore<VideoLocal> store, string id) => store.GetObjects().Single(local => local.VideoDetails.ID.Value == id);

    private int ResolveMovedFiles(string downloadsDirectory, ManagedStore<VideoLocal> store)
        => Invoke<int>(typeof(StateDownloads), "ResolveMovedFiles", downloadsDirectory, store);

    [DataTestMethod]
    [DataRow(@"C:\Users\user\Desktop\Grayjay.Desktop-win-x64-v2\Grayjay.Desktop-win-x64-v2\downloads\")]
    [DataRow("/home/user/old-portable/downloads/")]
    public void RecordsWithAnOldFolderPointAtTheFileInTheDownloadsFolder(string oldFolder)
    {
        string video = WriteFile(NewDownloads, "Mt5Uat93SOY__1920x1080_.mp4");
        string audio = WriteFile(NewDownloads, "Mt5Uat93SOY__en-128000_.mp4a");
        string subtitle = WriteFile(NewDownloads, "Mt5Uat93SOY__English_.vtt");
        string staleDash = Path.Combine(NewDownloads, "Mt5Uat93SOY__1920x1080_.mp4.dash");
        Directory.CreateDirectory(Path.Combine(staleDash, "OLDFINGERPRINT"));
        File.WriteAllText(Path.Combine(staleDash, "OLDFINGERPRINT", "manifest.mpd"), "<MPD/>");
        var store = OpenStore();
        store.Save(Local("moved", oldFolder + "Mt5Uat93SOY__1920x1080_.mp4", oldFolder + "Mt5Uat93SOY__en-128000_.mp4a", oldFolder + "Mt5Uat93SOY__English_.vtt"));
        string presentVideo = WriteFile(OldDownloads, "present__640x360_.mp4");
        store.Save(Local("present", presentVideo));

        Assert.AreEqual(1, ResolveMovedFiles(NewDownloads, store));

        var reloaded = OpenStore();
        var moved = Record(reloaded, "moved");
        Assert.AreEqual(video, moved.VideoSources[0].FilePath);
        Assert.AreEqual(audio, moved.AudioSources[0].FilePath);
        Assert.AreEqual(subtitle, moved.SubtitleSources[0].FilePath);
        Assert.IsFalse(Directory.Exists(staleDash));
        Assert.AreEqual(presentVideo, Record(reloaded, "present").VideoSources[0].FilePath);
        Assert.AreEqual(0, ResolveMovedFiles(NewDownloads, reloaded));
    }

    [TestMethod]
    public void ResolvedRecordsExportAndDeleteTheirFiles()
    {
        string video = WriteFile(NewDownloads, "abc__640x360_.mp4");
        var store = OpenStore();
        store.Save(Local("abc", @"D:\Grayjay\downloads\abc__640x360_.mp4"));
        ResolveMovedFiles(NewDownloads, store);
        var local = Record(OpenStore(), "abc");
        string exportFolder = Path.Combine(_directory, "export");
        Directory.CreateDirectory(exportFolder);

        Assert.IsNull(Invoke<string?>(typeof(DownloadController), "ExportToFolder", local, exportFolder));
        Assert.AreEqual(1, Directory.GetFiles(exportFolder).Length);

        local.DeleteFiles();
        Assert.IsFalse(File.Exists(video));
    }

    [TestMethod]
    public void ARecordThatCannotBeSavedDoesNotStopTheOthers()
    {
        WriteFile(NewDownloads, "locked__640x360_.mp4");
        string video = WriteFile(NewDownloads, "other__640x360_.mp4");
        var store = OpenStore();
        var locked = Local("locked", Path.Combine(OldDownloads, "locked__640x360_.mp4"));
        store.Save(locked);
        store.Save(Local("other", Path.Combine(OldDownloads, "other__640x360_.mp4")));
        string lockedRecordFile = store.GetFile(locked)!.FilePath;
        File.SetAttributes(lockedRecordFile, FileAttributes.ReadOnly);
        try
        {
            if (CanWriteTo(lockedRecordFile))
            {
                Assert.Inconclusive("Read-only files are writable for this user.");
            }

            Assert.AreEqual(1, ResolveMovedFiles(NewDownloads, store));
        }
        finally
        {
            File.SetAttributes(lockedRecordFile, FileAttributes.Normal);
        }

        Assert.AreEqual(video, Record(OpenStore(), "other").VideoSources[0].FilePath);
        Assert.AreEqual(Path.Combine(NewDownloads, "locked__640x360_.mp4"), Record(store, "locked").VideoSources[0].FilePath);
    }

    private static bool CanWriteTo(string filePath)
    {
        try
        {
            using (File.OpenWrite(filePath))
            {
                return true;
            }
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    [TestMethod]
    public void RecordsWhoseFileIsNowhereAreLeftAlone()
    {
        var store = OpenStore();
        store.Save(Local("gone", Path.Combine(OldDownloads, "gone__640x360_.mp4")));

        Assert.AreEqual(0, ResolveMovedFiles(NewDownloads, store));
        Assert.AreEqual(Path.Combine(OldDownloads, "gone__640x360_.mp4"), Record(OpenStore(), "gone").VideoSources[0].FilePath);
    }

    [TestMethod]
    public void ExportOfAMissingFileNamesTheFile()
    {
        string missing = Path.Combine(OldDownloads, "missing__640x360_.mp4");
        string audio = WriteFile(OldDownloads, "missing__en-128000_.mp4a");

        string? failure = Invoke<string?>(typeof(DownloadController), "ExportToFolder", Local("missing", missing, audio), NewDownloads);

        Assert.IsNotNull(failure);
        StringAssert.Contains(failure, "Downloaded file is missing");
        StringAssert.Contains(failure, missing);
        Assert.AreEqual(0, Directory.GetFiles(NewDownloads).Length);
    }

    [TestMethod]
    public void MissingExportFileSkipsAbsentSourcesAndFindsTheMissingOne()
    {
        string present = WriteFile(OldDownloads, "present__640x360_.mp4");
        string missing = Path.Combine(OldDownloads, "missing.vtt");

        Assert.IsNull(Invoke<string?>(typeof(DownloadController), "MissingExportFile", new object?[] { new string?[] { present, null, "" } }));
        Assert.AreEqual(missing, Invoke<string?>(typeof(DownloadController), "MissingExportFile", new object?[] { new string?[] { present, null, missing } }));
    }
}
