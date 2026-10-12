using Grayjay.Desktop.CEF.LinuxCdm;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;

internal static class BlinkPlaybackTests
{
    internal static async Task RunAsync(string root, int iterations = 4, string? runtimeId = null)
    {
        if (!RuntimeAssets.Supported) throw new PlatformNotSupportedException();
        if (iterations < 1 || iterations > 100) throw new ArgumentOutOfRangeException(nameof(iterations));
        root = Path.GetFullPath(root);
        var clock = Stopwatch.StartNew();
        var cache = Path.Combine(Path.GetTempPath(), "grayjay-cdm-blink-probe", "cache");
        await using var downloader = new WidevineDownloader(Path.Combine(cache, "widevine"));
        var cdm = await downloader.EnsureAsync(_ => Task.CompletedTask, default);
        runtimeId ??= OperatingSystem.IsWindows() ? "win-x64" : System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        if (OperatingSystem.IsWindows() ? runtimeId != "win-x64" : runtimeId is not "osx-arm64" and not "osx-x64")
            throw new ArgumentException("Unexpected playback runtime ID.", nameof(runtimeId));
        var runtimeDir = Path.Combine(root, "runtimes", runtimeId);
        var guestRoot = Path.Combine(cache, "roots", cdm.Sha256);
        await CdmRoot.BuildAsync(Path.Combine(root, "guest", "initramfs.cpio.gz"), cdm.Path, guestRoot, default);
        var runtime = new PlayerRuntime(runtimeDir, Path.Combine(runtimeDir, OperatingSystem.IsWindows() ? "blink.exe" : "blink"), "", "") { GuestRoot = guestRoot };
        Console.WriteLine("Runtime ready");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var keys = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PlaybackGuestSession? session = null;
        await using var playback = new PlaybackGuestSession(_ => Task.FromResult(runtime), (r, callback) => new BlinkGuest(r, Path.Combine(cache, Guid.NewGuid().ToString("N")), callback, 200UL * 1024 * 1024), async (playbackToken, e) =>
        {
            Console.WriteLine(clock.Elapsed + " Event: " + e.GetProperty("event").GetString());
            if (e.GetProperty("event").GetString() == "message")
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var content = new ByteArrayContent(Convert.FromBase64String(e.GetProperty("data").GetString()!));
                        content.Headers.ContentType = new("application/octet-stream");
                        var network = Stopwatch.StartNew();
                        using var response = await http.PostAsync("https://proxy.uat.widevine.com/proxy", content);
                        response.EnsureSuccessStatusCode();
                        var licenseBytes = await response.Content.ReadAsByteArrayAsync();
                        Console.WriteLine($"PHASE NETWORK {network.Elapsed.TotalMilliseconds:F0}");
                        var update = Stopwatch.StartNew();
                        await session!.RequestAsync("probe", "UPDATE", e.GetProperty("session").GetString() + " " + Convert.ToBase64String(licenseBytes), e.GetProperty("session").GetString());
                        Console.WriteLine($"PHASE UPDATE {update.Elapsed.TotalMilliseconds:F0}");
                    }
                    catch (Exception error) { keys.TrySetException(error); }
                });
            }
            if (e.GetProperty("event").GetString() == "keys" && e.GetProperty("statuses").EnumerateArray().Any(s => s.GetInt32() == 0)) keys.TrySetResult();
        }, default, "tcg");
        session = playback;
        var media = await http.GetByteArrayAsync("https://storage.googleapis.com/shaka-demo-assets/angel-one-widevine/v-0144p-0100k-libx264.mp4");
        Console.WriteLine($"Testing complete reference media ({media.Length} bytes)");
        int at = 0;
        while (System.Text.Encoding.ASCII.GetString(media, at + 4, 4) != "moof") at += checked((int)BinaryPrimitives.ReadUInt32BigEndian(media.AsSpan(at)));
        var wire = new byte[4 + media.Length];
        BinaryPrimitives.WriteUInt32BigEndian(wire, (uint)at); media.CopyTo(wire, 4);

        var initial = Stopwatch.StartNew();
        await playback.StartAsync("probe", "");
        Console.WriteLine($"INITIALIZE {initial.Elapsed.TotalMilliseconds:F0}");
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            keys = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var lap = Stopwatch.StartNew();
            await playback.StartAsync("probe", "");
            var challenge = Stopwatch.StartNew();
            await playback.RequestAsync("probe", "SESSION", "AAAAPnBzc2gAAAAA7e+LqXnWSs6jyCfc1R0h7QAAAB4iFnNoYWthX2NlYzJmNjRhYTc4OTBhMTFI49yVmwY=");
            Console.WriteLine($"PHASE CHALLENGE {challenge.Elapsed.TotalMilliseconds:F0}");
            await keys.Task.WaitAsync(TimeSpan.FromSeconds(120));
            var license = lap.Elapsed.TotalMilliseconds;
            var result = await playback.RequestAsync("probe", "FRAGMENT", Convert.ToBase64String(wire));
            if (result.GetProperty("samples").GetInt32() != 1290) throw new Exception("Unexpected decrypted sample count");
            var decrypted = Convert.FromBase64String(result.GetProperty("data").GetString()!);
            const string expectedHash = "39d48f0c2611ebf76850950a38d046b2b9996f666f3f250d9af6ebf500ada9da";
            if (!Convert.ToHexString(SHA256.HashData(decrypted)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Decrypted reference media differs from the expected bytes.");
            Console.WriteLine($"DECRYPTED {result.GetProperty("samples")} samples; BENCH {iteration} LICENSE {license:F0} DECRYPT {lap.Elapsed.TotalMilliseconds - license:F0}");
            await playback.CloseAsync("probe");
        }



        Console.WriteLine("PASS native Blink reference playback and repeated license sessions");
    }
}
