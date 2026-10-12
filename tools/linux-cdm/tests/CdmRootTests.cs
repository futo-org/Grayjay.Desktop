using System.IO.Compression;
using System.Text;
using Grayjay.Desktop.CEF.LinuxCdm;

internal static class CdmRootTests
{
    internal static async Task RunAsync(string temporary)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string image;
        while (!File.Exists(image = Path.Combine(directory.FullName, "Grayjay.ClientServer", "deps", "playback", "guest", "initramfs.cpio.gz")))
            directory = directory.Parent ?? throw new FileNotFoundException("Bundled guest required for CDM root tests.");
        string root = Path.Combine(temporary, "cdm-root"), cdm = Path.Combine(temporary, "test-cdm.so");
        await File.WriteAllTextAsync(cdm, "fixture CDM");
        await CdmRoot.BuildAsync(image, cdm, root, default);
        string host = Path.Combine(root, "usr", "local", "bin", "cdm-host");
        if (!File.Exists(host) || !File.Exists(Path.Combine(root, "lib64", "ld-linux-x86-64.so.2")) || Directory.Exists(Path.Combine(root, "lib", "modules")))
            throw new Exception("CDM extraction did not produce the required minimal runtime.");
        if (!Directory.Exists(Path.Combine(root, "tmp"))) throw new Exception("CDM fragment temporary directory is missing.");
        if (!OperatingSystem.IsWindows())
        {
            if ((File.GetUnixFileMode(host) & UnixFileMode.UserExecute) == 0)
                throw new Exception("CDM host is not executable on macOS.");
            File.SetUnixFileMode(host, File.GetUnixFileMode(host) & ~UnixFileMode.UserExecute);
        }
        Directory.Delete(Path.Combine(root, "tmp"));
        var timestamp = File.GetLastWriteTimeUtc(host);
        await CdmRoot.BuildAsync(image, cdm, root, default);
        if (!Directory.Exists(Path.Combine(root, "tmp"))) throw new Exception("Missing temporary directory was not repaired.");
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(host) & UnixFileMode.UserExecute) == 0)
            throw new Exception("Cached CDM host executable permission was not repaired.");
        if (File.GetLastWriteTimeUtc(host) != timestamp) throw new Exception("Verified CDM root was unnecessarily rebuilt.");
        await File.WriteAllTextAsync(host, "corrupt");
        await CdmRoot.BuildAsync(image, cdm, root, default);
        if (new FileInfo(host).Length < 1000) throw new Exception("Corrupt CDM root was not repaired.");
        Console.WriteLine("PASS minimal CDM extraction, integrity receipt, reuse and repair");

        string malicious = Path.Combine(temporary, "unsafe.cpio.gz");
        byte[] filename = Encoding.UTF8.GetBytes("../escape\0");
        uint[] fields = [1, 0x81A4, 0, 0, 1, 0, 4, 0, 0, 0, 0, (uint)filename.Length, 0];
        byte[] header = Encoding.ASCII.GetBytes("070701" + string.Concat(fields.Select(x => x.ToString("x8"))));
        await using (var file = File.Create(malicious))
        await using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        {
            await gzip.WriteAsync(header); await gzip.WriteAsync(filename);
            await gzip.WriteAsync(new byte[(4 - (header.Length + filename.Length) % 4) % 4]);
            await gzip.WriteAsync("test"u8.ToArray());
        }
        try { await CdmRoot.ExtractAsync(malicious, root, default); throw new Exception("Unsafe archive path accepted."); }
        catch (InvalidDataException) { Console.WriteLine("PASS unsafe CDM archive path rejected"); }
    }
}
