using System.Runtime.InteropServices;

namespace Grayjay.Desktop.CEF.LinuxCdm;

internal sealed class RuntimeAssets : IAsyncDisposable
{
    internal const int ProtocolVersion = 4;
    internal const string Release = "1";
    private readonly string cache;
    private readonly string applicationDirectory;
    private readonly SemaphoreSlim prepare = new(1, 1);
    private readonly WidevineDownloader widevine;
    internal RuntimeAssets(string cache, string? applicationDirectory = null)
    {
        this.cache = Path.GetFullPath(cache);
        this.applicationDirectory = applicationDirectory ?? AppContext.BaseDirectory;
        widevine = new(Path.Combine(this.cache, "widevine"));
    }
    internal static bool Supported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
    internal bool Ready => Supported && BundledRuntime.Available(applicationDirectory, OperatingSystem.IsWindows()) && widevine.Cached() is not null;

    internal async Task<PlayerRuntime> EnsureAsync(Func<DownloadProgress, Task> progress, CancellationToken cancellation)
    {
        if (!Supported) throw new PlatformNotSupportedException("The Linux playback helper is used only on Windows and macOS.");
        if (RuntimeInformation.ProcessArchitecture is not Architecture.X64 and not Architecture.Arm64)
            throw new PlatformNotSupportedException("Protected playback requires a 64-bit Grayjay installation.");
        await prepare.WaitAsync(cancellation);
        try
        {
            var bundle = BundledRuntime.Resolve(applicationDirectory, OperatingSystem.IsWindows());
            await bundle.VerifyGuestAsync(cancellation);
            var cdm = await widevine.EnsureAsync(progress, cancellation);
            await progress(new("prepare", "Preparing protected playback"));
            if (bundle.UsesBlink)
            {
                string root = Path.Combine(cache, "roots", bundle.InitramfsSha256 + "-" + cdm.Sha256);
                await CdmRoot.BuildAsync(bundle.Initramfs, cdm.Path, root, cancellation);
                await progress(new("ready", "Playback components are ready"));
                return new(bundle.Directory, bundle.Executable, "", "") { GuestRoot = root };
            }
            string boot = Path.Combine(cache, "boot", bundle.InitramfsSha256 + "-" + cdm.Sha256 + ".cpio.gz");
            await Initramfs.BuildAsync(bundle.Initramfs, cdm.Path, boot, cancellation);
            await progress(new("ready", "Playback components are ready"));
            return new(bundle.Directory, bundle.Executable, bundle.Kernel, boot);
        }
        finally { prepare.Release(); }
    }
    public ValueTask DisposeAsync() => widevine.DisposeAsync();
}
