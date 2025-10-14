
using Futo.PlatformPlayer.States;
using Grayjay.ClientServer;
using Grayjay.ClientServer.Controllers;
using Grayjay.ClientServer.States;
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
        public static void RegisterDescriptor(PluginDescriptor descriptor)
        {
            descriptor.OnAuthChanged += () => OnPluginAuthChanged?.Invoke(descriptor);
            descriptor.OnCaptchaChanged += () => OnPluginCaptchaChanged?.Invoke(descriptor);
        }

        public static bool CheckForUpdate(PluginConfig plugin, bool alwaysUpdate = true)
        {
            try
            {
                if(!alwaysUpdate)
                {
                    lock (_hasUpdates)
                    {
                        if (_hasUpdates.ContainsKey(plugin.ID))
                            return _hasUpdates[plugin.ID];
                    }
                }
                var config = PluginConfig.FromUrl(plugin.SourceUrl);
                if (config.Version > plugin.Version) {
                    Logger.i(nameof(StatePlugins), $"New update found for [{config.Name}] ({plugin.Version}=>{config.Version})");
                    lock (_hasUpdates)
                    {
                        _hasUpdates[config.ID] = true;
                    }
                    return true;
                }
                return false;
            }
            catch(Exception ex)
            {
                Logger.e(nameof(StatePlugins), $"Failed to check updates for plugin [{plugin.Name}]", ex);
                return false;
            }
        }
        public static Task CheckForUpdates()
        {
            var clients = StatePlatform.GetEnabledClients();

            return Task.WhenAll(clients.Select(async (client) =>
            {
                await StateApp.ThreadPool.Run<bool>(() => CheckForUpdate(client.Config));
            }));
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
            if (id == StateDeveloper.DEV_ID)
                throw new InvalidOperationException("Attempted to make developer plugin persistent, this is not allowed");
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
            string possiblePrefix = "grayjay://plugin/";
            if (sourceUrl.StartsWith(possiblePrefix))
                sourceUrl = sourceUrl.Substring(possiblePrefix.Length);
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
                if (!string.IsNullOrEmpty(config.ScriptSignature) && !config.VerifySignature(script))
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
            if (config.ID == StateDeveloper.DEV_ID)
                throw new InvalidOperationException("Attempted to make developer plugin persistent, this is not allowed");

            if(!string.IsNullOrEmpty(config.ScriptSignature))
            {
                var isValid = config.VerifySignature(script);
                if (!isValid)
                    throw new InvalidOperationException($"Script signature is invalid. Possible tampering");
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
            if (id == StateDeveloper.DEV_ID)
                throw new InvalidOperationException("Attempted to make developer plugin persistent, this is not allowed");
            lock (_pluginScripts)
            {
                lock(_plugins)
                {
                    var plugin = GetPlugin(id);
                    _plugins.Save(plugin);
                    OnPluginSettingsChanged?.Invoke(plugin, doReload);
                    GrayjayServer.Instance.WebSocket.Broadcast(id, "PluginUpdated", id);
                    StateWebsocket.PluginChanged(id);
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


        public static async Task<SourceAuth> AuthenticatePlugin(PluginConfig pluginConfig)
        {

            if (GrayjayServer.Instance?.WindowProvider == null)
            {
                throw new NotImplementedException("Running headless, login only supported in UI application mode");
            }
            var authConfig = pluginConfig.GetPlatformAuthentication();

            bool urlFound = string.IsNullOrEmpty(authConfig.CompletionUrl);
            Dictionary<string, Dictionary<string, string>> headersFoundMap = new Dictionary<string, Dictionary<string, string>>();
            Dictionary<string, Dictionary<string, string>> cookiesFoundMap = new Dictionary<string, Dictionary<string, string>>();

            bool completionUrlExcludeQuery = false;
            string completionUrlToCheck = (string.IsNullOrEmpty(authConfig.CompletionUrl)) ? null : authConfig.CompletionUrl;
            if (completionUrlToCheck != null)
            {
                if (authConfig.CompletionUrl.EndsWith("?*"))
                {
                    completionUrlToCheck = completionUrlToCheck.Substring(0, completionUrlToCheck.Length - 2);
                    completionUrlExcludeQuery = true;
                }
            }

            IWindow window = null;
            TaskCompletionSource<SourceAuth> tcs = new TaskCompletionSource<SourceAuth>();

            bool _didLogIn()
            {
                var headersFound = authConfig.HeadersToFind?.Select(x => x.ToLower())?.All(reqHeader => headersFoundMap.Any(x => x.Value.ContainsKey(reqHeader))) ?? true;
                var domainHeadersFound = authConfig.DomainHeadersToFind?.All(x =>
                {
                    if (x.Value.Count == 0)
                        return true;
                    if (!headersFoundMap.ContainsKey(x.Key.ToLower()))
                        return false;
                    var foundDomainHeaders = headersFoundMap[x.Key.ToLower()] ?? new Dictionary<string, string>();
                    return x.Value.All(reqHeader => foundDomainHeaders.ContainsKey(reqHeader.ToLower()));
                }) ?? true;
                var cookiesFound = authConfig.CookiesToFind?.All(toFind => cookiesFoundMap.Any(x => x.Value.ContainsKey(toFind))) ?? true;

                return (urlFound && headersFound && domainHeadersFound && cookiesFound);
            }

            void _loggedIn()
            {
                if (_didLogIn())
                {
                    Logger.i(nameof(SourcesController), "Logged in!");
                    _ = window?.CloseAsync();
                }
            }
            void _closed()
            {
                //Finished
                if (_didLogIn())
                {
                    tcs.SetResult(new SourceAuth()
                    {
                        Headers = headersFoundMap,
                        CookieMap = cookiesFoundMap
                    });
                }
                else
                    tcs.SetResult(null);
            }

            window = await GrayjayServer.Instance.WindowProvider.CreateInterceptorWindowAsync("Grayjay (Login)", authConfig.LoginUrl, authConfig.UserAgent, (InterceptorRequest request) =>
            {
                try
                {
                    var uri = new Uri(request.Url);
                    string domain = uri.Host;
                    string domainLower = uri.Host.ToLower();

                    if (!urlFound)
                    {
                        if (completionUrlExcludeQuery)
                        {
                            if (request.Url.Contains("?"))
                                urlFound = request.Url.Substring(0, request.Url.IndexOf("?")) == completionUrlToCheck;
                            else
                                urlFound = request.Url == completionUrlToCheck;
                        }
                        else
                            urlFound = request.Url == completionUrlToCheck;
                    }

                    if (authConfig.AllowedDomains != null && !authConfig.AllowedDomains.Contains(uri.Host))
                        return;

                    //HEADERS
                    if (domainLower != null)
                    {
                        var headersToFind = (authConfig.HeadersToFind?.Select(x => (x.ToLower(), domainLower)).ToList() ?? new List<(string, string)>())
                            .Concat(authConfig.DomainHeadersToFind?
                                .Where(x => domainLower.MatchesDomain(x.Key.ToLower()))
                                .SelectMany(y => y.Value.Select(header => (header.ToLower(), y.Key.ToLower())))
                                .ToList() ?? new List<(string, string)>())
                            .ToList();

                        var foundHeaders = request.Headers.Where(requestHeader => headersToFind.Any(x => x.Item1.Equals(requestHeader.Key, StringComparison.OrdinalIgnoreCase))
                            && (!requestHeader.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) || requestHeader.Value.FirstOrDefault() != "undefined"))
                            .ToList();

                        foreach (var header in foundHeaders)
                        {
                            if (header.Value.Count < 1)
                                continue;

                            foreach (var headerDomain in headersToFind.Where(x => x.Item1.Equals(header.Key, StringComparison.OrdinalIgnoreCase)))
                            {
                                if (!headersFoundMap.ContainsKey(headerDomain.Item2))
                                    headersFoundMap[headerDomain.Item2] = new Dictionary<string, string>();
                                headersFoundMap[headerDomain.Item2][header.Key.ToLower()] = header.Value.First();
                            }
                        }
                    }

                    //COOKIES
                    if (request.Headers.TryGetValue("cookie", out var cookieHeader))
                    {
                        var cookieString = cookieHeader.FirstOrDefault();
                        if (cookieString != null)
                        {
                            var domainParts = domain.Split(".");
                            var cookieDomain = (domainParts.Length > 2) ?
                                "." + string.Join(".", domainParts.Skip(1)) :
                                "." + string.Join(".", domainParts);
                            if (domainParts.Length > 2 && cookieDomain.IsSLD())
                                cookieDomain = "." + string.Join(".", domainParts);

                            if (pluginConfig == null || pluginConfig.AllowUrls.Any(x => x == "everywhere" || domain.MatchesDomain(x)))
                            {
                                authConfig.CookiesToFind?.ForEach(cookiesToFind =>
                                {
                                    var cookies = cookieString.Split(";");
                                    foreach (var cookieStr in cookies)
                                    {
                                        var cookieSplitIndex = cookieStr.IndexOf("=");
                                        if (cookieSplitIndex <= 0) continue;
                                        var cookieKey = cookieStr.Substring(0, cookieSplitIndex).Trim();
                                        var cookieVal = cookieStr.Substring(cookieSplitIndex + 1).Trim();

                                        if (authConfig.CookiesExclOthers && !cookiesToFind.Contains(cookieKey))
                                            continue;

                                        if (cookiesFoundMap.ContainsKey(cookieDomain))
                                            cookiesFoundMap[cookieDomain][cookieKey] = cookieVal;
                                        else
                                            cookiesFoundMap[cookieDomain] = new Dictionary<string, string>() { { cookieKey, cookieVal } };
                                    }
                                });
                            }
                        }

                        if (_didLogIn())
                            _loggedIn();
                    }
                }
                catch (Exception ex)
                {
                    Logger.e(nameof(SourcesController), "Login Interceptor failed: " + ex.Message, ex);
                    throw ex;
                }
            });
            window.OnClosed += _closed;
            return await tcs.Task;
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
