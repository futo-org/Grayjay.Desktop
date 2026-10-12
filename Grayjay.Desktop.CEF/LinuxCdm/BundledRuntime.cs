using System.Text.Json;

namespace Grayjay.Desktop.CEF.LinuxCdm;

internal sealed record BundledRuntime(string Directory, string Executable, string Kernel, string Initramfs,
    string KernelSha256, string InitramfsSha256)
{
    internal bool UsesBlink => Path.GetFileName(Executable) is "blink" or "blink.exe";
    internal static bool Available(string applicationDirectory, bool windows)
    {
        try { Resolve(applicationDirectory, windows); return true; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return false; }
    }
    internal static BundledRuntime Resolve(string applicationDirectory, bool windows)
    {
        var app = new DirectoryInfo(Path.GetFullPath(applicationDirectory));
        bool macBundle = !windows && app.Name == "MacOS" && app.Parent?.Name == "Contents";
        string root = macBundle ? Path.Combine(app.Parent!.FullName, "Resources", "playback") : Path.Combine(app.FullName, "playback");
        string runtime = macBundle ? root : Path.Combine(root, "runtime");
        string executable = macBundle ? Path.Combine(app.Parent!.FullName, "Helpers", "blink") :
            Path.Combine(runtime, windows ? "blink.exe" : "blink");
        string guest = Path.Combine(root, "guest");
        string kernel = Path.Combine(guest, "vmlinuz"), initramfs = Path.Combine(guest, "initramfs.cpio.gz");
        if (!File.Exists(executable) || !File.Exists(initramfs) ||
            !File.Exists(Path.Combine(runtime, "blink-runtime.json")) ||
            (windows && !File.Exists(Path.Combine(runtime, "cygwin1.dll"))))
            throw new FileNotFoundException("Bundled playback components are missing. Reinstall Grayjay to restore them.");
        var metadata = JsonSerializer.Deserialize<Metadata>(File.ReadAllText(Path.Combine(guest, "bundle.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (metadata is null || metadata.Version != RuntimeAssets.Release || metadata.Protocol != RuntimeAssets.ProtocolVersion ||
            !Hash(metadata.KernelSha256) || !Hash(metadata.InitramfsSha256))
            throw new InvalidDataException("Bundled playback components are incompatible with this version of Grayjay.");
        return new(runtime, executable, kernel, initramfs, metadata.KernelSha256, metadata.InitramfsSha256);
    }
    internal async Task VerifyGuestAsync(CancellationToken cancellation)
    {
        if ((!UsesBlink && !await VerifiedDownload.MatchesAsync(Kernel, KernelSha256, cancellation)) ||
            !await VerifiedDownload.MatchesAsync(Initramfs, InitramfsSha256, cancellation))
            throw new InvalidDataException("Bundled Linux guest is damaged. Reinstall Grayjay to restore it.");
        if (UsesBlink)
        {
            var manifest = JsonSerializer.Deserialize<BlinkMetadata>(await File.ReadAllTextAsync(Path.Combine(Directory, "blink-runtime.json"), cancellation),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest is null || manifest.Protocol != RuntimeAssets.ProtocolVersion || manifest.Version != RuntimeAssets.Release ||
                !Hash(manifest.BlinkSha256) ||
                (Path.GetFileName(Executable) == "blink.exe" &&
                    (!await VerifiedDownload.MatchesAsync(Executable, manifest.BlinkSha256, cancellation) ||
                     !Hash(manifest.CygwinSha256) ||
                     !await VerifiedDownload.MatchesAsync(Path.Combine(Directory, "cygwin1.dll"), manifest.CygwinSha256!, cancellation))))
                throw new InvalidDataException("Bundled CDM runner is damaged or incompatible. Reinstall Grayjay to restore it.");
        }
    }
    private static bool Hash(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
    private sealed record Metadata(string Version, int Protocol, string KernelSha256, string InitramfsSha256);
    private sealed record BlinkMetadata(string Version, int Protocol, string BlinkSha256, string? CygwinSha256);
}
