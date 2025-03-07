using DotCef;
using Grayjay.ClientServer;
using Grayjay.ClientServer.Constants;
using Grayjay.ClientServer.Controllers;
using Grayjay.ClientServer.Settings;
using Grayjay.ClientServer.States;
using Grayjay.Desktop.CEF;
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
                Logger.i<Program>($"Unhandled exception occurred: {e}");
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
#if DEBUG
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                WindowsAPI.AllocConsole();
#endif

            Console.SetOut(new SuppressingTextWriter(Console.Out));
            Console.SetError(new SuppressingTextWriter(Console.Error));

            Console.WriteLine(Logger.FormatLogMessage(LogLevel.Info, nameof(Program), $"AppContext.BaseDirectory: {AppContext.BaseDirectory}"));
            Console.WriteLine(Logger.FormatLogMessage(LogLevel.Info, nameof(Program), $"Base Directory: {Directories.Base}"));
            Console.WriteLine(Logger.FormatLogMessage(LogLevel.Info, nameof(Program), $"Temporary Directory: {Directories.Temporary}"));
            Console.WriteLine(Logger.FormatLogMessage(LogLevel.Info, nameof(Program), $"Log Level: {(LogLevel)GrayjaySettings.Instance.Logging.LogLevel}"));
            Console.WriteLine(Logger.FormatLogMessage(LogLevel.Info, nameof(Program), $"Log file path: {Directories.Base}/log.txt"));

            FUTO.MDNS.Logger.LogCallback = (level, tag, message, ex) => Logger.Log((LogLevel)level, tag, message, ex);
            FUTO.MDNS.Logger.WillLog = (level) => Logger.WillLog((LogLevel)level);
            Engine.Logger.LogCallback = (level, tag, message, ex) => Logger.Log((LogLevel)level, tag, message, ex);
            Engine.Logger.WillLog = (level) => Logger.WillLog((LogLevel)level);
            DotCef.Logger.LogCallback = (level, tag, message, ex) => Logger.Log((LogLevel)level, tag, message, ex);
            DotCef.Logger.WillLog = (level) => Logger.WillLog((LogLevel)level);

            GrayjayDevSettings.Instance.DeveloperMode = File.Exists("DEV");

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
                File.WriteAllText("launch", Path.GetFileName(p.MainModule!.FileName));
                if (Directory.Exists("cef"))
                    File.WriteAllText("cef/launch", "../" + Path.GetFileName(p.MainModule!.FileName));
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


            watch.Restart();
            var extraArgs = ReconstructArgs(args);
            Logger.i(nameof(Program), "Extra args: " + extraArgs);

            Logger.i(nameof(Program), "Main: Starting DotCefProcess");
            using var cef = !isServer ? new DotCefProcess() : null;
            if (cef != null)
            {
                if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
                    cef.Start("--disable-web-security --use-alloy-style --use-native " + extraArgs);
                else
                    cef.Start("--disable-web-security --use-alloy-style --use-native --no-sandbox " + extraArgs);
            }
            Logger.i(nameof(Program), $"Main: Starting DotCefProcess finished ({watch.ElapsedMilliseconds}ms)");

            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            GrayjayServer server = new GrayjayServer((!isServer && cef != null ? new CEFWindowProvider(cef) : null), 
                isHeadless, 
                isServer);
            _ = Task.Run(async () => 
            {
                try
                {
                    await server.RunServerAsync(proxyUrl, cancellationTokenSource.Token);
                }
                catch (Exception ex)
                {
                    Logger.e(nameof(Program), $"Main: Unhandled error in RunServerAsync.", ex);
                    cancellationTokenSource.Cancel();
                }
            });

            watch.Restart();
            Logger.i(nameof(Program), "Main: Starting window.");
            //TODO: Device scale
            //double scale = 1.5;
            DotCefWindow window = null;
            if (cef != null && !isHeadless && !isServer)
            {
                window = await cef.CreateWindowAsync(
                    url: "about:blank", 
                    minimumWidth: 900, 
                    minimumHeight: 550,
                    preferredWidth: 1300,
                    preferredHeight: 950,
                    title: "Grayjay", 
                    iconPath: Path.GetFullPath("grayjay.png"), 
                    appId: "com.futo.grayjay.desktop"
                );
                await window.SetDevelopmentToolsEnabledAsync(true);
                Logger.i(nameof(Program), $"Main: Starting window finished ({watch.ElapsedMilliseconds}ms)");
            }
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

            Logger.i(nameof(Program), "Main: Waiting for window exit.");
            if (window != null)
                await window.WaitForExitAsync(cancellationTokenSource.Token);
            else
                cancellationTokenSource.Token.WaitHandle.WaitOne();
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
