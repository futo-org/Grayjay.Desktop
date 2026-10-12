using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Grayjay.ClientServer.Controllers;
using Grayjay.ClientServer.Serializers;
using Grayjay.ClientServer.Transcoding;
using Grayjay.Engine.Models.Detail;
using Grayjay.Engine.Models.Video;
using Grayjay.Engine.Models.Video.Sources;
using PlatformID = Grayjay.Engine.Models.General.PlatformID;

namespace Grayjay.Desktop.Tests;

[TestClass]
public class DownloadExportTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Initialize() => Directory.CreateDirectory(_directory = Path.Combine(Path.GetTempPath(), "export-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var file in Directory.GetFiles(_directory))
        {
            File.Delete(file);
        }
        Directory.Delete(_directory);
    }

    [DataTestMethod]
    [DataRow("video/mp4", "avc1.64001F", "audio/mp4", "mp4a.40.2", "mp4")]
    [DataRow("video/webm", "vp9", "audio/webm", "opus", "webm")]
    [DataRow("video/webm", "vp09.00.40.08", "audio/webm", "vorbis", "webm")]
    [DataRow("video/mp4", "avc1.4d401e", "audio/webm", "opus", "mp4")]
    [DataRow("video/webm", "vp9", "audio/mp4", "mp4a.40.2", "mkv")]
    [DataRow("video/mp4", "avc1.4d401e", "audio/webm", "vorbis", "mkv")]
    [DataRow("video/mp4", "av01.0.08M.08", "audio/webm", "opus", "mp4")]
    [DataRow("video/webm", "av01.0.08M.08", "audio/webm", "opus", "webm")]
    [DataRow("video/mp4", "", "audio/mp4", "", "mp4")]
    [DataRow("video/webm", null, "audio/webm", null, "webm")]
    [DataRow("application/vnd.apple.mpegurl", "", "audio/mp4", "mp4a.40.2", "mp4")]
    [DataRow("video/x-matroska", "", "audio/mp4", "mp4a.40.2", "mkv")]
    public void ExportContainerMatchesBothStreams(string videoContainer, string? videoCodec, string audioContainer, string? audioCodec, string expected)
    {
        Assert.AreEqual(expected, Call<string>("ExportContainerFor", videoContainer, videoCodec, audioContainer, audioCodec));
    }

    [TestMethod]
    public void ExportFileNameIsSanitizedAndUsesTheMergedContainer()
    {
        var video = new LocalVideoSource { Container = "video/webm", Codec = "vp9" };
        var audio = new LocalAudioSource { Container = "audio/mp4", Codec = "mp4a.40.2" };

        Assert.AreEqual("a_b_c.mkv", Call<string>("ExportFileName", "a/b:c", video, audio));
        Assert.AreEqual("a_b_c.webm", Call<string>("ExportFileName", "a/b:c", video, null));
        Assert.AreEqual("a_b_c.mp4a", Call<string>("ExportFileName", "a/b:c", null, audio));
        string traversal = Call<string>("ExportFileName", "..\\..\\x/y", video, null);
        Assert.AreEqual(traversal, Path.GetFileName(traversal));
        Assert.IsFalse(traversal.Contains('/') || traversal.Contains('\\'));
    }

    [TestMethod]
    public void ExportOfMissingFileReportsTheFailure()
    {
        var download = Local(new VideoDescriptor(), new LocalVideoSource { Container = "video/mp4", FilePath = Path.Combine(_directory, "missing.mp4") }, null);

        Assert.IsNotNull(Call<string?>("ExportToFolder", download, _directory));
        Assert.IsNotNull(Call<string?>("ExportToFolder", Local(new VideoDescriptor(), null, null), _directory));
    }

    [TestMethod]
    public void FailedMergeIsReportedInsteadOfIgnored()
    {
        RequireFfmpeg();
        string videoPath = Path.Combine(_directory, "video.mp4");
        string audioPath = Path.Combine(_directory, "audio.m4a");
        File.WriteAllText(videoPath, "not media");
        File.WriteAllText(audioPath, "not media");
        var download = Local(new UnMuxedVideoDescriptor(),
            new LocalVideoSource { Container = "video/mp4", Codec = "avc1", FilePath = videoPath },
            new LocalAudioSource { Container = "audio/mp4", Codec = "mp4a.40.2", FilePath = audioPath });

        string? failure = Call<string?>("ExportToFolder", download, _directory);

        Assert.IsNotNull(failure);
        StringAssert.Contains(failure, "FFmpeg exited with code");
    }

    [TestMethod]
    public void MergedExportContainsAudio()
    {
        RequireFfmpeg();
        string videoPath = CreateMedia("video.mp4", withVideo: true, withAudio: false);
        string audioPath = CreateMedia("audio.m4a", withVideo: false, withAudio: true);
        var download = Local(new UnMuxedVideoDescriptor(),
            new LocalVideoSource { Container = "video/mp4", Codec = "mp4v", FilePath = videoPath },
            new LocalAudioSource { Container = "audio/mp4", Codec = "mp4a.40.2", FilePath = audioPath });

        Assert.IsNull(Call<string?>("ExportToFolder", download, _directory));

        string exported = Path.Combine(_directory, "Export__test.mp4");
        Assert.IsTrue(File.Exists(exported));
        var result = RunFfmpeg("-nostdin", "-v", "error", "-i", exported, "-map", "0:a:0", "-f", "null", "-");
        Assert.AreEqual(0, result.ExitCode, result.Error);
    }

    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void VideoOnlyFileIsMissingAudioWhateverTheDescriptor(bool unmuxed, bool restored)
    {
        RequireFfmpeg();
        string videoPath = CreateMedia("video.mp4", withVideo: true, withAudio: false);
        var download = Local(unmuxed ? new UnMuxedVideoDescriptor() : new VideoDescriptor(), new LocalVideoSource { Container = "video/mp4", FilePath = videoPath }, null);

        Assert.AreEqual("NoAudio", CheckExportAudio(restored ? SaveAndLoad(download) : download));
    }

    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void VideoFileWithAnAudioTrackHasAudioWhateverTheDescriptor(bool unmuxed, bool restored)
    {
        RequireFfmpeg();
        string videoPath = CreateMedia("muxed.mp4", withVideo: true, withAudio: true);
        var download = Local(unmuxed ? new UnMuxedVideoDescriptor() : new VideoDescriptor(), new LocalVideoSource { Container = "video/mp4", FilePath = videoPath }, null);

        Assert.AreEqual("HasAudio", CheckExportAudio(restored ? SaveAndLoad(download) : download), "An HLS variant is stored as video/mp4 but carries audio");
    }

    [DataTestMethod]
    [DataRow("mp4", "title", new[] { "-metadata", "title=Stream #0:0: Audio: metadata text" })]
    [DataRow("mp4", "multi-line comment", new[] { "-metadata", "comment=line\n  Stream #0:1: Audio: aac" })]
    [DataRow("mp4", "custom tag name", new[] { "-movflags", "use_metadata_tags", "-metadata", "Stream #0:1: Audio: aac=value" })]
    [DataRow("mp4", "custom tag key", new[] { "-movflags", "use_metadata_tags", "-metadata", "Stream #0:0=Audio: metadata text" })]
    [DataRow("matroska", "multi-line title", new[] { "-metadata", "title=first line\nStream #0:0: Audio: spoof" })]
    [DataRow("matroska", "stream language", new[] { "-metadata:s:v:0", "language=xx): Audio: spoof" })]
    [DataRow("matroska", "stream title", new[] { "-metadata:s:v:0", "title=  Stream #0:1: Audio: aac" })]
    public void AudioTextInMetadataIsNotAnAudioStream(string format, string description, string[] metadataArguments)
    {
        RequireFfmpeg();
        string extension = format == "mp4" ? "mp4" : "mkv";
        string videoPath = CreateMedia("metadata." + extension, withVideo: true, withAudio: false, format, metadataArguments);
        string muxedPath = CreateMedia("metadata-muxed." + extension, withVideo: true, withAudio: true, format, metadataArguments);

        Assert.AreEqual("NoAudio", CheckExportAudio(Local(new VideoDescriptor(), new LocalVideoSource { Container = "video/mp4", FilePath = videoPath }, null)), description);
        Assert.AreEqual("HasAudio", CheckExportAudio(Local(new VideoDescriptor(), new LocalVideoSource { Container = "video/mp4", FilePath = muxedPath }, null)), description);
    }

    [TestMethod]
    public void MuxedAudioWithAVideoLikeLanguageHasAudio()
    {
        RequireFfmpeg();
        string muxedPath = CreateMedia("audio-language.mkv", withVideo: true, withAudio: true, "matroska", "-metadata:s:a:0", "language=xx): Video: spoof");

        Assert.AreEqual("HasAudio", CheckExportAudio(Local(new VideoDescriptor(), new LocalVideoSource { Container = "video/x-matroska", FilePath = muxedPath }, null)));
    }

    [TestMethod]
    public void AudioInsideAnMpegTsProgramIsFound()
    {
        RequireFfmpeg();
        string silentPath = CreateMedia("program.ts", withVideo: true, withAudio: false, "mpegts");
        string muxedPath = CreateMedia("program-muxed.ts", withVideo: true, withAudio: true, "mpegts");

        Assert.AreEqual("NoAudio", CheckExportAudio(Local(new VideoDescriptor(), new LocalVideoSource { Container = "video/mp2t", FilePath = silentPath }, null)));
        Assert.AreEqual("HasAudio", CheckExportAudio(Local(new VideoDescriptor(), new LocalVideoSource { Container = "video/mp2t", FilePath = muxedPath }, null)));
    }

    [TestMethod]
    public void SeparateAudioOrAudioOnlyIsNotProbed()
    {
        string missingFile = Path.Combine(_directory, "never-probed.mp4");
        var video = new LocalVideoSource { Container = "video/mp4", FilePath = missingFile };
        var audio = new LocalAudioSource { Container = "audio/mp4", FilePath = missingFile };

        Assert.AreEqual("HasAudio", CheckExportAudio(Local(new UnMuxedVideoDescriptor(), video, audio)));
        Assert.AreEqual("HasAudio", CheckExportAudio(Local(new UnMuxedVideoDescriptor(), null, audio)));
    }

    [TestMethod]
    public void UnreadableOrMissingVideoFileLeavesAudioUnknown()
    {
        RequireFfmpeg();
        string unreadablePath = Path.Combine(_directory, "unreadable.mp4");
        File.WriteAllText(unreadablePath, "not media");

        Assert.AreEqual("Unknown", CheckExportAudio(Local(new VideoDescriptor(), new LocalVideoSource { Container = "video/mp4", FilePath = unreadablePath }, null)));
        Assert.AreEqual("Unknown", CheckExportAudio(Local(new VideoDescriptor(), new LocalVideoSource { Container = "video/mp4", FilePath = Path.Combine(_directory, "missing.mp4") }, null)));
    }

    [TestMethod]
    [DoNotParallelize]
    public void FfmpegThatCannotStartLeavesAudioUnknown()
    {
        RequireFfmpeg();
        string videoPath = CreateMedia("video.mp4", withVideo: true, withAudio: false);
        var download = Local(new VideoDescriptor(), new LocalVideoSource { Container = "video/mp4", FilePath = videoPath }, null);

        WithFfmpegCommand(Path.Combine(_directory, "no-such-ffmpeg"), () => Assert.AreEqual("Unknown", CheckExportAudio(download)));
    }

    [DataTestMethod]
    [DoNotParallelize]
    [DataRow(0, false, "HasAudio")]
    [DataRow(234, true, "NoAudio")]
    [DataRow(1, false, "Unknown")]
    public void NoAudioNeedsTheAudioMapRejected(int exitCode, bool printMapRejected, string expected)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The scripted FFmpeg stand-in needs a Unix shell");
        }
        var lines = new List<string> { "printf 'some other error\\n' >&2" };
        if (printMapRejected)
        {
            lines.Add("printf \"Stream map '' matches no streams.\\n\" >&2");
        }
        string markerPath = Path.Combine(_directory, "scripted-ffmpeg-ran");
        lines.Add("printf ran > '" + markerPath + "'");
        string scriptPath = Path.Combine(_directory, "scripted-ffmpeg");
        File.WriteAllText(scriptPath, "#!/bin/sh\n" + string.Join("\n", lines) + "\nexit " + exitCode + "\n");
        File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var download = Local(new VideoDescriptor(), new LocalVideoSource { Container = "video/mp4", FilePath = Path.Combine(_directory, "video.mp4") }, null);

        WithFfmpegCommand(scriptPath, () => Assert.AreEqual(expected, CheckExportAudio(download)));
        Assert.IsTrue(File.Exists(markerPath), "The scripted FFmpeg stand-in did not run");
    }

    private static void WithFfmpegCommand(string ffmpegCommand, Action action)
    {
        var commandField = typeof(FFMPEG).GetField("_ffmpegCommand", BindingFlags.NonPublic | BindingFlags.Static)!;
        var availableField = typeof(FFMPEG).GetField("_isFFMPEGAvailable", BindingFlags.NonPublic | BindingFlags.Static)!;
        object? command = commandField.GetValue(null);
        object? available = availableField.GetValue(null);
        try
        {
            commandField.SetValue(null, ffmpegCommand);
            availableField.SetValue(null, true);
            action();
        }
        finally
        {
            commandField.SetValue(null, command);
            availableField.SetValue(null, available);
        }
    }

    [TestMethod]
    public void BulkExportKeepsFailuresAndAudioWarningsApart()
    {
        RequireFfmpeg();
        string outputFolder = Path.Combine(_directory, "out");
        Directory.CreateDirectory(outputFolder);
        string unreadablePath = Path.Combine(_directory, "unreadable.mp4");
        File.WriteAllText(unreadablePath, "not media");
        var downloads = new[]
        {
            Local(new UnMuxedVideoDescriptor(), new LocalVideoSource { Container = "video/mp4", FilePath = CreateMedia("muxed.mp4", withVideo: true, withAudio: true) }, null, "Muxed"),
            Local(new UnMuxedVideoDescriptor(), new LocalVideoSource { Container = "video/mp4", FilePath = CreateMedia("video.mp4", withVideo: true, withAudio: false) }, null, "Silent"),
            Local(new VideoDescriptor(), new LocalVideoSource { Container = "video/mp4", FilePath = unreadablePath }, null, "Unreadable"),
            Local(new VideoDescriptor(), new LocalVideoSource { Container = "video/mp4", FilePath = Path.Combine(_directory, "missing.mp4") }, null, "Missing"),
            Local(new UnMuxedVideoDescriptor(), new LocalVideoSource { Container = "video/mp4", Codec = "mp4v", FilePath = CreateMedia("merge-video.mp4", withVideo: true, withAudio: false) },
                new LocalAudioSource { Container = "audio/mp4", Codec = "mp4a.40.2", FilePath = Path.Combine(_directory, "missing.m4a") }, "MissingAudioFile")
        };
        try
        {
            object results = Call<object>("ExportAllToFolder", downloads, outputFolder);

            var failures = Results(results, "Failures");
            Assert.AreEqual(2, failures.Count, string.Join("\n", failures));
            StringAssert.StartsWith(failures[0], "Missing: ");
            StringAssert.StartsWith(failures[1], "MissingAudioFile: ");
            CollectionAssert.AreEqual(new[] { "Silent" }, Results(results, "WithoutAudio"));
            CollectionAssert.AreEqual(new[] { "Unreadable" }, Results(results, "AudioUnverified"));
        }
        finally
        {
            foreach (var file in Directory.GetFiles(outputFolder))
            {
                File.Delete(file);
            }
            Directory.Delete(outputFolder);
        }
    }

    [TestMethod]
    public void ExportMessagesNameEachAudioOutcome()
    {
        Assert.AreEqual("Video [a] is ready at the location below", Call<string>("ExportFinishedText", "a", AudioOutcome("HasAudio")));
        StringAssert.Contains(Call<string>("ExportFinishedText", "a", AudioOutcome("NoAudio")), "exported without audio");
        StringAssert.Contains(Call<string>("ExportFinishedText", "a", AudioOutcome("Unknown")), "could not be verified");

        var resultsType = typeof(DownloadController).GetNestedType("ExportResults", BindingFlags.NonPublic)!;
        object results = Activator.CreateInstance(resultsType, new List<string> { "Broken: reason" }, new List<string> { "Silent" }, new List<string> { "Unreadable" })!;
        string summary = Call<string>("ExportsFinishedText", 4, results);

        Assert.AreEqual("3 of 4 videos are ready at the location below"
            + "\n\nFailed:\nBroken: reason"
            + "\n\nExported without audio, as no audio was downloaded:\nSilent"
            + "\n\nExported, but audio could not be verified:\nUnreadable", summary);
    }

    [TestMethod]
    public void QuietFfmpegRunStillReportsLines()
    {
        RequireFfmpeg();
        string mediaPath = CreateMedia("probe.mp4", withVideo: true, withAudio: true);
        var lines = new List<string>();

        FFMPEG.ExecuteSafe(new[] { "-hide_banner", "-nostdin", "-i", mediaPath }, print: false, onLog: (line, isError) =>
        {
            if (line != null)
            {
                lock (lines)
                {
                    lines.Add(line);
                }
            }
        });

        lock (lines)
        {
            Assert.IsTrue(lines.Any(line => line.Contains("Stream #") && line.Contains("Video:")), string.Join("\n", lines));
            Assert.IsTrue(lines.Any(line => line.Contains("Stream #") && line.Contains("Audio:")), string.Join("\n", lines));
        }
    }

    private static T Call<T>(string name, params object?[] arguments)
    {
        var method = typeof(DownloadController).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
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

    private static string CheckExportAudio(VideoLocal download) => Call<object>("CheckExportAudio", download).ToString()!;

    private static object AudioOutcome(string name) => Enum.Parse(typeof(DownloadController).GetNestedType("ExportAudio", BindingFlags.NonPublic)!, name);

    private static List<string> Results(object results, string name) => (List<string>)results.GetType().GetProperty(name)!.GetValue(results)!;

    private static VideoLocal SaveAndLoad(VideoLocal download) => GJsonSerializer.Deserialize<VideoLocal>(GJsonSerializer.Serialize(download));

    private static VideoLocal Local(VideoDescriptor originalDescriptor, LocalVideoSource? video, LocalAudioSource? audio, string name = "Export: test")
    {
        var details = new PlatformVideoDetails
        {
            ID = new PlatformID { Value = "export-" + Guid.NewGuid().ToString("N") },
            Name = name,
            Url = "https://test/video",
            Video = originalDescriptor
        };
        var local = new VideoLocal(details);
        if (video != null)
        {
            local.VideoSources.Add(video);
        }
        if (audio != null)
        {
            local.AudioSources.Add(audio);
        }
        return local;
    }

    private string CreateMedia(string name, bool withVideo, bool withAudio, string format = "mp4", params string[] metadataArguments)
    {
        string path = Path.Combine(_directory, name);
        var arguments = new List<string> { "-nostdin", "-y", "-v", "error" };
        if (withVideo)
        {
            arguments.AddRange(new[] { "-f", "lavfi", "-i", "testsrc=size=64x64:rate=10" });
        }
        if (withAudio)
        {
            arguments.AddRange(new[] { "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000" });
        }
        arguments.AddRange(new[] { "-t", "1" });
        if (withVideo)
        {
            arguments.AddRange(new[] { "-c:v", "mpeg4" });
        }
        if (withAudio)
        {
            arguments.AddRange(new[] { "-c:a", "aac", "-b:a", "32k" });
        }
        arguments.AddRange(metadataArguments);
        arguments.AddRange(new[] { "-f", format, path });
        var result = RunFfmpeg(arguments.ToArray());
        Assert.AreEqual(0, result.ExitCode, result.Error);
        return path;
    }

    private static void RequireFfmpeg()
    {
        try
        {
            if (!FFMPEG.IsFFMPEGAvailable())
            {
                Assert.Inconclusive("FFmpeg is required for export tests");
            }
        }
        catch (InvalidOperationException exception)
        {
            Assert.Inconclusive("FFmpeg is required for export tests: " + exception.Message);
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
            Assert.Inconclusive("FFmpeg is required for export tests: " + exception.Message);
            throw;
        }
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        output.Wait();
        return (process.ExitCode, error.Result);
    }
}
