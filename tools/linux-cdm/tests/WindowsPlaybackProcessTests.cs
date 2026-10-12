using System.Runtime.InteropServices;
using System.Text.Json;
using Grayjay.Desktop.CEF.LinuxCdm;

internal static class WindowsPlaybackProcessTests
{
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint allocation, uint protection);
    internal static async Task ChildAsync(string[] args)
    {
        bool denied = VirtualAlloc(IntPtr.Zero, (UIntPtr)(128 * 1024 * 1024), 0x3000, 4) == IntPtr.Zero;
        string? line = await Console.In.ReadLineAsync();
        Console.WriteLine(JsonSerializer.Serialize(new { denied, line, args }));
    }
    internal static async Task RunAsync()
    {
        if (!OperatingSystem.IsWindows()) return;
        string executable = Environment.ProcessPath!;
        string[] unusual = ["space here", "quote\"here", "trailing\\", "ünicode"];
        string[] arguments = ["--roll-forward", "Major", typeof(WindowsPlaybackProcessTests).Assembly.Location, "--native-child", .. unusual];
        // dotnet run uses the apphost; invoke dotnet explicitly for the child.
        if (!Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            executable = Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe");
        using var child = WindowsPlaybackProcess.Start(Path.GetFullPath(executable), Path.GetTempPath(), arguments, 64UL * 1024 * 1024);
        await child.Input.WriteLineAsync("pipe check"); await child.Input.FlushAsync();
        string line = (await child.Output.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)))!;
        using var response = JsonDocument.Parse(line);
        if (!response.RootElement.GetProperty("denied").GetBoolean() || response.RootElement.GetProperty("line").GetString() != "pipe check")
            throw new Exception("Native helper memory limit or pipe transport failed.");
        var received = response.RootElement.GetProperty("args").EnumerateArray().Select(x => x.GetString()).ToArray();
        if (!received.Skip(1).SequenceEqual(unusual)) throw new Exception("Native helper argument quoting failed.");
        await child.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Console.WriteLine("PASS native suspended launch, pipe transport, argument quoting and optional memory ceiling");
    }
}
