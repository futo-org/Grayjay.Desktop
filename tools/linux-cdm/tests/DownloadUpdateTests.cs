using System.Security.Cryptography;
using System.Text.Json;
using Grayjay.Desktop.CEF.LinuxCdm;

internal static class DownloadUpdateTests
{
    internal static async Task RunAsync(string root)
    {
        string cache = Path.Combine(root, "update-cache"), directory = Path.Combine(cache, "1.0.0.0");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "libwidevinecdm.so");
        byte[] bytes = [1, 2, 3]; await File.WriteAllBytesAsync(path, bytes);
        var installed = new InstalledCdm("1.0.0.0", path, Convert.ToHexString(SHA256.HashData(bytes)));
        await File.WriteAllTextAsync(Path.Combine(cache, "current.json"), JsonSerializer.Serialize(installed));
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<InstalledCdm>(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        await using (var downloader = new WidevineDownloader(cache, (_, cancellation) =>
        {
            Interlocked.Increment(ref attempts); entered.TrySetResult(true);
            return release.Task.WaitAsync(cancellation);
        }))
        {
            var result = await downloader.EnsureAsync(_ => Task.CompletedTask, default).WaitAsync(TimeSpan.FromSeconds(1));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Check(result == installed && !release.Task.IsCompleted, "cached CDM is returned before an update completes");
            var repeated = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => downloader.EnsureAsync(_ => Task.CompletedTask, default)))
                .WaitAsync(TimeSpan.FromSeconds(1));
            Check(repeated.All(x => x == installed) && attempts == 1,
                "concurrent cached playback does not wait for or duplicate the background update");
            release.SetResult(installed);
        }
        await using (var downloader = new WidevineDownloader(cache, (_, _) => throw new Exception("Unexpected update")))
        {
            Check(await downloader.EnsureAsync(_ => Task.CompletedTask, default) == installed,
                "a recent update check avoids another network request");
        }
        File.SetLastWriteTimeUtc(Path.Combine(cache, "checked-at.txt"), DateTime.UtcNow.AddDays(-2));
        var failed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        attempts = 0;
        await using (var downloader = new WidevineDownloader(cache, (_, _) =>
        {
            Interlocked.Increment(ref attempts); failed.TrySetResult(true);
            throw new HttpRequestException("Offline");
        }))
        {
            Check(await downloader.EnsureAsync(_ => Task.CompletedTask, default) == installed,
                "offline updates do not interrupt cached playback");
            await failed.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Check(await downloader.EnsureAsync(_ => Task.CompletedTask, default) == installed && attempts == 1,
                "failed update checks are not repeated for every playback");
        }
        await File.WriteAllBytesAsync(path, [9]);
        attempts = 0;
        await using (var downloader = new WidevineDownloader(cache, (_, _) =>
        {
            Interlocked.Increment(ref attempts); throw new HttpRequestException("No valid cached CDM");
        }))
        {
            try { await downloader.EnsureAsync(_ => Task.CompletedTask, default); throw new Exception("Corrupt CDM accepted"); }
            catch (HttpRequestException) { Check(attempts == 1, "corrupt cached CDMs require a foreground replacement"); }
        }
        await File.WriteAllBytesAsync(path, bytes);
        File.SetLastWriteTimeUtc(Path.Combine(cache, "checked-at.txt"), DateTime.UtcNow.AddDays(-2));
        entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool cancelled = false;
        var pending = new WidevineDownloader(cache, async (_, cancellation) =>
        {
            entered.TrySetResult(true);
            try { await Task.Delay(Timeout.Infinite, cancellation); }
            catch (OperationCanceledException) { cancelled = true; throw; }
            return installed;
        });
        await pending.EnsureAsync(_ => Task.CompletedTask, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await pending.DisposeAsync();
        Check(cancelled, "disposing components cancels the background update");
    }
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
}
