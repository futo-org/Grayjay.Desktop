using Grayjay.ClientServer.Constants;
using Grayjay.ClientServer.Controllers;
using Grayjay.ClientServer.Database;
using Grayjay.ClientServer.Database.Indexes;
using Grayjay.ClientServer.Store;
using Grayjay.Desktop.POC;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine;
using Grayjay.Engine.Exceptions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Grayjay.ClientServer.States
{
    public static class StateApp
    {
        public static string VersionName { get; } = VersionCode.ToString();
        public static int VersionCode { get; } = 1;

        public static DatabaseConnection Connection { get; private set; }

        public static CancellationTokenSource AppCancellationToken { get; private set; } = new CancellationTokenSource();


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


        public static void Startup()
        {
            if (Connection != null)
                throw new InvalidOperationException("Connection already set");

            Logger.i(nameof(StateApp), "Startup: Initializing PluginEncryptionProvider");
            PluginDescriptor.Encryption = new PluginEncryptionProvider();

            Logger.i(nameof(StateApp), "Startup: Initializing DatabaseConnection");
            Connection = new DatabaseConnection();

            Logger.i(nameof(StateApp), $"Startup: Ensuring Table DBSubscriptionCache");
            Connection.EnsureTable<DBSubscriptionCacheIndex>(DBSubscriptionCacheIndex.TABLE_NAME);
            Logger.i(nameof(StateApp), $"Startup: Ensuring Table DBHistory");
            Connection.EnsureTable<DBHistoryIndex>(DBHistoryIndex.TABLE_NAME);

            _ = Task.Run(async () =>
            {
                StatePlugins.CheckForUpdates();

                await Task.Delay(2500);
                foreach(var update in StatePlugins.GetKnownPluginUpdates())
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
            });

            _ = Task.Run(async () =>
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
                        ThreadPool.GetAvailableThreads(out count, out countComp);
                        Console.WriteLine($"Threadpool available: {count}, {countComp} Completers");
                        Thread.Sleep(1000);
                    }
                }).Start();

            //Temporary workaround for youtube
            Task.Run(() =>
            {
                StatePlatform.GetHome();
            });

            Logger.i(nameof(StateApp), "Startup: Initializing Download Cycle");
            StateDownloads.StartDownloadCycle();
        }

        public static void Shutdown()
        {
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
                Console.WriteLine("Captcha result: " + success.ToString());
                StatePlatform.UpdateAvailableClients(true);
            });
        }
    }
}
