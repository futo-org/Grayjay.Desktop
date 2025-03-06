using Grayjay.ClientServer.Constants;
using Grayjay.ClientServer.Controllers;
using Grayjay.ClientServer.Database;
using Grayjay.ClientServer.Database.Indexes;
using Grayjay.ClientServer.Pooling;
using Grayjay.ClientServer.Settings;
using Grayjay.ClientServer.Threading;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine;
using Grayjay.Engine.Exceptions;
using System.Runtime.InteropServices;

using Logger = Grayjay.Desktop.POC.Logger;
using LogLevel = Grayjay.Desktop.POC.LogLevel;

namespace Grayjay.ClientServer.States
{
    public static class StateApp
    {
        public static string VersionName { get; } = VersionCode.ToString();
        public static int VersionCode { get; } = 1;

        public static DatabaseConnection Connection { get; private set; }

        public static CancellationTokenSource AppCancellationToken { get; private set; } = new CancellationTokenSource();

        public static ManagedThreadPool ThreadPool { get; } = new ManagedThreadPool(16, "Global");
        public static ManagedThreadPool ThreadPoolDownload { get; } = new ManagedThreadPool(4, "Download");


        static StateApp()
        {
            
        }

        public static string GetPlatformName()
        {
            if (OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                return "win-arm64";
            else if (OperatingSystem.IsWindows())
                return "win-x64";
            else if (OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                return "linux-arm64";
            else if (OperatingSystem.IsLinux())
                return "linux-x64";
            else if (OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                return "osx-arm64";
            else if (OperatingSystem.IsMacOS())
                return "osx-x64";
            else
                throw new NotImplementedException();
        }


        public static FileInfo GetTemporaryFile(string suffix = null, string prefix = null)
        {
            string fileName = (prefix ?? "") + Guid.NewGuid().ToString() + (suffix ?? "");
            string newFile = Path.Combine(Directories.Temporary, fileName);
            File.WriteAllBytes(newFile, new byte[0]);
            FileInfo info = new FileInfo(newFile);
            return info;
        }

        public static DirectoryInfo GetAppDirectory()
        {
            return new DirectoryInfo(Directories.Base);
        }

        public static string ReadTextFile(string name)
        {
            string path = Path.Combine(GetAppDirectory().FullName, name);
            return (File.Exists(path)) ? File.ReadAllText(path) : null;
        }
        public static void WriteTextFile(string name, string text)
        {
            string path = Path.Combine(GetAppDirectory().FullName, name);
            File.WriteAllText(path, text);
        }


        public static async Task Startup()
        {
            if (Connection != null)
                throw new InvalidOperationException("Connection already set");

            //On boot set all downloading to queued
            foreach (var downloading in StateDownloads.GetDownloading())
                downloading.ChangeState(Models.Downloads.DownloadState.QUEUE);

            Logger.i(nameof(StateApp), "Startup: Initializing PluginEncryptionProvider");
            PluginDescriptor.Encryption = new PluginEncryptionProvider();

            await StatePlatform.UpdateAvailableClients(true);

            Logger.i(nameof(StateApp), "Startup: Initializing DatabaseConnection");
            Connection = new DatabaseConnection();

            Logger.i(nameof(StateApp), $"Startup: Ensuring Table DBSubscriptionCache");
            Connection.EnsureTable<DBSubscriptionCacheIndex>(DBSubscriptionCacheIndex.TABLE_NAME);
            Logger.i(nameof(StateApp), $"Startup: Ensuring Table DBHistory");
            Connection.EnsureTable<DBHistoryIndex>(DBHistoryIndex.TABLE_NAME);

            if (GrayjaySettings.Instance.Notifications.PluginUpdates)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await StatePlugins.CheckForUpdates();

                        await Task.Delay(2500);
                        foreach (var update in StatePlugins.GetKnownPluginUpdates())
                        {
                            //TODO: Proper validation
                            StateUI.Dialog(update.AbsoluteIconUrl, "Update [" + update.Name + "]", "A new version for " + update.Name + " is available.\n\nThese updates may be critical.", null, 0,
                                new StateUI.DialogAction("Ignore", () =>
                                {

                                }, StateUI.ActionStyle.None),
                                new StateUI.DialogAction("Update", () =>
                                {
                                    StatePlugins.InstallPlugin(update.SourceUrl);
                                }, StateUI.ActionStyle.Primary));
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.e(nameof(StateApp), ex.Message, ex);
                    }
                });
            }

            ThreadPool.Run(() =>
            {
                StateTelemetry.Upload();
            });

            if (false)
                new Thread(() =>
                {
                    while (!StateApp.AppCancellationToken.IsCancellationRequested)
                    {
                        int count = 0;
                        int countComp = 0;
                        System.Threading.ThreadPool.GetAvailableThreads(out count, out countComp);
                        
                        if (Logger.WillLog(LogLevel.Debug))
                            Logger.Debug<PlatformClientPool>($"Threadpool available: {count}, {countComp} Completers");
                        Thread.Sleep(500);
                    }
                }).Start();

            //Temporary workaround for youtube
            ThreadPool.Run(() =>
            {
                _ = StatePlatform.GetHome();
            });

            Logger.i(nameof(StateApp), "Startup: Initializing Download Cycle");
            StateDownloads.StartDownloadCycle();
        }

        public static void Shutdown()
        {
            StateSubscriptions.Shutdown();
            ThreadPool.Stop();
            AppCancellationToken.Cancel();
            Connection.Dispose();
            Connection = null;
        }

        private static bool _hasCaptchaDialog = false;
        public static async Task HandleCaptchaException(PluginConfig config, ScriptCaptchaRequiredException ex)
        {
            Logger.w(nameof(StateApp), $"[{config.Name}] Plugin captcha required", ex);
            if (_hasCaptchaDialog)
                return;
            _hasCaptchaDialog = true;
            await StateUI.ShowCaptchaWindow(config, ex, (success) =>
            {
                _hasCaptchaDialog = false;
                Logger.Info(nameof(StateApp), "Captcha result: " + success.ToString());
                StatePlatform.UpdateAvailableClients(true);
            });
        }
    }
}
