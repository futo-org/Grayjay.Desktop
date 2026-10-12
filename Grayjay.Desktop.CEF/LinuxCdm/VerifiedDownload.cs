using System.Security.Cryptography;

namespace Grayjay.Desktop.CEF.LinuxCdm;

internal static class VerifiedDownload
{
    internal static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(15) };
    internal static async Task<bool> MatchesAsync(string path, string hash, CancellationToken cancellation)
    {
        if (!File.Exists(path)) return false;
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellation)).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }
    internal static async Task FetchAsync(DownloadAsset asset, string path, string stage,
        Func<DownloadProgress, Task> progress, CancellationToken cancellation)
    {
        if (asset.Size <= 0 || asset.Size > 512L * 1024 * 1024 || asset.Sha256.Length != 64 || !asset.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid playback component download metadata.");
        var uri = new Uri(asset.Url);
        if (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
            throw new InvalidDataException("Playback components require HTTPS.");
        if (await MatchesAsync(path, asset.Sha256, cancellation)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != asset.Size)
                throw new InvalidDataException("Playback component size does not match its manifest.");
            await progress(new(stage, "Downloading playback components", 0, asset.Size));
            await using (var output = File.Create(temporary))
            await using (var input = await response.Content.ReadAsStreamAsync(cancellation))
            {
                byte[] buffer = new byte[128 * 1024]; long received = 0;
                long lastProgress = Environment.TickCount64;
                while (true)
                {
                    int read = await input.ReadAsync(buffer, cancellation);
                    if (read == 0) break;
                    received += read;
                    if (received > asset.Size) throw new InvalidDataException("Playback component exceeds its declared size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
                    if (Environment.TickCount64 - lastProgress >= 150)
                    {
                        await progress(new(stage, "Downloading playback components", received, asset.Size));
                        lastProgress = Environment.TickCount64;
                    }
                }
                if (received != asset.Size) throw new InvalidDataException("Playback component download is incomplete.");
            }
            await progress(new(stage, "Verifying playback components", asset.Size, asset.Size));
            if (!await MatchesAsync(temporary, asset.Sha256, cancellation))
                throw new InvalidDataException("Playback component verification failed.");
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
    }
}
