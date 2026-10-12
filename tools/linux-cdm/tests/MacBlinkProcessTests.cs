using System.Diagnostics;
using Grayjay.Desktop.CEF.LinuxCdm;

internal static class MacBlinkProcessTests
{
    internal static async Task RunAsync(string temporary)
    {
        if (!OperatingSystem.IsMacOS()) return;
        string directory = Path.Combine(temporary, "Mac runner with spaces");
        Directory.CreateDirectory(directory);
        string executable = Path.Combine(directory, "blink");
        await File.WriteAllTextAsync(executable, """
            #!/bin/sh
            echo $$ > child.pid
            echo '{"event":"initialized","protocol":4,"success":true}'
            while IFS= read -r line; do
                case "$line" in
                    QUIT) exit 0 ;;
                    REQUEST*) echo '{"event":"resolved","id":1}' ;;
                esac
            done
            """ + "\n");
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var runtime = new PlayerRuntime(directory, executable, "", "") { GuestRoot = directory };
        int pid;
        await using (var guest = new BlinkGuest(runtime, Path.Combine(directory, "logs"), _ => Task.CompletedTask))
        {
            await guest.StartAsync("tcg", default);
            pid = int.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "child.pid")));
            if (!guest.IsRunning) throw new Exception("Mac Blink failed to initialize.");
            var response = await guest.RequestAsync("REQUEST", "test", default);
            if (response.GetProperty("event").GetString() != "resolved") throw new Exception("Mac Blink pipe response was lost.");
        }
        CheckExited(pid);
        Console.WriteLine("PASS macOS Blink pipes, paths with spaces and graceful shutdown");

        await File.WriteAllTextAsync(executable, "#!/bin/sh\necho $$ > child.pid\nwhile IFS= read -r line; do :; done\n");
        await using (var guest = new BlinkGuest(runtime, Path.Combine(directory, "cancel-logs"), _ => Task.CompletedTask))
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            try { await guest.StartAsync("tcg", cancellation.Token); throw new Exception("Mac Blink startup ignored cancellation."); }
            catch (OperationCanceledException) { }
            pid = int.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "child.pid")));
        }
        CheckExited(pid);
        Console.WriteLine("PASS macOS Blink cancelled startup kills an unresponsive helper");
    }

    private static void CheckExited(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited) throw new Exception("Mac Blink helper survived disposal.");
        }
        catch (ArgumentException) { }
    }
}
