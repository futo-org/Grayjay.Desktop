using JustCef;
using Grayjay.ClientServer;
using Grayjay.ClientServer.Constants;
using Grayjay.ClientServer.Controllers;
using Grayjay.ClientServer.Settings;
using Grayjay.ClientServer.States;
using Grayjay.Desktop.CEF;
using Grayjay.Engine.Packages;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

using Logger = Grayjay.Desktop.POC.Logger;
using LogLevel = Grayjay.Desktop.POC.LogLevel;

namespace Grayjay.Desktop
{
    internal class Program
    {
        private static string? StartingUpFile = null;
        private const string StartingUpFileName = "starting";
        private static string? PortFile = null;
        private const string PortFileName = "port";   
        private const int StartupTimeoutSeconds = 5;
        private const int NewWindowTimeoutSeconds = 5;

        private static bool IsProcessRunningByPath(string path, out Process? matchingProcess)
        {
            matchingProcess = null;
            int currentProcessId = Process.GetCurrentProcess().Id;
            string processName = Path.GetFileNameWithoutExtension(path);

            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    if (process.Id != currentProcessId &&
                        process.MainModule?.FileName == path)
                    {
                        matchingProcess = process;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Verbose(nameof(Program), $"Error checking process ID={process.Id}", ex);
                }
            }
            return false;
        }

        private static async Task<bool> WaitForPortFileAndProcess()
        {
            Stopwatch sw = Stopwatch.StartNew();
            string currentProcessPath = Process.GetCurrentProcess().MainModule!.FileName;
            int waitedSeconds = 0;

            while (waitedSeconds < StartupTimeoutSeconds)
            {
                if (File.Exists(PortFile!))
                    return true;

                if (!IsProcessRunningByPath(currentProcessPath, out _))
                    return false;

                await Task.Delay(1000);
                waitedSeconds++;
            }

            Logger.i(nameof(Program), $"WaitForPortFileAndProcess duration {sw.ElapsedMilliseconds}ms");
            return false;
        }

        private static void KillExistingProcessByPath()
        {
            Stopwatch sw = Stopwatch.StartNew();
            string currentProcessPath = Process.GetCurrentProcess().MainModule!.FileName;
            int currentProcessId = Process.GetCurrentProcess().Id;

            string processName = Path.GetFileNameWithoutExtension(currentProcessPath);
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    if (process.Id != currentProcessId && process.MainModule?.FileName == currentProcessPath)
                    {
                        process.Kill();
                        process.WaitForExit(1000);
                    }
                }
                catch
                {
                    // Ignore processes that may throw due to access issues
                }
            }

            Logger.i(nameof(Program), $"KillExistingProcessByPath duration {sw.ElapsedMilliseconds}ms");
        }

        private static string? FindSystemWidevineCdm()
        {
            if (!OperatingSystem.IsLinux())
                return null;

            string[] candidates =
            {
                "/opt/google/chrome/WidevineCdm",
                "/opt/google/chrome-beta/WidevineCdm",
                "/opt/google/chrome-unstable/WidevineCdm",
                "/opt/microsoft/msedge/WidevineCdm",
                "/usr/lib/chromium/WidevineCdm",
                "/usr/lib64/chromium/WidevineCdm"
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(Path.Combine(candidate, "manifest.json")))
                    return candidate;
            }

            return null;
        }

        private static async Task<JustCefProcess> StartCefProcessAsync(string startArgs)
        {
            const int maxAttempts = 3;

            for (int attempt = 1; ; attempt++)
            {
                var cef = new JustCefProcess();
                try
                {
                    cef.Start(startArgs);
                    await cef.WaitForReadyAsync();
                    return cef;
                }
                catch (JustCefStartupException e) when (e.Failure != JustCefStartupFailure.Unknown && attempt < maxAttempts)
                {
                    Logger.w(nameof(Program), $"JustCef startup attempt {attempt} failed ({e.Failure}), retrying.", e);
                    cef.Dispose();
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt));
                }
                catch
                {
                    cef.Dispose();
                    throw;
                }
            }
        }

        private static async Task MonitorWidevineAsync(JustCefProcess cef)
        {
            try
            {
                bool everRegistered = false;

                for (int i = 0; i < 12; i++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5));

                    var status = await cef.GetWidevineStatusAsync();
                    everRegistered |= status.Registered;

                    if (!status.Installed)
                        continue;

                    if (!status.RequiresRestart)
                    {
                        Logger.i(nameof(Program), $"Widevine CDM {status.Version} is active.");
                        return;
                    }

                    Logger.i(nameof(Program), $"Widevine CDM {status.Version} was installed and takes effect after a restart.");

                    await StateWindow.WaitForReadyAsync();
                    StateUI.Toast("Protected playback", "Restart Grayjay to finish enabling playback of protected content.");
                    return;
                }

                if (everRegistered)
                    Logger.i(nameof(Program), "The Widevine CDM did not install, protected content will not play.");
                else
                    Logger.i(nameof(Program), "Widevine is unavailable on this platform, protected content will not play.");
            }
            catch (Exception e)
            {
                Logger.w(nameof(Program), "Failed to query the Widevine status.", e);
            }
        }

        private static async Task<bool> TryOpenWindow()
        {
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                string currentProcessPath = Process.GetCurrentProcess().MainModule!.FileName;
                if (!IsProcessRunningByPath(currentProcessPath, out _))
                {
                    Logger.i(nameof(Program), "Process not running, skipping HTTP request");
                    return false;
                }
                Logger.i(nameof(Program), "Process running, proceeding with HTTP request");

                if (!File.Exists(PortFile!))
                {
                    Logger.i(nameof(Program), "PortFile missing, skipping HTTP request");
                    return false;
                }

                string port = File.ReadAllText(PortFile!);
                if (string.IsNullOrWhiteSpace(port))
                {
                    Logger.i(nameof(Program), "PortFile empty or invalid, skipping HTTP request");
                    return false;
                }

                var url = $"http://127.0.0.1:{port}/Window/StartWindow";
                Logger.i(nameof(Program), $"TryOpenWindow: " + url);

                using HttpClient client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
                var response = await client.GetAsync(url);

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Logger.i(nameof(Program), $"TryOpenWindow failed", ex);
                return false;
            }
            finally
            {
                Logger.i(nameof(Program), $"TryOpenWindow duration {sw.ElapsedMilliseconds}ms");
            }
        }

        public static string ReconstructArgs(string[] args)
        {
            if (args == null || args.Length == 0)
                return string.Empty;

            var builder = new StringBuilder();

            foreach (var arg in args)
            {
                bool isHeadless = arg == "--headless";
                bool isServer = arg == "--server";
                bool isFullscreen = arg == "--fullscreen";
                bool isScaleFactor = arg.StartsWith("--scale-factor=");
                bool isInputSource = arg.StartsWith("--input-source=");

                if (isHeadless || isServer || isFullscreen || isScaleFactor || isInputSource)
                    continue;

                if (builder.Length > 0)
                    builder.Append(' ');

                builder.Append(EscapeArgument(arg));
            }

            return builder.ToString();
        }

        private static string EscapeArgument(string arg)
        {
            if (string.IsNullOrEmpty(arg))
                return "\"\"";

            bool needsQuotes = arg.Contains(' ') || arg.Contains('\t') || arg.Contains('"') || arg.Contains('\\');

            if (!needsQuotes)
                return arg;

            var escapedArg = new StringBuilder();
            escapedArg.Append('"');

            for (int i = 0; i < arg.Length; i++)
            {
                if (arg[i] == '\\')
                {
                    int backslashCount = 0;
                    while (i < arg.Length && arg[i] == '\\')
                    {
                        backslashCount++;
                        i++;
                    }

                    if (i < arg.Length && arg[i] == '"')
                    {
                        escapedArg.Append(new string('\\', backslashCount * 2));
                        escapedArg.Append('\\');
                    }
                    else
                    {
                        escapedArg.Append(new string('\\', backslashCount));
                    }

                    if (i < arg.Length && arg[i] != '"')
                        i--;
                }
                else if (arg[i] == '"')
                {
                    escapedArg.Append("\\\"");
                }
                else
                {
                    escapedArg.Append(arg[i]);
                }
            }

            escapedArg.Append('"');
            return escapedArg.ToString();
        }

        static async Task Main(string[] args)
        {
            try
            {
                await EntryPoint(args);
            }
            catch (Exception e)
            {
                Logger.e<Program>($"Unhandled exception occurred: {e}");
            }
        }

        static async Task EntryPoint(string[] args)
        {
            Stopwatch sw = Stopwatch.StartNew();

            if (args.Length > 0 && args[0] == "version")
            {
                Console.WriteLine(App.Version.ToString());
                return;
            }

            bool isHeadless = args?.Contains("--headless") ?? false;
            bool isServer = args?.Contains("--server") ?? false;
            bool disableSecurity = args?.Contains("--ignore-security") ?? false;
            bool isFullscreen = args?.Contains("--fullscreen") ?? false;
            double? scaleFactor = args?.FirstOrDefault(a => a.StartsWith("--scale-factor=")) is string s && double.TryParse(s["--scale-factor=".Length..], out var v) ? v : null;
            StateApp.InputSource = args?.FirstOrDefault(a => a.StartsWith("--input-source="))?["--input-source=".Length..];

#if DEBUG
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                WindowsAPI.AllocConsole();
#endif
            if (isHeadless || isServer)
            {
                if (disableSecurity)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("You disabled security (using --ignore-security, this may expose your Grayjay instance to remote invocation");
                    Debug.WriteLine("You disabled security (using --ignore-security, this may expose your Grayjay instance to remote invocation");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Headless and Server mode have temporarily been disabled due to security reasons");
                    Console.WriteLine("If you would like to ignore this warning, you can choose to start Grayjay with the --ignore-security parameter");
                    Debug.WriteLine("Headless and Server mode have temporarily been disabled due to security reasons");
                    Debug.WriteLine("If you would like to ignore this warning, you can choose to start Grayjay with the --ignore-security parameter");
                    Console.ResetColor();
                    Console.ReadLine();
                    return;
                }
            }

            Console.SetOut(new SuppressingTextWriter(Console.Out));
            Console.SetError(new SuppressingTextWriter(Console.Error));

            Console.WriteLine(Logger.FormatLogMessage(LogLevel.Info, nameof(Program), $"AppContext.BaseDirectory: {AppContext.BaseDirectory}"));
            Console.WriteLine(Logger.FormatLogMessage(LogLevel.Info, nameof(Program), $"Base Directory: {Directories.Base}"));
            Console.WriteLine(Logger.FormatLogMessage(LogLevel.Info, nameof(Program), $"Temporary Directory: {Directories.Temporary}"));
            Console.WriteLine(Logger.FormatLogMessage(LogLevel.Info, nameof(Program), $"Log Level: {(LogLevel)GrayjaySettings.Instance.Logging.LogLevel}"));
            Console.WriteLine(Logger.FormatLogMessage(LogLevel.Info, nameof(Program), $"Log file path: {Path.Combine(Directories.Base, "log.txt")}"));
            Logger.LoadFromSettings();

            FUTO.MDNS.Logger.LogCallback = (level, tag, message, ex) => Logger.Log((LogLevel)level, tag, message, ex);
            FUTO.MDNS.Logger.WillLog = (level) => Logger.WillLog((LogLevel)level);
            Engine.Logger.LogCallback = (level, tag, message, ex) => Logger.Log((LogLevel)level, tag, message, ex);
            Engine.Logger.WillLog = (level) => Logger.WillLog((LogLevel)level);
            JustCef.Logger.LogCallback = (level, tag, message, ex) => Logger.Log((LogLevel)level, tag, message, ex);
            JustCef.Logger.WillLog = (level) => Logger.WillLog((LogLevel)level);
            SyncShared.Logger.WillLog = (level) => Logger.WillLog((LogLevel)level);
            SyncShared.Logger.LogCallback = (level, tag, message, ex) => Logger.Log((LogLevel)level, tag, message, ex);

            GrayjayDevSettings.Instance.DeveloperMode = File.Exists(Path.Combine(Directories.Base, "DEV"));

            foreach(var arg in args)
                Console.WriteLine(Logger.FormatLogMessage(LogLevel.Info, nameof(Program), "Arg: " + arg));

            Updater.SetStartupArguments(string.Join(" ", args.Select(x => (x.Contains(" ") ? $"\"{x}\"" : x))));

            Logger.i<Program>($"Initialize {sw.ElapsedMilliseconds}ms");
            sw.Restart();

            PortFile = Path.Combine(Directories.Base, PortFileName);
            Logger.i<Program>($"PortFile path: {PortFile}");
            StartingUpFile = Path.Combine(Directories.Base, StartingUpFileName);
            Logger.i<Program>($"StartingUpFile path: {StartingUpFile}");

            if (File.Exists(StartingUpFile))
            {
                Logger.i<Program>("Found StartingUpFile, waiting for PortFile and process");

                if (await WaitForPortFileAndProcess())
                {
                    if (await TryOpenWindow())
                    {
                        Logger.i<Program>("Successfully opened new window, closing current process.");
                        return;
                    }
                    else
                    {
                        Logger.i<Program>("Failed to open window, killing any lingering (stuck) process");
                        KillExistingProcessByPath();
                    }
                }
                else
                {
                    Logger.i<Program>("No PortFile after waiting, killing any lingering (stuck) process");
                    KillExistingProcessByPath();
                }
            }

            Logger.i<Program>($"Check StartingUpFile {sw.ElapsedMilliseconds}ms");

            if (File.Exists(PortFile))
            {
                if (await TryOpenWindow())
                {
                    Logger.i<Program>("Successfully opened new window, closing current process.");
                    return;
                }
                else
                {
                    Logger.i<Program>("Failed to open window, killing any lingering (stuck) process");
                    KillExistingProcessByPath();
                }
            }

            Logger.i<Program>("Created StartingUpFile, removed PortFile");
            File.Delete(PortFile);
            File.WriteAllText(StartingUpFile, "");

            if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process p = Process.GetCurrentProcess();
                try
                {
                    File.WriteAllText("launch", Path.GetFileName(p.MainModule!.FileName));
                }
                catch(Exception ex)
                {
                    Logger.w(nameof(Program), "Failed to create launch file in executable directory");
                }
                if (Directory.Exists("cef"))
                {
                    try
                    {
                        File.WriteAllText("cef/launch", "../" + Path.GetFileName(p.MainModule!.FileName));
                    }
                    catch(Exception ex)
                    {
                        Logger.w(nameof(Program), "Failed to create launch file in cef directory");
                    }
                }
            }

            string cefStartArgs = "";
            if (!isServer)
            {
                var extraArgs = ReconstructArgs(args);
                Logger.i(nameof(Program), "Extra args: " + extraArgs);

                string rootCachePath = Path.Combine(Directories.Base, "cef_cache");
                string rootCacheDirCmd = "--root-cache-path=\"" + rootCachePath + "\" ";
                Logger.i(nameof(Program), "Root cache path: " + rootCachePath);

                string? systemCdmPath = FindSystemWidevineCdm();
                if (systemCdmPath != null)
                {
                    Logger.i(nameof(Program), "Found a system Widevine CDM at " + systemCdmPath);
                    rootCacheDirCmd += "--widevine-cdm-path=\"" + systemCdmPath + "\" ";
                }

                if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
                    cefStartArgs = "--use-alloy-style --use-native " + rootCacheDirCmd + extraArgs;
                else if (Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") != null)
                    cefStartArgs = "--no-sandbox " + rootCacheDirCmd + extraArgs;
                else
                    cefStartArgs = "--use-alloy-style --use-native --no-sandbox " + rootCacheDirCmd + extraArgs;

                Logger.i(nameof(Program), "Main: Starting JustCefProcess");
            }

            Stopwatch startCefWatch = Stopwatch.StartNew();
            using var cef = !isServer ? await StartCefProcessAsync(cefStartArgs) : null;
            if (cef != null)
            {
                PackageBrowser.Process = cef;
                Logger.i(nameof(Program), $"Main: Starting JustCefProcess finished ({startCefWatch.ElapsedMilliseconds}ms)");

                _ = MonitorWidevineAsync(cef);
            }
            GrayjayServer server = null;
            JustCefWindow ? window = null;

            if (cef != null && !isHeadless && !isServer)
            {
                Stopwatch startWindowWatch = Stopwatch.StartNew();
                Logger.i(nameof(Program), "Main: Starting window.");
                window = await cef.CreateWindowAsync(
                    url: """data:text/html;base64,PHN0eWxlPkBpbXBvcnQgdXJsKGh0dHBzOi8vZm9udHMuZ29vZ2xlYXBpcy5jb20vY3NzMj9mYW1pbHk9Um9ib3RvOndnaHRANDAwOzcwMCZkaXNwbGF5PXN3YXApO2JvZHl7YmFja2dyb3VuZC1jb2xvcjojMWIxYjFiO21hcmdpbjowO2ZvbnQtZmFtaWx5OlJvYm90byxzYW5zLXNlcmlmfS5sb2FkZXItY29udGFpbmVye2Rpc3BsYXk6ZmxleDtqdXN0aWZ5LWNvbnRlbnQ6Y2VudGVyO2ZsZXgtZGlyZWN0aW9uOmNvbHVtbjthbGlnbi1pdGVtczpjZW50ZXI7d2lkdGg6MTAwJTtoZWlnaHQ6MTAwdmh9LmxvYWRlci1zdmd7d2lkdGg6MjAwcHg7aGVpZ2h0OjIwMHB4O2FuaW1hdGlvbjpnZW50bGUtcHVsc2UgM3MgZWFzZS1pbi1vdXQgaW5maW5pdGV9LmxvYWRpbmctdGV4dHtjb2xvcjojZmZmO2ZvbnQtc2l6ZToyNHB4O21hcmdpbi10b3A6MjBweDtmb250LXdlaWdodDo3MDA7bGV0dGVyLXNwYWNpbmc6MXB4fS5sb2FkaW5nLXRleHQgc3BhbntvcGFjaXR5OjA7YW5pbWF0aW9uOmRvdCAxLjVzIGluZmluaXRlfS5sb2FkaW5nLXRleHQgc3BhbjpudGgtY2hpbGQoMSl7YW5pbWF0aW9uLWRlbGF5OjBzfS5sb2FkaW5nLXRleHQgc3BhbjpudGgtY2hpbGQoMil7YW5pbWF0aW9uLWRlbGF5Oi41c30ubG9hZGluZy10ZXh0IHNwYW46bnRoLWNoaWxkKDMpe2FuaW1hdGlvbi1kZWxheToxc31Aa2V5ZnJhbWVzIGdlbnRsZS1wdWxzZXswJSwxMDAle3RyYW5zZm9ybTpzY2FsZSgxKTtvcGFjaXR5Oi44fTUwJXt0cmFuc2Zvcm06c2NhbGUoMS4xKTtvcGFjaXR5OjF9fUBrZXlmcmFtZXMgZG90ezAle29wYWNpdHk6MH01MCV7b3BhY2l0eToxfTEwMCV7b3BhY2l0eTowfX08L3N0eWxlPjxkaXYgY2xhc3M9bG9hZGVyLWNvbnRhaW5lcj48c3ZnIGNsYXNzPWxvYWRlci1zdmcgZmlsbD1ub25lIGhlaWdodD0yMDAgdmlld0JveD0iMCAwIDQ4IDQ4IndpZHRoPTIwMCB4bWxucz1odHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2Zz48cGF0aCBkPSJNMjMuODYxMiA0MS4yNTE2TDQ2LjIyMjUgNy4wMDIySDEuNUwyMy44NjEyIDQxLjI1MTZaImZpbGw9dXJsKCNwYWludDBfbGluZWFyXzgyOF85NTA2KSAvPjxwYXRoIGQ9Ik02LjgxMjUgMzAuODcxNUM3LjcyMzgxIDI5Ljg5OTQgOS45ODM4OSAyNy42NzU4IDExLjczMzYgMjYuNTU3OUMxMy40ODMzIDI1LjQ0MDEgMTUuNTgxNCAyMi4yNDQ0IDE2LjQxMTcgMjAuNzg2M0MxOC45NDMxIDE3LjA2IDI0LjYxMzUgOS4yNzk0NSAyNy4wNDM3IDcuOTY3MTNDMjcuNDgxMSA3LjYyNjg5IDI4LjExNyA3LjA5NjMyIDI4LjM4MDMgNi44NzM1OEMyOS4yOTE2IDQuODQ4NCAzMi4zOTAxIDEuNDA1NjYgMzcuNDkzNCAzLjgzNTg2QzM3LjkzMDkgMy42OTAwMyAzOS4wMTIzIDMuNjUzNTggMzkuNDk4MyAzLjY1MzU4QzM5LjExMzUgMy45MTY4NiAzOC4zMDc1IDQuNjg2NCAzOC4xNjE3IDUuNjU4NDhDMzcuNzcyOSA4LjMzMTY0IDM2LjI5ODYgOS44OTEwMyAzNS42MSAxMC4zMzY1QzM1LjEyNCAxMy40MzUgMzQuNDU1NyAxNi4xNjkgMzIuOTk3NiAxNy4xNDFMMzQuMzM0MiAxOS41MTA0QzM2LjMzOTEgMjEuNjM2OSA0MC40OTQ3IDI2LjIyOTkgNDEuMDc3NyAyNy41OTA4QzM5LjEzMzggMjYuOTU4OSAzNy44NzgyIDI2LjM1NTQgMzcuNDkzNCAyNi4xMzI3TDQxLjA3NzcgMzEuMDUzOEMzOC45NzE4IDMwLjg5MTggMzQuMjM3IDI5LjUyMjggMzIuMTQ3MSAyNS4zNDI5QzMyLjk3MzMgMjcuNTc4NiAzMy43ODc0IDMwLjM2NTIgMzQuMDkxMiAzMS40NzlDMzIuOTU3MSAzMC41NDc1IDMwLjUxODggMjcuODQ1OSAyOS44Mzg0IDI0LjQ5MjNDMzAuMDMyOCAyNy43NDg3IDMwLjAwMDQgMzAuMDYxNCAyOS45NTk5IDMwLjgxMDdDMjkuNDEzMSAzMC4zMDQ1IDI4LjE0OTQgMjguNzMyOSAyNy40Njg5IDI2LjQ5NzJWMzAuMjY0QzI2LjY1MjkgMjkuMTI5NCAyNS4wMTEyIDI2LjM3NSAyNC44NjI5IDI0LjI5NjZDMjQuOTk5OCAyNi42NTI0IDI0LjkxNjQgMjcuNzU2NyAyNC44NTY1IDI4LjAxNkwyMS43NTgxIDI1LjA5OTlDMjAuOTI3NyAyNS41NDU0IDE4LjY5NjEgMjYuNTU3OSAxNi40MTE3IDI3LjA0NEMxNC44NTY0IDI4LjM1NjMgMTIuOTY4OSAzMS42NDExIDEyLjIxOTYgMzMuMTE5NFYzMS4yMzZMMTAuMTU0IDMzLjMwMTdMMTAuODgzIDMxLjExNDVMOS41NDY0NCAzMi4yNjg5QzkuMjQyNjUgMzIuNDUxMSA4LjUzNzkgMzIuODE1NiA4LjE0OTEgMzIuODE1NkM4LjI5NDg5IDMyLjQ3NTQgOC41NzQzOSAzMi4xMDY4IDguNjk1OSAzMS45NjUxTDYuOTM0MDEgMzIuNjMzNEM3LjEzNjUxIDMyLjA0NjEgNy43NzI0MiAzMC43NSA4LjY5NTkgMzAuMjY0QzcuNDMyMTkgMzAuNzUgNi45MTM3OCAzMC44NzE1IDYuODEyNSAzMC44NzE1WiJmaWxsPXdoaXRlIC8+PGRlZnM+PGxpbmVhckdyYWRpZW50IGdyYWRpZW50VW5pdHM9dXNlclNwYWNlT25Vc2UgaWQ9cGFpbnQwX2xpbmVhcl84MjhfOTUwNiB4MT0yMy44NjEyIHgyPTIzLjg2MTIgeTE9NDEuMjUxNiB5Mj0tNC40MTQyOD48c3RvcCBzdG9wLWNvbG9yPSMwMUQ2RTYgLz48c3RvcCBzdG9wLWNvbG9yPSMwMTgyRTcgb2Zmc2V0PTEgLz48L2xpbmVhckdyYWRpZW50PjwvZGVmcz48L3N2Zz48cCBjbGFzcz1sb2FkaW5nLXRleHQ+TG9hZGluZzxzcGFuPi48L3NwYW4+PHNwYW4+Ljwvc3Bhbj48c3Bhbj4uPC9zcGFuPjwvZGl2Pg==""",
                    minimumWidth: 900,
                    minimumHeight: 550,
                    preferredWidth: 1300,
                    preferredHeight: 950,
                    title: "Grayjay",
                    iconPath: Utilities.FindFile("grayjay.png"),
                    appId: "com.futo.grayjay.desktop",
                    fullscreen: isFullscreen
                );
                await window.SetModifyRequestsAsync(true, false);
                if (scaleFactor != null && scaleFactor != 1.0)
                {
                    window.OnFrameLoadEnd += async (frameLoadInfo) =>
                    {
                        try
                        {
                            if (frameLoadInfo.Url != null && frameLoadInfo.Url.EndsWith("/web/index.html"))
                            {
                                await window.SetZoomAsync(scaleFactor.Value);
                                Logger.i(nameof(Program), "Set page scale factor.");
                            }
                            Logger.i(nameof(Program), "OnLoadEnd: " + frameLoadInfo.Url);
                        }
                        catch (Exception e)
                        {
                            Logger.e(nameof(Program), "Failed to set page scale factor.", e);
                        }
                    };
                }
                await window.SetDevelopmentToolsEnabledAsync(true);
                Logger.i(nameof(Program), $"Time to window show {sw.ElapsedMilliseconds}ms");
                Logger.i(nameof(Program), $"Main: Starting window finished ({startWindowWatch.ElapsedMilliseconds}ms)");
            }

            Stopwatch startupTime = Stopwatch.StartNew();
            int proxyParameter = Array.IndexOf(args, "-proxy");
            string? proxyUrl = null;
            if (proxyParameter >= 0)
                proxyUrl = args[proxyParameter + 1];

            #if DEBUG
                proxyUrl = "http://localhost:3000";
            #endif

            //var youtube = GrayjayPlugin.FromUrl("https://plugins.grayjay.app/Youtube/YoutubeConfig.json");
            //if (StatePlugins.GetPlugin(youtube.Config.ID) == null)
            //    StatePlugins.InstallPlugin("https://plugins.grayjay.app/Youtube/YoutubeConfig.json");

            Stopwatch watch = Stopwatch.StartNew();
            Logger.i(nameof(Program), "Main: StateApp.Startup");
            await StateApp.Startup();
            Logger.i(nameof(Program), $"Main: StateApp.Startup finished ({watch.ElapsedMilliseconds}ms)");

            watch.Restart();
            //Logger.i(nameof(Program), "Main: EnableClient");
            //StatePlatform.EnableClient(youtube.Config.ID).Wait();
            //Logger.i(nameof(Program), $"Main: EnableClient finished ({watch.ElapsedMilliseconds}ms)");


            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            server = new GrayjayServer((!isServer && cef != null ? new CEFWindowProvider(cef) : null), 
                isHeadless, 
                isServer,
                !(disableSecurity && (isHeadless || isServer)));
            if (window != null)
            {
                var windowWrapped = new CEFWindowProvider.Window(window);
                await server.RegisterTokenWindow(windowWrapped);
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await server.RunServerAsync(proxyUrl, cancellationTokenSource.Token);
                }
                catch (Exception ex)
                {
                    Logger.e(nameof(Program), $"Main: Unhandled error in RunServerAsync.", ex);
                }
                finally
                {
                    Logger.i(nameof(Program), "Application graceful exit requested.");
                    cancellationTokenSource.Cancel();
                }
            });

            watch.Restart();

            Logger.i(nameof(Program), "Main: Waiting for ASP to start.");
            server.StartedResetEvent.Wait();
            Logger.i(nameof(Program), $"Main: Waiting for ASP to start finished ({watch.ElapsedMilliseconds}ms)");

            startupTime.Stop();
            Logger.i(nameof(Program), $"Main: Readytime: {startupTime.ElapsedMilliseconds}ms");

            File.Delete(StartingUpFile);
            File.WriteAllText(PortFile, server.BaseUri!.Port.ToString());
            Logger.i<Program>("Created PortFile, removed StartingUpFile");

            Logger.i(nameof(Program), "Main: Navigate.");
            if (window != null)
                await window.LoadUrlAsync($"{server.BaseUrl}/web/index.html");
            else if (!isServer)
                OSHelper.OpenUrl($"{server.BaseUrl}/web/index.html");
            
            if (window != null)
                StateApp.SetMainWindow(new CEFWindowProvider.Window(window));

            watch.Stop();


            /*
            new Thread(() =>
            {
                Console.WriteLine("Rebooting in 10s");
                Thread.Sleep(10000);
                Updater.RebootTest(new int[] { Process.GetCurrentProcess().Id }, -1);
                Environment.Exit(0);
            }).Start();
            */

            if (GrayjaySettings.Instance.Notifications.AppUpdates)
            {
                StateWindow.WaitForReady(() =>
                {
                    new Thread(() =>
                    {
                        Logger.i(nameof(Program), "Checking for updates");
                        try
                        {
                            if (!OperatingSystem.IsMacOS())
                            {

                                (bool hasUpdates, int updaterVersion) = Updater.HasUpdate();
                                if (updaterVersion > 0)
                                    GrayjaySettings.Instance.Info.updaterVersion = "v" + updaterVersion.ToString();

                                Logger.i(nameof(Program), (hasUpdates) ? "New updates found" : "No new updates");
                                if (hasUpdates)
                                {
                                    var processIds = new int[]
                                    {
                                        Process.GetCurrentProcess().Id
                                    };
                                    var changelog = Updater.GetTargetChangelog();
                                    int currentVersion = (updaterVersion > 0) ? updaterVersion : Updater.GetUpdaterVersion();
                                    GrayjaySettings.Instance.Info.updaterVersion = "v" + currentVersion.ToString();
                                    if (changelog != null)
                                    {
                                        int targetUpdaterVersion = Updater.GetTargetUpdaterVersion(changelog.Server, changelog.Version, changelog.Platform);
                                        if (targetUpdaterVersion > currentVersion)
                                        {
                                            string url = Updater.GetUpdaterUrl(changelog.Server, changelog.Version, changelog.Platform);
                                            Logger.w(nameof(Program), $"UPDATER REQUIRES UPDATING FROM: {url}\nAttempting self-updating");
                                            Logger.w(nameof(Program), "Starting self-update..");
                                            try
                                            {
                                                using (WebClient client = new WebClient())
                                                {
                                                    string updatedPath = Updater.GetUpdaterExecutablePath() + ".updated";
                                                    client.DownloadFile(url, updatedPath);
                                                    File.Copy(updatedPath, Updater.GetUpdaterExecutablePath(), true);
                                                    if (OperatingSystem.IsLinux())
                                                    {
                                                        //Just in case
                                                        try
                                                        {
                                                            Process chmod = new Process()
                                                            {
                                                                StartInfo = new ProcessStartInfo()
                                                                {
                                                                    FileName = "chmod",
                                                                    Arguments = "-R u=rwx \"" + Updater.GetUpdaterExecutablePath() + "\"",
                                                                    UseShellExecute = false,
                                                                    RedirectStandardOutput = true,
                                                                    CreateNoWindow = true
                                                                }
                                                            };
                                                            chmod.Start();
                                                            while (!chmod.StandardOutput.EndOfStream)
                                                            {
                                                                var line = chmod.StandardOutput.ReadLine();
                                                                if (line != null)
                                                                    Logger.Info<Program>(line);
                                                            }
                                                            chmod.WaitForExit();
                                                        }
                                                        catch (Exception ex)
                                                        {
                                                            Logger.e(nameof(Program), "Failed to fix permissions for Linux on updater");
                                                            throw;
                                                        }
                                                    }
                                                }
                                                Logger.i(nameof(Program), "Self-updating appeared succesful");
                                            }
                                            catch (Exception ex)
                                            {
                                                Logger.e(nameof(Program), "Failed to download new Updater:\n" + url);
                                                StateUI.Dialog(new StateUI.DialogDescriptor()
                                                {
                                                    Text = $"Failed to self-update updater to version {targetUpdaterVersion}",
                                                    TextDetails = "Please download it yourself and override it in the Grayjay directory.\nOn linux, ensure it has execution permissions.",
                                                    Code = "url",
                                                    Actions = new List<StateUI.DialogAction>()
                                                {
                                                new StateUI.DialogAction("Ignore", () =>
                                                {

                                                }, StateUI.ActionStyle.Accent),
                                                new StateUI.DialogAction("Download", () =>
                                                {
                                                    OSHelper.OpenUrl(url);
                                                }, StateUI.ActionStyle.Primary)
                                                }
                                                });
                                            }
                                        }
                                    }

                                    Thread.Sleep(1500);
                                    StateUI.Dialog(new StateUI.DialogDescriptor()
                                    {
                                        Text = $"A new update is available for Grayjay Desktop {(changelog != null ? $"(v{changelog.Version})" : "")}",
                                        TextDetails = "Would you like to install the new update?\nGrayjay.Desktop will close during updating.",
                                        Code = changelog?.Text,
                                        Actions = new List<StateUI.DialogAction>()
                                    {
                                    new StateUI.DialogAction("Never", () =>
                                    {
                                        GrayjaySettings.Instance.Notifications.AppUpdates = false;
                                        GrayjaySettings.Instance.Save();
                                    }, StateUI.ActionStyle.Accent),
                                    new StateUI.DialogAction("Ignore", () =>
                                    {

                                    }, StateUI.ActionStyle.Accent),
                                    new StateUI.DialogAction("Install", () =>
                                    {
                                        Updater.Update(processIds);
                                        window?.CloseAsync();
                                        server?.StopServer();
                                        cef.Dispose();
                                        Environment.Exit(0);
                                    }, StateUI.ActionStyle.Primary)
                                    }
                                    });
                                }
                            }
                            else
                            {
                                string macosServer = "https://updater.grayjay.app/Apps/Grayjay.Desktop";
                                int currentVersion = App.Version;
                                string versionType = App.VersionType;
                                string platform = StateApp.GetPlatformName();

                                int latestMacOS = Updater.GetLatestMacOSVersion(macosServer);

                                if (latestMacOS > currentVersion)
                                {
                                    var changelog = Updater.GetTargetChangelog(macosServer, latestMacOS, "win-x64");
                                    Thread.Sleep(1500);
                                    StateUI.Dialog(new StateUI.DialogDescriptor()
                                    {
                                        Text = $"A new update is available for Grayjay Desktop {(changelog != null ? $"(v{changelog.Version})" : "")}",
                                        TextDetails = "Would you like to install the new update?\nMacOS requires you to redownload the entire application.",
                                        Code = changelog?.Text,
                                        Actions = new List<StateUI.DialogAction>()
                                    {
                                    new StateUI.DialogAction("Ignore", () =>
                                    {

                                    }, StateUI.ActionStyle.Accent),
                                    new StateUI.DialogAction("Install", () =>
                                    {
                                        OSHelper.OpenUrl($"{macosServer}/{latestMacOS}/Grayjay.Desktop-{platform}-v{latestMacOS}.zip");
                                    }, StateUI.ActionStyle.Primary)
                                    }
                                    });
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.e(nameof(Program), "Failed to check updates", ex);
                        }
                    }).Start();
                });
            }

            if (window != null)
            {
                Logger.i(nameof(Program), "Main: Waiting for window exit.");
                await window.WaitForExitAsync(cancellationTokenSource.Token);
                Logger.i(nameof(Program), "Main: Window exited.");
            }
            else
            {
                Logger.i(nameof(Program), "Main: Waiting for server exit.");
                cancellationTokenSource.Token.WaitHandle.WaitOne();
                Logger.i(nameof(Program), "Main: Server exited.");
            }

            File.Delete(PortFile);
            cancellationTokenSource.Cancel();
            if(cef != null)
            cef.Dispose();
            await server.StopServer();

            StateApp.Shutdown();
            Logger.DisposeStaticLogger();
        }
    }
}
