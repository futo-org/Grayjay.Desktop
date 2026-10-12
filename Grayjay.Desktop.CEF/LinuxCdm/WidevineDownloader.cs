using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Grayjay.Desktop.CEF.LinuxCdm;

internal sealed class WidevineDownloader : IAsyncDisposable
{
    private readonly string cache;
    private readonly SemaphoreSlim install = new(1, 1);
    private readonly CancellationTokenSource stop = new();
    private readonly object updates = new();
    private readonly Func<Func<DownloadProgress, Task>, CancellationToken, Task<InstalledCdm>> fetch;
    private Task? updateTask;
    internal WidevineDownloader(string cache,
        Func<Func<DownloadProgress, Task>, CancellationToken, Task<InstalledCdm>>? fetch = null)
    { this.cache = cache; this.fetch = fetch ?? FetchLatestAsync; }

    internal InstalledCdm? Cached()
    {
        try
        {
            var saved = JsonSerializer.Deserialize<InstalledCdm>(File.ReadAllText(Path.Combine(cache, "current.json")));
            if (saved is null || !Version.TryParse(saved.Version, out _) || saved.Sha256.Length != 64) return null;
            string expected = Path.Combine(cache, saved.Version, "libwidevinecdm.so");
            return saved.Path == expected && File.Exists(expected) ? saved : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }
    internal async Task<InstalledCdm> EnsureAsync(Func<DownloadProgress, Task> progress, CancellationToken cancellation)
    {
        var cached = Cached();
        if (cached is not null && await VerifiedDownload.MatchesAsync(cached.Path, cached.Sha256, cancellation))
        {
            ScheduleUpdate();
            return cached;
        }
        await install.WaitAsync(cancellation);
        try
        {
            var current = Cached();
            if (current is not null && await VerifiedDownload.MatchesAsync(current.Path, current.Sha256, cancellation))
            {
                ScheduleUpdate();
                return current;
            }
            var installed = await fetch(progress, cancellation);
            await File.WriteAllTextAsync(Path.Combine(cache, "checked-at.txt"), installed.Version, cancellation);
            return installed;
        }
        finally { install.Release(); }
    }
    private void ScheduleUpdate()
    {
        lock (updates)
        {
            if (stop.IsCancellationRequested || updateTask is { IsCompleted: false }) return;
            string checkedAt = Path.Combine(cache, "checked-at.txt");
            if (File.Exists(checkedAt) && DateTime.UtcNow - File.GetLastWriteTimeUtc(checkedAt) < TimeSpan.FromDays(1)) return;
            updateTask = Task.Run(UpdateAsync);
        }
    }
    private async Task UpdateAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        bool acquired = false;
        try
        {
            await install.WaitAsync(timeout.Token); acquired = true;
            await File.WriteAllTextAsync(Path.Combine(cache, "checked-at.txt"), "checking", timeout.Token);
            var installed = await fetch(_ => Task.CompletedTask, timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(cache, "checked-at.txt"), installed.Version, timeout.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception e) { Console.Error.WriteLine("Playback component update unavailable: " + e.Message); }
        finally { if (acquired) install.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        Task? task;
        lock (updates) { stop.Cancel(); task = updateTask; }
        if (task is not null) await task;
    }
    private async Task<InstalledCdm> FetchLatestAsync(Func<DownloadProgress, Task> progress, CancellationToken cancellation)
    {
        await progress(new("cdm", "Finding protected playback components"));
        var request = new Dictionary<string, object>
        {
            ["protocol"] = "3.1", ["acceptformat"] = "crx3", ["@updater"] = "chrome",
            ["prodversion"] = "145.0.0.0", ["updaterversion"] = "145.0.0.0", ["prodchannel"] = "stable",
            ["@os"] = "linux", ["arch"] = "x64", ["os"] = new { platform = "linux", arch = "x86_64" },
            ["app"] = new[] { new { appid = "oimompecagnajdejgnnjijobebaeigek", version = "0.0.0.0", enabled = true, updatecheck = new { } } }
        };
        using var body = new StringContent(JsonSerializer.Serialize(new { request }), Encoding.UTF8, "application/json");
        using var response = await VerifiedDownload.Http.PostAsync("https://update.googleapis.com/service/update2/json", body, cancellation);
        response.EnsureSuccessStatusCode();
        string text = await response.Content.ReadAsStringAsync(cancellation);
        if (text.StartsWith(")]}'")) text = text[(text.IndexOf('\n') + 1)..];
        using var document = JsonDocument.Parse(text);
        var update = document.RootElement.GetProperty("response").GetProperty("app")[0].GetProperty("updatecheck");
        if (update.GetProperty("status").GetString() != "ok")
            throw new InvalidOperationException("Google did not offer a compatible Linux Widevine component. Please try again later.");
        var manifest = update.GetProperty("manifest");
        string version = manifest.GetProperty("version").GetString()!;
        if (!Version.TryParse(version, out _)) throw new InvalidDataException("Invalid Widevine component version.");
        var existing = Cached();
        if (existing?.Version == version && await VerifiedDownload.MatchesAsync(existing.Path, existing.Sha256, cancellation)) return existing;
        var package = manifest.GetProperty("packages").GetProperty("package")[0];
        string name = package.GetProperty("name").GetString()!;
        if (name != Path.GetFileName(name) || name.Contains('\\')) throw new InvalidDataException("Invalid Widevine package name.");
        string codebase = update.GetProperty("urls").GetProperty("url").EnumerateArray()
            .Select(x => x.GetProperty("codebase").GetString()!)
            .First(x => x.StartsWith("https://dl.google.com/", StringComparison.Ordinal));
        var asset = new DownloadAsset(new Uri(new Uri(codebase), name).AbsoluteUri,
            package.GetProperty("hash_sha256").GetString()!, package.GetProperty("size").GetInt64());
        string archive = Path.Combine(cache, "downloads", asset.Sha256 + ".crx3");
        await VerifiedDownload.FetchAsync(asset, archive, "cdm", progress, cancellation);
        byte[] bytes = await File.ReadAllBytesAsync(archive, cancellation);
        var zipBytes = Crx3.VerifyWidevine(bytes);
        using var zipStream = new MemoryStream(zipBytes.ToArray(), false);
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var cdmManifest = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Widevine package lacks its manifest.");
        if (cdmManifest.Length > 1024 * 1024) throw new InvalidDataException("Widevine manifest is too large.");
        using (var contents = cdmManifest.Open())
        using (var metadata = await JsonDocument.ParseAsync(contents, cancellationToken: cancellation))
        {
            if (metadata.RootElement.GetProperty("version").GetString() != version ||
                !SupportsInterface(metadata.RootElement.GetProperty("x-cdm-interface-versions")))
                throw new InvalidDataException("The downloaded Widevine component is incompatible with this version of Grayjay.");
        }
        var library = zip.GetEntry("_platform_specific/linux_x64/libwidevinecdm.so")
            ?? throw new InvalidDataException("Widevine package lacks its Linux x64 library.");
        if (library.Length < 1024 || library.Length > 128 * 1024 * 1024)
            throw new InvalidDataException("Invalid Widevine library size.");
        string directory = Path.Combine(cache, version); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "libwidevinecdm.so"), temporary = path + ".partial";
        try
        {
            await using (var output = File.Create(temporary))
            await using (var input = library.Open()) await input.CopyToAsync(output, cancellation);
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
        string hash;
        await using (var input = File.OpenRead(path)) hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellation));
        var installed = new InstalledCdm(version, path, hash);
        string receipt = Path.Combine(cache, "current.json"), receiptTemp = receipt + ".partial";
        await File.WriteAllTextAsync(receiptTemp, JsonSerializer.Serialize(installed), cancellation);
        File.Move(receiptTemp, receipt, true);
        File.Delete(archive);
        return installed;
    }
    private static bool SupportsInterface(JsonElement versions) => versions.ValueKind == JsonValueKind.String
        ? versions.GetString()!.Split(',').Any(x => x.Trim() == "10")
        : versions.ValueKind == JsonValueKind.Array && versions.EnumerateArray().Any(x => x.ToString() == "10");
}
