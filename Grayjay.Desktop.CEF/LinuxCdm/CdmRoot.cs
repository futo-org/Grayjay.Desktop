using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Grayjay.Desktop.CEF.LinuxCdm;

// Extract only the host and its runtime libraries. Windows users need neither
// a Linux installation nor permission to create symbolic links.
internal static class CdmRoot
{
    internal static async Task BuildAsync(string image, string cdm, string output, CancellationToken cancellation)
    {
        if (await ValidAsync(output, cancellation)) { Directory.CreateDirectory(Path.Combine(output, "tmp")); EnsureExecutable(output); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string temporary = output + ".partial-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(temporary);
        try
        {
            await ExtractAsync(image, temporary, cancellation);
            Directory.CreateDirectory(Path.Combine(temporary, "tmp"));
            string destination = Path.Combine(temporary, "opt", "widevine", "libwidevinecdm.so");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using (var input = File.OpenRead(cdm))
            await using (var target = File.Create(destination)) await input.CopyToAsync(target, cancellation);
            var receipt = new Dictionary<string, string>();
            foreach (string file in Directory.EnumerateFiles(temporary, "*", SearchOption.AllDirectories))
            {
                await using var input = File.OpenRead(file);
                receipt[Path.GetRelativePath(temporary, file).Replace('\\', '/')] = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellation));
            }
            foreach (string required in new[] { "usr/local/bin/cdm-host", "lib64/ld-linux-x86-64.so.2", "lib/x86_64-linux-gnu/libc.so.6" })
                if (!receipt.ContainsKey(required)) throw new InvalidDataException("CDM runtime library is missing: " + required);
            EnsureExecutable(temporary);
            await File.WriteAllTextAsync(Path.Combine(temporary, "receipt.json"), JsonSerializer.Serialize(receipt), cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
            Directory.Move(temporary, output);
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true); }
    }
    private static void EnsureExecutable(string root)
    {
        if (!OperatingSystem.IsWindows())
        {
            string host = Path.Combine(root, "usr", "local", "bin", "cdm-host");
            File.SetUnixFileMode(host, File.GetUnixFileMode(host) | UnixFileMode.UserExecute);
        }
    }
    private static async Task<bool> ValidAsync(string root, CancellationToken cancellation)
    {
        string receipt = Path.Combine(root, "receipt.json");
        if (!File.Exists(receipt)) return false;
        try
        {
            var files = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(receipt, cancellation));
            if (files is null || !files.ContainsKey("usr/local/bin/cdm-host") || !files.ContainsKey("opt/widevine/libwidevinecdm.so")) return false;
            foreach (var file in files)
                if (!await VerifiedDownload.MatchesAsync(Target(root, file.Key), file.Value, cancellation)) return false;
            return true;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return false; }
    }
    internal static async Task ExtractAsync(string image, string root, CancellationToken cancellation)
    {
        await using var input = File.OpenRead(image);
        await using var gzip = new GZipStream(input, CompressionMode.Decompress);
        var header = new byte[110];
        for (int entries = 0; entries < 10000; entries++)
        {
            await gzip.ReadExactlyAsync(header, cancellation);
            if (Encoding.ASCII.GetString(header, 0, 6) != "070701") throw new InvalidDataException("Unsupported CDM archive.");
            uint Field(int index) => uint.Parse(Encoding.ASCII.GetString(header, 6 + index * 8, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            uint mode = Field(1), size = Field(6), nameSize = Field(11);
            if (nameSize is < 2 or > 4096 || size > 128 * 1024 * 1024) throw new InvalidDataException("Invalid CDM archive entry.");
            byte[] name = new byte[nameSize]; await gzip.ReadExactlyAsync(name, cancellation);
            if (name[^1] != 0) throw new InvalidDataException("Invalid CDM archive name.");
            string relative = Encoding.UTF8.GetString(name, 0, name.Length - 1);
            await SkipAsync(gzip, (4 - (110 + nameSize) % 4) % 4, cancellation);
            if (relative == "TRAILER!!!") return;
            if (relative == "." && (mode & 0xF000) == 0x4000 && size == 0) continue;
            string target = Target(root, relative);
            bool wanted = relative == "usr/local/bin/cdm-host" || relative.StartsWith("lib64/", StringComparison.Ordinal) ||
                (relative.StartsWith("lib/", StringComparison.Ordinal) && !relative.StartsWith("lib/modules/", StringComparison.Ordinal));
            if (wanted && (mode & 0xF000) == 0xA000) throw new InvalidDataException("CDM libraries must be bundled as regular files.");
            if (wanted && (mode & 0xF000) == 0x8000)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var file = File.Create(target);
                await CopyAsync(gzip, file, size, cancellation);
            }
            else await SkipAsync(gzip, size, cancellation);
            await SkipAsync(gzip, (4 - size % 4) % 4, cancellation);
        }
        throw new InvalidDataException("CDM archive has too many entries.");
    }
    private static string Target(string root, string name)
    {
        if (name.StartsWith('/') || name.Contains('\\') || name.Contains(':') || name.Split('/').Any(x => x is ".." or "." or ""))
            throw new InvalidDataException("Unsafe CDM archive path.");
        string target = Path.GetFullPath(Path.Combine(root, name));
        if (!target.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsafe CDM archive path.");
        return target;
    }
    private static Task SkipAsync(Stream input, long count, CancellationToken cancellation) => CopyAsync(input, Stream.Null, count, cancellation);
    private static async Task CopyAsync(Stream input, Stream output, long count, CancellationToken cancellation)
    {
        byte[] buffer = new byte[64 * 1024];
        while (count > 0)
        {
            int read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, count)), cancellation);
            if (read == 0) throw new InvalidDataException("Truncated CDM archive.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellation); count -= read;
        }
    }
}
