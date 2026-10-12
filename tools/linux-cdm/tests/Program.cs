using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Grayjay.Desktop.CEF.LinuxCdm;

if (args.Length >= 2 && args[0] == "--blink-reference")
{
    await BlinkPlaybackTests.RunAsync(Path.GetFullPath(args[1]), args.Length > 2 ? int.Parse(args[2]) : 4, args.Length > 3 ? args[3] : null);
    return;
}
if (args.Contains("--job-owner")) { await ProcessJobTests.RunOwnerAsync(); return; }
if (args.Contains("--native-child")) { await WindowsPlaybackProcessTests.ChildAsync(args); return; }
await WindowsPlaybackProcessTests.RunAsync();
await ProcessJobTests.RunAsync();

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS " + name);
}
static async Task Reject(Func<Task> action, string name)
{
    try { await action(); }
    catch (InvalidDataException) { Console.WriteLine("PASS " + name); return; }
    throw new Exception(name);
}
var root = Path.Combine(Path.GetTempPath(), "grayjay-download-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
using var probe = new TcpListener(IPAddress.Loopback, 0);
probe.Start(); int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
using var listener = new HttpListener(); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
byte[] data = [];
int requests = 0;
var server = Task.Run(async () => {
    try {
        while (true) {
            var context = await listener.GetContextAsync(); Interlocked.Increment(ref requests);
            byte[] body = data; context.Response.ContentLength64 = body.Length;
            try { await context.Response.OutputStream.WriteAsync(body); }
            catch (HttpListenerException) { }
            finally { context.Response.Close(); }
        }
    } catch (Exception e) when (e is HttpListenerException or ObjectDisposedException) { }
});
DownloadAsset Asset(byte[] bytes) => new($"http://127.0.0.1:{port}/component.zip", Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
byte[] Archive(string name, string contents)
{
    using var stream = new MemoryStream();
    using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true)) {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open()); writer.Write(contents);
    }
    return stream.ToArray();
}
try
{
    await MacBlinkProcessTests.RunAsync(root);
    await CdmRootTests.RunAsync(root);
    await PlaybackTests.RunAsync();
    await DownloadUpdateTests.RunAsync(root);
    void Fixture(string app, bool mac)
    {
        string root = mac ? Path.Combine(app, "..", "Resources", "playback") : Path.Combine(app, "playback");
        string runtime = mac ? root : Path.Combine(root, "runtime");
        string executable = mac ? Path.Combine(app, "..", "Helpers", "blink") : Path.Combine(runtime, "blink.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!); File.WriteAllText(executable, "signed executable");
        if (!mac)
        {
            File.WriteAllText(Path.Combine(runtime, "cygwin1.dll"), "runtime");
            File.WriteAllText(Path.Combine(runtime, "blink-runtime.json"), JsonSerializer.Serialize(new {
                version="1", protocol=4, blinkSha256=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("signed executable"))),
                cygwinSha256=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("runtime"))) }));
        }
        if (mac)
        {
            Directory.CreateDirectory(runtime);
            File.WriteAllText(Path.Combine(runtime, "blink-runtime.json"), JsonSerializer.Serialize(new {
                version="1", protocol=4, blinkSha256=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("signed executable"))) }));
        }
        Directory.CreateDirectory(Path.Combine(runtime, "data")); File.WriteAllText(Path.Combine(runtime, "data", "bios-256k.bin"), "firmware");
        string guest = Path.Combine(root, "guest"); Directory.CreateDirectory(guest);
        File.WriteAllBytes(Path.Combine(guest, "vmlinuz"), [1,2,3]); File.WriteAllBytes(Path.Combine(guest, "initramfs.cpio.gz"), [4,5,6]);
        File.WriteAllText(Path.Combine(guest, "bundle.json"), JsonSerializer.Serialize(new {
            version="1",protocol=4,kernelSha256=Convert.ToHexString(SHA256.HashData(new byte[]{1,2,3})),
            initramfsSha256=Convert.ToHexString(SHA256.HashData(new byte[]{4,5,6})) }));
    }
    string windowsApp = Path.Combine(root, "windows-app"); Fixture(windowsApp, false);
    var winBundle = BundledRuntime.Resolve(windowsApp, true);
    await winBundle.VerifyGuestAsync(default);
    Check(winBundle.Executable.EndsWith(".exe") && winBundle.Directory.EndsWith("runtime") && requests == 0,
        "Windows bundled runtime resolves without network");
    string macApp = Path.Combine(root, "Grayjay.app", "Contents", "MacOS"); Fixture(macApp, true);
    var macBundle = BundledRuntime.Resolve(macApp, false);
    await macBundle.VerifyGuestAsync(default);
    Check(macBundle.Executable.Contains("Helpers") && macBundle.Directory.Contains("Resources") && requests == 0,
        "Mac bundled code and resources resolve without network");
    await File.WriteAllTextAsync(macBundle.Executable, "release signing changes executable bytes");
    Check(BundledRuntime.Available(macApp, false), "release code signing does not invalidate guest metadata");
    await File.WriteAllBytesAsync(macBundle.Initramfs, [9]);
    await Reject(() => macBundle.VerifyGuestAsync(default), "damaged bundled guest rejected");
    Check(!BundledRuntime.Available(Path.Combine(root, "missing-app"), true), "missing bundled runtime reported unavailable");
    string metadataPath = Path.Combine(Path.GetDirectoryName(winBundle.Kernel)!, "bundle.json");
    string metadata = await File.ReadAllTextAsync(metadataPath);
    await File.WriteAllTextAsync(metadataPath, metadata.Replace("\"protocol\":4", "\"protocol\":999"));
    await Reject(() => { BundledRuntime.Resolve(windowsApp, true); return Task.CompletedTask; }, "incompatible bundled guest rejected");
    data = Archive("qemu-system-x86_64", "runtime-v1");
    var asset = Asset(data); string download = Path.Combine(root, "download.zip");
    var progress = new List<DownloadProgress>();
    Task Report(DownloadProgress value) { progress.Add(value); return Task.CompletedTask; }
    await VerifiedDownload.FetchAsync(asset, download, "runtime", Report, default);
    Check(await VerifiedDownload.MatchesAsync(download, asset.Sha256, default), "verified download");
    Check(progress.Any(p => p.Received == 0 && p.Total == data.Length) && progress.Any(p => p.Received == data.Length), "download progress");
    int before = requests;
    await VerifiedDownload.FetchAsync(asset, download, "runtime", Report, default);
    Check(requests == before, "cached download avoids network");
    var assets = new RuntimeAssets(root);
    data = Archive("qemu-system-x86_64", "wrong hash");
    await Reject(() => VerifiedDownload.FetchAsync(Asset(data) with { Sha256 = new string('0', 64) }, download, "runtime", Report, default), "download hash mismatch rejected");
    Check(!Directory.EnumerateFiles(root, "*.partial-*", SearchOption.AllDirectories).Any(), "failed download removes partial files");
    using var cancel = new CancellationTokenSource();
    try {
        await VerifiedDownload.FetchAsync(Asset(data), download, "runtime", p => { cancel.Cancel(); return Task.CompletedTask; }, cancel.Token);
        throw new Exception("Cancellation ignored");
    } catch (OperationCanceledException) { Console.WriteLine("PASS download cancellation"); }
    Check(!Directory.EnumerateFiles(root, "*.partial-*", SearchOption.AllDirectories).Any(), "cancelled download removes partial files");
    string baseImage = Path.Combine(root, "base.cpio.gz"), library = Path.Combine(root, "test-cdm.so"), boot = Path.Combine(root, "boot.cpio.gz");
    await File.WriteAllBytesAsync(baseImage, [1, 2, 3]);
    await File.WriteAllBytesAsync(library, [4, 5, 6]);
    await Initramfs.BuildAsync(baseImage, library, boot, default);
    string bootHash = await File.ReadAllTextAsync(boot + ".sha256");
    Check(await VerifiedDownload.MatchesAsync(boot, bootHash, default), "boot image integrity receipt");
    var timestamp = File.GetLastWriteTimeUtc(boot);
    await Initramfs.BuildAsync(baseImage, library, boot, default);
    Check(File.GetLastWriteTimeUtc(boot) == timestamp, "verified boot image reused");
    await File.WriteAllBytesAsync(boot, [9, 9, 9]);
    await Initramfs.BuildAsync(baseImage, library, boot, default);
    Check(await VerifiedDownload.MatchesAsync(boot, await File.ReadAllTextAsync(boot + ".sha256"), default) && new FileInfo(boot).Length > 3, "corrupt boot image rebuilt");
    if (OperatingSystem.IsLinux()) {
        Check(!RuntimeAssets.Supported, "Linux uses existing native playback");
        try { await assets.EnsureAsync(Report, default); throw new Exception("Linux helper enabled"); }
        catch (PlatformNotSupportedException) { Console.WriteLine("PASS Linux installation guard"); }
    }
    if (args.Length == 1) {
        byte[] crx = await File.ReadAllBytesAsync(args[0]);
        Check(Crx3.VerifyWidevine(crx).Span[..2].SequenceEqual("PK"u8), "official Google component signature");
        crx[^1] ^= 1;
        await Reject(() => { Crx3.VerifyWidevine(crx); return Task.CompletedTask; }, "tampered signed component rejected");
    }
}
finally { listener.Stop(); await server; Directory.Delete(root, true); }
