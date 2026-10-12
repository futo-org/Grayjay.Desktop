using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using Grayjay.ClientServer.Models.Downloads;
using Grayjay.ClientServer.Transcoding;

namespace Grayjay.Desktop.Tests;

[TestClass]
public class HlsSegmentCombinationTests
{
    private string _directory = null!;
    private static readonly MethodInfo CombineMethod = typeof(VideoDownload).GetMethod(
        "CombineHLSSegments", BindingFlags.NonPublic | BindingFlags.Static)!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "hls-combination-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (string file in Directory.EnumerateFiles(_directory))
        {
            File.Delete(file);
        }
        Directory.Delete(_directory);
    }

    private static void RequireFfmpeg()
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
    }

    private static void Combine(List<FileInfo>? segments, string target)
    {
        try
        {
            CombineMethod.Invoke(null, new object?[] { segments, target });
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static (int ExitCode, string Output, string Error) RunFfmpeg(params string[] arguments)
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
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }

    private string GenerateMedia(string format, int seconds = 2)
    {
        string source = Path.Combine(_directory, "reference." + (format == "mpegts" ? "ts" : format == "audio" ? "aac" : "mp4"));
        var arguments = new List<string> { "-nostdin", "-y", "-v", "error" };
        if (format != "audio")
        {
            arguments.AddRange(new[] { "-f", "lavfi", "-i", "testsrc2=size=64x48:rate=10" });
        }
        arguments.AddRange(new[] { "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", seconds.ToString() });
        if (format != "audio")
        {
            arguments.AddRange(new[] { "-c:v", "libx264", "-preset", "ultrafast", "-g", "5", "-threads", "1" });
        }
        arguments.AddRange(new[] { "-c:a", "aac", "-b:a", "32k" });
        if (format == "fragmented")
        {
            arguments.AddRange(new[] { "-movflags", "+frag_keyframe+empty_moov", "-frag_duration", "500000" });
        }
        arguments.AddRange(new[] { "-f", format == "audio" ? "adts" : format == "fragmented" ? "mp4" : format, source });
        var result = RunFfmpeg(arguments.ToArray());
        Assert.AreEqual(0, result.ExitCode, result.Error);
        return source;
    }

    private List<FileInfo> SplitMedia(string source, int count, bool initializationSegment = false, bool specialPaths = false)
    {
        byte[] bytes = File.ReadAllBytes(source);
        var segments = new List<FileInfo>();
        int start = 0;
        if (initializationSegment)
        {
            int offset = 0;
            while (Encoding.ASCII.GetString(bytes, offset + 4, 4) != "moof")
            {
                int boxSize = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
                Assert.IsTrue(boxSize >= 8);
                offset += boxSize;
                Assert.IsTrue(offset + 8 <= bytes.Length);
            }
            string initialization = Path.Combine(_directory, "init.mp4");
            File.WriteAllBytes(initialization, bytes[..offset]);
            segments.Add(new FileInfo(initialization));
            start = offset;
        }

        int remaining = bytes.Length - start;
        Assert.IsTrue(remaining >= count, $"Media has {remaining} bytes, not enough for {count} segments");
        for (int segmentIndex = 0; segmentIndex < count; segmentIndex++)
        {
            int end = start + remaining / (count - segmentIndex);
            string fileName = specialPaths ? $"segment {segmentIndex:D5} 'ação' [x]" : $"segment-{segmentIndex:D5}";
            if (specialPaths && !OperatingSystem.IsWindows())
            {
                fileName += "\\literal|name\n";
            }
            string path = Path.Combine(_directory, fileName);
            File.WriteAllBytes(path, bytes[start..end]);
            segments.Add(new FileInfo(path));
            remaining -= end - start;
            start = end;
        }
        return segments;
    }

    private static string[] DecodedHashes(string source, string stream)
    {
        var result = RunFfmpeg("-nostdin", "-v", "error", "-i", source, "-map", stream, "-f", "framemd5", "-");
        Assert.AreEqual(0, result.ExitCode, result.Error);
        return result.Output.Split('\n').Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split(',')[^1].Trim()).ToArray();
    }

    private static void AssertSameMedia(string reference, string target, bool hasVideo)
    {
        if (hasVideo)
        {
            CollectionAssert.AreEqual(DecodedHashes(reference, "0:v:0"), DecodedHashes(target, "0:v:0"));
        }
        CollectionAssert.AreEqual(DecodedHashes(reference, "0:a:0"), DecodedHashes(target, "0:a:0"));
    }

    [DataTestMethod]
    [DataRow("mpegts")]
    [DataRow("fragmented")]
    [DataRow("audio")]
    public void CombinationPreservesDecodedMediaAndOverwritesExistingOutput(string format)
    {
        RequireFfmpeg();
        string reference = GenerateMedia(format);
        var segments = SplitMedia(reference, 4, format == "fragmented", specialPaths: true);
        string target = Path.Combine(_directory, "result." + (format == "audio" ? "aac" : "mp4"));
        File.WriteAllText(target, "stale output");
        var stopwatch = Stopwatch.StartNew();
        for (int attempt = 0; attempt < 2; attempt++)
        {
            Combine(segments, target);
        }
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(20), "Combination or overwrite blocked");
        AssertSameMedia(reference, target, format != "audio");
        Assert.IsTrue(segments.All(segment => File.Exists(segment.FullName)), "Combiner removed caller-owned segments");
    }

    [TestMethod]
    public void LargeSegmentCountPreservesMedia()
    {
        RequireFfmpeg();
        string reference = GenerateMedia("mpegts", 30);
        //Just enough segments for the old concat argument to pass the 128 KiB single-argument limit
        int pathLength = Encoding.UTF8.GetByteCount(Path.Combine(_directory, "segment-00000"));
        var segments = SplitMedia(reference, Math.Min(20000, 131072 / (pathLength + 1) + 64));
        string oldArgument = "concat:" + string.Join('|', segments.Select(segment => segment.FullName));
        Assert.IsTrue(Encoding.UTF8.GetByteCount(oldArgument) > 131072);
        string target = Path.Combine(_directory, "long.mp4");
        Combine(segments, target);
        AssertSameMedia(reference, target, true);
    }

    [TestMethod]
    public void MissingSegmentThrowsFileNotFound()
    {
        RequireFfmpeg();
        string reference = GenerateMedia("mpegts");
        var segments = SplitMedia(reference, 4);
        File.Delete(segments[2].FullName);
        string target = Path.Combine(_directory, "missing.mp4");
        var stopwatch = Stopwatch.StartNew();
        Assert.ThrowsException<FileNotFoundException>(() => Combine(segments, target));
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(20), $"Missing segment took {stopwatch.Elapsed}");
    }

    [TestMethod]
    public void InvalidMediaFailsWithoutHanging()
    {
        RequireFfmpeg();
        var segments = new List<FileInfo>();
        var random = new Random(1234);
        for (int segmentIndex = 0; segmentIndex < 4; segmentIndex++)
        {
            byte[] noise = new byte[8 * 1024 * 1024];
            random.NextBytes(noise);
            string path = Path.Combine(_directory, $"noise-{segmentIndex}");
            File.WriteAllBytes(path, noise);
            segments.Add(new FileInfo(path));
        }
        string target = Path.Combine(_directory, "invalid.mp4");
        var stopwatch = Stopwatch.StartNew();
        Assert.ThrowsException<InvalidDataException>(() => Combine(segments, target));
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"Invalid media took {stopwatch.Elapsed}");
    }

    [TestMethod]
    public void EarlyExitWithSuccessCodeIsReportedAsFailure()
    {
        RequireFfmpeg();
        string reference = GenerateMedia("mpegts", 10);
        var segment = new FileInfo(reference);
        var inputs = Enumerable.Repeat(segment, Math.Max(2, (int)(64L * 1024 * 1024 / segment.Length))).ToList();
        string target = Path.Combine(_directory, "early.mp4");
        int exitCode = FFMPEG.ExecuteSafeWithInputFiles(new[] { "-y", "-i", "pipe:0", "-t", "0.5", "-c", "copy", target }, inputs);
        Assert.AreEqual(-1, exitCode);
    }

    [TestMethod]
    public void EmptyInputIsRejected()
    {
        Assert.ThrowsException<ArgumentException>(() => Combine(new List<FileInfo>(), Path.Combine(_directory, "empty.mp4")));
        Assert.ThrowsException<ArgumentException>(() => Combine(null, Path.Combine(_directory, "empty.mp4")));
    }
}
