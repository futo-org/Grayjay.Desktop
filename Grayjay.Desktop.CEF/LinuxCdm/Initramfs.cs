using System.IO.Compression;
using System.Text;
using System.Security.Cryptography;

namespace Grayjay.Desktop.CEF.LinuxCdm;

internal static class Initramfs
{

    internal static async Task BuildAsync(string baseImage, string cdm, string output, CancellationToken cancellation)
    {
        string receipt = output + ".sha256";
        if (File.Exists(output) && File.Exists(receipt) &&
            await VerifiedDownload.MatchesAsync(output, await File.ReadAllTextAsync(receipt, cancellation), cancellation)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string temporary = output + ".partial-" + Guid.NewGuid().ToString("N");
        string temporaryReceipt = temporary + ".sha256";
        try
        {
            await using (var destination = File.Create(temporary))
            {
                await using (var source = File.OpenRead(baseImage)) await source.CopyToAsync(destination, cancellation);

                await using var gzip = new GZipStream(destination, CompressionLevel.Fastest, leaveOpen: true);
                await EntryAsync(gzip, "opt", 0x41ED, null, cancellation);
                await EntryAsync(gzip, "opt/widevine", 0x41ED, null, cancellation);
                await EntryAsync(gzip, "opt/widevine/libwidevinecdm.so", 0x81A4, cdm, cancellation);
                await EntryAsync(gzip, "TRAILER!!!", 0, null, cancellation);
            }
            File.Move(temporary, output, true);
            await using var installed = File.OpenRead(output);
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(installed, cancellation));
            await File.WriteAllTextAsync(temporaryReceipt, hash, cancellation);
            File.Move(temporaryReceipt, receipt, true);
        }
        finally { File.Delete(temporary); File.Delete(temporaryReceipt); }
    }
    private static async Task EntryAsync(Stream output, string name, uint mode, string? file, CancellationToken cancellation)
    {
        byte[] filename = Encoding.UTF8.GetBytes(name + "\0");
        uint length = file is null ? 0 : checked((uint)new FileInfo(file).Length);
        uint[] fields = [1, mode, 0, 0, 1, 0, length, 0, 0, 0, 0, (uint)filename.Length, 0];
        byte[] header = Encoding.ASCII.GetBytes("070701" + string.Concat(fields.Select(x => x.ToString("x8"))));
        await output.WriteAsync(header, cancellation); await output.WriteAsync(filename, cancellation);
        await PadAsync(output, header.Length + filename.Length, cancellation);
        if (file is not null) { await using var input = File.OpenRead(file); await input.CopyToAsync(output, cancellation); }
        await PadAsync(output, length, cancellation);
    }
    private static Task PadAsync(Stream output, long length, CancellationToken cancellation)
        => output.WriteAsync(new byte[(int)((4 - length % 4) % 4)], cancellation).AsTask();
}
