
using Grayjay.ClientServer;
using Grayjay.ClientServer.Store;
using Grayjay.Engine;
using System.Linq;
using System.Net;

namespace Grayjay.Desktop.POC.Port.States
{
    public static class StatePlugins
    {

        private static StringUniqueStore _pluginScripts = new StringUniqueStore("plugin_scripts")
            .WithMemory()
            .Load();
        private static ManagedStore<PluginDescriptor> _plugins = new ManagedStore<PluginDescriptor>("plugins")
            .WithEncryption()
            .Load();

        private static Dictionary<string, bool> _hasUpdates = new Dictionary<string, bool>();


        public static event Action<PluginDescriptor, bool> OnPluginSettingsChanged;
        public static event Action<PluginDescriptor> OnPluginAuthChanged;
        public static event Action<PluginDescriptor> OnPluginCaptchaChanged;

        static StatePlugins()
        {
            foreach (var plugin in _plugins.GetObjects())
            {
                RegisterDescriptor(plugin);
            }
        }
        private static void RegisterDescriptor(PluginDescriptor descriptor)
        {
            descriptor.OnAuthChanged += () => OnPluginAuthChanged?.Invoke(descriptor);
            descriptor.OnCaptchaChanged += () => OnPluginCaptchaChanged?.Invoke(descriptor);
        }

        public static void CheckForUpdate(PluginConfig plugin)
        {
            try
            {
                var config = PluginConfig.FromUrl(plugin.SourceUrl);
                if (config.Version > plugin.Version) {
                    Logger.i(nameof(StatePlugins), $"New update found for [{config.Name}] ({plugin.Version}=>{config.Version})");
                    lock (_hasUpdates)
                    {
                        _hasUpdates[config.ID] = true;
                    }
                }
            }
            catch(Exception ex)
            {
                Logger.e(nameof(StatePlugins), $"Failed to check updates for plugin [{plugin.Name}]", ex);
            }
        }
        public static void CheckForUpdates()
        {
            StatePlatform.GetEnabledClients().AsParallel().ForAll((client) =>
            {
                CheckForUpdate(client.Config);
            });
        }
        public static bool HasUpdate(string pluginId)
        {
            lock (_hasUpdates)
            {
                return _hasUpdates.ContainsKey(pluginId) && _hasUpdates[pluginId];
            }
        }
        public static List<PluginConfig> GetKnownPluginUpdates()
        {
            return _plugins.GetObjects().Where(x => HasUpdate(x.Config.ID)).Select(x => x.Config).ToList();
        } 

        public static void ReloadPluginFile()
        {
            _plugins = new ManagedStore<PluginDescriptor>("plugins")
                .WithEncryption()
                .Load();
        }

        public static bool HasPlugin(string id)
        {
            return _plugins.FindObject(x => x.Config.ID == id) != null;
        }
        public static PluginDescriptor GetPlugin(string id)
        {
            var plugin = _plugins.FindObject(x => x.Config.ID == id);
            return plugin;
        }
        public static List<PluginDescriptor> GetPlugins()
        {
            return _plugins.GetObjects();
        }

        public static string GetPluginIconOrNull(string id)
        {
            return GetPlugin(id).Config.AbsoluteIconUrl;
        }

        public static string GetPluginScript(string id)
        {
            return _pluginScripts.Read(id);
        }


        public static Prompt PromptPlugin(string sourceUrl)
        {
            using (WebClient client = new WebClient())
            {
                if (!sourceUrl.StartsWith("http"))
                    sourceUrl = "https://" + sourceUrl;

                PluginConfig config;
                try
                {
                    var configJson = client.DownloadString(sourceUrl);
                    if (string.IsNullOrEmpty(configJson))
                        throw new InvalidOperationException("No config response");
                    config = PluginConfig.FromJson(configJson);
                    config.SourceUrl = sourceUrl;
                }
                catch (Exception ex)
                {
                    Logger.e(nameof(StatePlugins), "Failed to fetch or parse config", ex);
                    throw new InvalidDataException("Failed to fetch or parse config");
                }

                return new Prompt()
                {
                    Config = config,
                    Warnings = config.GetWarnings(),
                    AlreadyInstalled = StatePlugins.HasPlugin(config.ID)
                };
            }
        }
        public static PluginConfig InstallPlugin(string sourceUrl, bool reload = true)
        {
            using (WebClient client = new WebClient())
            {
                PluginConfig config;
                try
                {
                    var configJson = client.DownloadString(sourceUrl);
                    if (string.IsNullOrEmpty(configJson))
                        throw new InvalidOperationException("No config response");
                    config = PluginConfig.FromJson(configJson);
                    config.SourceUrl = sourceUrl;
                }
                catch(Exception ex)
                {
                    Logger.e(nameof(StatePlugins), "Failed to fetch or parse config", ex);
                    throw new InvalidDataException("Failed to fetch or parse config");
                }

                string script;
                try
                {
                    script = client.DownloadString(config.AbsoluteScriptUrl);
                    if (string.IsNullOrEmpty(script))
                        throw new InvalidDataException("No script");
                }
                catch(Exception ex)
                {
                    Logger.e(nameof(StatePlugins), "Failed to fetch script", ex);
                    throw new InvalidDataException("Failed to fetch script");
                }

                InstallPlugin(config, script, reload);

                return config;
            }
        }

        public static PluginConfig InstallPlugin(PluginConfig config, string script, bool doReload = true)
        {
            try
            {
                var existing = GetPlugin(config.ID);
                if(existing != null)
                {
                    if (config.ScriptPublicKey != existing.Config.ScriptPublicKey)
                        throw new Exception("Plugin author public key changed");
                }

                if (!config.VerifyAuthority())
                    throw new Exception("Plugin public key appears invalid or tampered");
                if (!config.VerifySignature(script))
                    throw new Exception("Plugin script is tampered with and does not match the signature");

                var tempDescriptor = new PluginDescriptor(config);
                using (GrayjayPlugin plugin = new GrayjayPlugin(tempDescriptor, script))
                    plugin.Test();


                var descriptor = CreatePlugin(config, script, true);

                if(doReload)
                    StatePlatform.UpdateAvailableClients().Wait();
            }
            catch(Exception ex)
            {
                throw new PluginConfigInstallException(ex.Message, config, ex);
            }
            return config;
        }

        public static PluginDescriptor CreatePlugin(PluginConfig config, string script, bool reinstall)
        {
            if(!string.IsNullOrEmpty(config.ScriptSignature))
            {
                //TODO: validate
            }

            var existing = GetPlugin(config.ID);
            var existingAuth = existing?.GetAuth();
            var existingCaptcha = existing?.GetCaptchaData();

            if(existing != null)
            {
                if (!reinstall)
                    throw new InvalidOperationException($"Plugin with id {config.ID} already exists");
                else
                    DeletePlugin(config.ID);
            }

            var descriptor = new PluginDescriptor(config, existing?.AuthEncrypted, existing?.CaptchaEncrypted, existing?.Settings);
            if(existing != null)
                descriptor.AppSettings = existing.AppSettings;
            _pluginScripts.Write(descriptor.Config.ID, script);
            _plugins.Save(descriptor);
            RegisterDescriptor(descriptor);
            return descriptor;
        }

        public static PluginDescriptor UpdatePlugin(string id, bool doReload = false)
        {
            lock(_pluginScripts)
            {
                lock(_plugins)
                {
                    var plugin = GetPlugin(id);
                    _plugins.Save(plugin);
                    OnPluginSettingsChanged?.Invoke(plugin, doReload);
                    GrayjayServer.Instance.WebSocket.Broadcast(id, "PluginUpdated", id);
                    return plugin;
                }
            }
        }

        public static void DeletePlugin(string id)
        {
            lock(_pluginScripts)
            {
                lock(_plugins)
                {
                    var plugin = GetPlugin(id);
                    if (plugin != null) 
                    {
                        _plugins.Delete(plugin);
                        _pluginScripts.Delete(plugin.Config.ID);
                    }
                }
            }
        }


        public static IEnumerable<string> GetEmbeddedSourcesDefault()
        {
            return new List<string>();
            //throw new NotImplementedException();
        }
        public static void InstallMissingEmbeddedPlugins()
        {
            //throw new NotImplementedException();
        }
        public static void UpdateEmbeddedPlugins()
        {
            //throw new NotImplementedException();
        }


        public class Prompt
        {
            public PluginConfig Config { get; set; }
            public List<PluginWarning> Warnings { get; set; } = new List<PluginWarning>();

            public bool AlreadyInstalled { get; set; }
        }
    }

    public class PluginConfigInstallException: Exception
    {
        public PluginConfig Config { get; private set; }
        public PluginConfigInstallException(string msg, PluginConfig config, Exception inner): base(msg, inner)
        {
            Config = config;
        }
    }
}
