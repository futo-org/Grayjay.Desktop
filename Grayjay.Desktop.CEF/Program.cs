using DotCef;
using Grayjay.ClientServer;
using Grayjay.ClientServer.Constants;
using Grayjay.ClientServer.Controllers;
using Grayjay.ClientServer.Settings;
using Grayjay.ClientServer.States;
using Grayjay.Desktop.CEF;
using Grayjay.Desktop.POC;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Grayjay.Desktop
{
    internal class Program
    {
        private const bool ShowCefLogs = true;
        private static string? StartingUpFile = null;
        private const string StartingUpFileName = "starting";
        private static string? PortFile = null;
        private const string PortFileName = "port";   
        private const int StartupTimeoutSeconds = 5;
        private const int NewWindowTimeoutSeconds = 5;

        private static bool IsProcessRunningByPath(string path)
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.MainModule?.FileName == path && process.Id != Process.GetCurrentProcess().Id)
                    {
                        return true;
                    }
                }
                catch
                {
                    // Ignore processes that may throw due to access issues
                }
            }
            return false;
        }

        private static async Task<bool> WaitForPortFileAndProcess()
        {
            string currentProcessPath = Process.GetCurrentProcess().MainModule!.FileName;
            int waitedSeconds = 0;

            while (waitedSeconds < StartupTimeoutSeconds)
            {
                if (File.Exists(PortFile!))
                    return true;

                if (!IsProcessRunningByPath(currentProcessPath))
                    return false;

                await Task.Delay(1000);
                waitedSeconds++;
            }

            return false; // Timeout reached without PortFile creation
        }

        private static void KillExistingProcessByPath()
        {
            string currentProcessPath = Process.GetCurrentProcess().MainModule!.FileName;

            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.Id != Process.GetCurrentProcess().Id && process.MainModule?.FileName == currentProcessPath)
                    {
                        process.Kill();
                        process.WaitForExit();
                    }
                }
                catch
                {
                    // Ignore processes that may throw due to access issues
                }
            }
        }

        private static async Task<bool> TryOpenWindow()
        {
            try
            {
                string port = File.ReadAllText(PortFile!);
                using HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(NewWindowTimeoutSeconds) };
                var response = await client.GetAsync($"http://127.0.0.1:{port}/Window/StartWindow");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
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
            if(args.Length > 0 && args[0] == "version")
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

            Logger.i(nameof(Directories), $"Base Directory: {Directories.Base}");
            Logger.i(nameof(Directories), $"Temporary Directory: {Directories.Temporary}");
            Logger.i(nameof(Directories), $"Log file path: {Directories.Base}/log.txt");

            GrayjayDevSettings.Instance.DeveloperMode = File.Exists("DEV");

            foreach(var arg in args)
            {
                Console.WriteLine("Arg: " + arg);
            }

            Updater.SetStartupArguments(string.Join(" ", args.Select(x => (x.Contains(" ") ? $"\"{x}\"" : x))));

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
            StateApp.Startup();
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
                cef.OutputDataReceived += (msg) =>
                {
                    if (msg != null && ShowCefLogs)
                        Logger.i("CEF", msg);
                };
                cef.ErrorDataReceived += (msg) =>
                {
                    if (msg != null && ShowCefLogs)
                        Logger.e("CEF", msg);
                };

                if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
                    cef.Start("--disable-web-security " + extraArgs);
                else
                    cef.Start("--disable-web-security --use-views " + extraArgs);
            }
            Logger.i(nameof(Program), $"Main: Starting DotCefProcess finished ({watch.ElapsedMilliseconds}ms)");

            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            GrayjayServer server = new GrayjayServer((!isServer && cef != null ? 
                    new CEFWindowProvider(cef) : null), 
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
                window = cef.CreateWindowAsync("about:blank", ((int)(900)), ((int)(500)), ((int)(1300)), ((int)(900)), title: "Grayjay", iconPath: Path.GetFullPath("grayjay.png")).Result;
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

            new Thread(() =>
            {
                try
                {
                    var hasUpdates = Updater.HasUpdate();
                    Logger.i(nameof(Program), (hasUpdates) ? "New updates found" : "No new updates");
                    if (hasUpdates)
                    {
                        var processIds = new int[]
                        {
                            Process.GetCurrentProcess().Id
                        };
                        var changelog = Updater.GetTargetChangelog();
                        int currentVersion = Updater.GetUpdaterVersion();
                        if (changelog != null)
                        {
                            int targetUpdaterVersion = Updater.GetTargetUpdaterVersion(changelog.Server, changelog.Version, changelog.Platform);
                            if (targetUpdaterVersion > currentVersion)
                            {
                                string url = Updater.GetUpdaterUrl(changelog.Server, changelog.Version, changelog.Platform);
                                Logger.w(nameof(Program), $"UPDATER REQUIRES UPDATING FROM: {url}\nAttempting self-updating");
                                Logger.w(nameof(Program), "Starting self-update..");
                                Updater.UpdateSelf();
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
                catch(Exception ex)
                {
                    Logger.e(nameof(Program), "Failed to check updates", ex);
                }
            }).Start();

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
        }
    }
}
