using System.Diagnostics;
using Grayjay.Desktop.CEF.LinuxCdm;

internal static class ProcessJobTests
{
    private static Process StartChild() => Process.Start(new ProcessStartInfo("powershell.exe")
    {
        UseShellExecute = false, CreateNoWindow = true,
        ArgumentList = { "-NoProfile", "-Command", "Start-Sleep -Seconds 300" }
    })!;

    internal static async Task RunOwnerAsync()
    {
        using var child = StartChild();
        using var job = new PlaybackProcessJob(child);
        Console.WriteLine(child.Id);
        await Task.Delay(Timeout.Infinite);
    }

    internal static async Task RunAsync()
    {
        if (!OperatingSystem.IsWindows()) return;
        using (var child = StartChild())
        {
            try
            {
                using (var job = new PlaybackProcessJob(child)) { }
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Console.WriteLine("PASS closing the playback job terminates its helper");
            }
            finally { if (!child.HasExited) child.Kill(entireProcessTree: true); }
        }
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            ArgumentList = { "--roll-forward", "Major", typeof(ProcessJobTests).Assembly.Location, "--job-owner" }
        };
        using var owner = Process.Start(start)!;
        Process? ownedChild = null;
        try
        {
            string line = (await owner.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)))!;
            ownedChild = Process.GetProcessById(int.Parse(line));
            owner.Kill(); // Deliberately skip managed cleanup, as a crash would.
            await owner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await ownedChild.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Console.WriteLine("PASS abrupt owner exit terminates the playback helper");
        }
        finally
        {
            if (!owner.HasExited) owner.Kill(entireProcessTree: true);
            if (ownedChild is { HasExited: false }) ownedChild.Kill(entireProcessTree: true);
            ownedChild?.Dispose();
        }
    }
}
