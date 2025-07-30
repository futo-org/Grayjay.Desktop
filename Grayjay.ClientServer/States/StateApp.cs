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
using System.Diagnostics;
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

        //TODO: Make this more easily accessible from controllers (request=>window)
        public static IWindow MainWindow { get; private set; }
        

        public static void SetMainWindow(IWindow window)
        {
            MainWindow = window;
        }


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

        public static void SettingsChanged(GrayjaySettings settings)
        {
            //TODO: Pass previous settings to this? so this isn't executed when unchanged
            if (settings.Synchronization.Enabled)
            {
                if (StateSync.Instance.SyncService == null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await StateSync.Instance.StartAsync();
                        }
                        catch (Exception e)
                        {
                            Logger.i(nameof(StateApp), "Failed to start StateSync", e);
                        }
                    });
                }
            }
            else
            {
                if (StateSync.Instance.SyncService != null)
                {
                    try
                    {
                        StateSync.Instance.Dispose();
                    }
                    catch (Exception e)
                    {
                        Logger.i(nameof(StateApp), "Failed to stop StateSync", e);
                    }
                }
            }
        }

        public static async Task Startup()
        {
            Stopwatch sw = Stopwatch.StartNew();

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

                        new Thread(async () =>
                        {
                            int i = 0;
                            while (!StateApp.AppCancellationToken.IsCancellationRequested)
                            {
                                try
                                {
                                    if (i % 60 == 0)
                                    {
                                        Logger.i("StateApp", "Checking for plugin updates");
                                        await StatePlugins.CheckForUpdates();
                                    }

                                    Thread.Sleep(1000);
                                    i++;
                                }
                                catch(Exception ex)
                                {
                                    Logger.e("StateApp", "Failed to check for plugin updates due to: " + ex.Message, ex);
                                }
                            }
                        }).Start();
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
            new Thread(() =>
            {
                var now = DateTime.Now;
                var subscriptions = StateSubscriptions.GetSubscriptions().Where(x=>now.Subtract(x.LastChannelUpdate).TotalMinutes > 120).OrderBy(x => x.LastChannelUpdate).ToList();
                foreach(var subscription in subscriptions)
                {
                    if (StateApp.AppCancellationToken.IsCancellationRequested)
                        return;
                    try
                    {
                        Logger.i(nameof(StateApp), $"Updating subscription data for [{subscription.Channel.Name}]");
                        var channel = StatePlatform.GetChannel(subscription.Channel.Url);
                        subscription.UpdateChannelObject(channel);
                    }
                    catch(Exception ex)
                    {
                        Logger.w(nameof(StateApp), "Failed to update subscription channel object due to: " + ex.Message, ex);
                        subscription.UpdateChannelAttemptDate();
                    }
                    Thread.Sleep(3500 + Random.Shared.Next(0, 1000));
                }
            }).Start();

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

            ThreadPool.Run(() =>
            {
                try
                {
                    StateDownloads.CleanupFiles();
                }
                catch(Exception ex)
                {

                }
            });

            ThreadPool.Run(() =>
            {
                var enabledPlugins = StatePlatform.GetEnabledClients();
                foreach(var plugin in enabledPlugins)
                {
                    try
                    {
                        if(plugin.Descriptor.AppSettings.Sync.EnableHistorySync == true)
                        {
                            StateHistory.SyncRemoteHistory(plugin);
                        }
                    }
                    catch(Exception ex)
                    {
                        Logger.e("StateApp", $"Failed to update remote history for {plugin.Config.Name}");
                    }
                }
            });


            //Temporary workaround for youtube
            ThreadPool.Run(() =>
            {
                try
                {
                    _ = StatePlatform.GetHome();
                }
                catch (Exception ex) { }
            });

            Logger.i(nameof(StateApp), "Startup: Initializing Download Cycle");
            StateDownloads.StartDownloadCycle();

            if(false) //To verify if async threads are every blocked
                new Thread(() =>
                {
                    while(Connection != null)
                    {
                        Task.Run(() => Console.WriteLine("Async Heartbeat"));
                        Thread.Sleep(1000);
                    }
                }).Start();
            Logger.i(nameof(StateApp), $"Startup duration {sw.ElapsedMilliseconds}ms");
        }

        public static void Shutdown()
        {
            StateSubscriptions.Shutdown();
            StateWindow.Shutdown();
            ThreadPool.Stop();
            AppCancellationToken.Cancel();
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
