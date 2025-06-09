using Futo.PlatformPlayer.States;
using Grayjay.ClientServer.Exceptions;
using Grayjay.ClientServer.Models;
using Grayjay.ClientServer.Models.Sources;
using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine;
using Grayjay.Engine.Models.Detail;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Pagers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Server.HttpSys;
using System.Net;
using System.Text.Json;
using System.Web;

using Logger = Grayjay.Desktop.POC.Logger;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class SourcesController : ControllerBase
    {


        [HttpGet]
        public PluginConfig[] Sources()
        {
            return StatePlatform.GetAvailableClients().Select(x => x.Config).ToArray();
        }
        [HttpGet]
        public PluginConfigState[] SourceStates()
        {
            return StatePlatform.GetEnabledClients().Select(x => PluginConfigState.FromClient(x)).ToArray();
        }
        [HttpGet]
        public PluginConfig Source(string id)
        {
            return StatePlatform.GetClient(id)?.Config;
        }
        [HttpGet]
        public PluginDetails SourceDetails(string id)
        {
            var descriptor = StatePlatform.GetClient(id)?.Descriptor;
            if (descriptor == null)
                return null;
            return new PluginDetails(descriptor);
        }

        [HttpGet]
        public PluginConfig[] SourcesEnabled()
        {
            return StatePlatform.GetEnabledClients().Select(x => x.Config).ToArray();
        }
        [HttpGet]
        public PluginConfig[] SourcesDisabled()
        {
            return StatePlatform.GetDisabledClients().Select(x => x.Config).ToArray();
        }

        [HttpGet]
        public async Task<bool> SourceEnable(string id)
        {
            try
            {
                await StatePlatform.EnableClient(id, true);

                if (id != "DEV")
                {
                    var config = StatePlugins.GetPlugin(id);
                    if (config != null && StatePlugins.CheckForUpdate(config?.Config, false))
                    {
                        StateUI.Toast($"Update available for [{config.Config.Name}]");
                        StateWebsocket.PluginChanged(config.Config.ID);
                    }
                }
            }
            catch( Exception ex)
            {
                PluginDescriptor config = null;
                try
                {
                    config = StatePlugins.GetPlugin(id);
                }
                catch (Exception _) { }
                throw DialogException.FromException($"Failed to enable plugin [{config?.Config.Name}]", ex);
            }
            return true;
        }
        [HttpGet]
        public async Task<bool> SourceDisable(string id)
        {
            await StatePlatform.DisableClient(id);
            return true;
        }

        [HttpPost]
        public async Task<bool> SourcesReorder([FromBody] string[] ids)
        {
            StatePlatform.ReorderClients(ids);
            return true;
        }

        [HttpGet]
        public async Task<bool> SourceLogin(string id)
        {
            if (GrayjayServer.Instance?.WindowProvider == null)
            {
                throw new NotImplementedException("Running headless, login only supported in UI application mode");
            }

            var descriptor = (id == StateDeveloper.DEV_ID) ? StatePlatform.GetDevClient()?.Descriptor : StatePlugins.GetPlugin(id);
            var pluginConfig = descriptor.Config;
            var authConfig = pluginConfig.Authentication;

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
                if(_didLogIn())
                {
                    Logger.i(nameof(SourcesController), "Logged in!");
                    if (window != null)
                        _ = window.CloseAsync();
                }
            }
            void _closed()
            {
                //Finished
                if (_didLogIn())
                {
                    var plugin = (id == StateDeveloper.DEV_ID) ? StatePlatform.GetDevClient()?.Descriptor : StatePlugins.GetPlugin(id);
                    plugin.SetAuth(new SourceAuth()
                    {
                        Headers = headersFoundMap,
                        CookieMap = cookiesFoundMap
                    });
                    if (id != StateDeveloper.DEV_ID)
                        StatePlugins.UpdatePlugin(id, true);
                }
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
                    }

                    if (_didLogIn())
                        _loggedIn();
                }
                catch(Exception ex)
                {
                    Logger.e(nameof(SourcesController), "Login Interceptor failed: " + ex.Message, ex);
                    throw ex;
                }
            });
            window.OnClosed += _closed;

            return true;
        }

        [HttpGet]
        public async Task<bool> SourceLoginDevClone()
        {
            var plugin = StatePlatform.GetDevClient();
            if (plugin == null)
                return false;

            if(plugin.OriginalID == null)
            {
                StateUI.Toast("DEV Plugin has no original id");
                return false;
            }
            
            var mainPlugin = StatePlugins.GetPlugin(plugin.OriginalID);
            if (mainPlugin == null)
            {
                StateUI.Toast("No main plugin found for id");
                return false;
            }

            var auth = mainPlugin.GetAuth();
            if(auth == null)
            {
                StateUI.Toast("Main plugin is not authenticated");
                return false;
            }
            plugin.Descriptor.SetAuth(auth);
            StateUI.Toast("Plugin auth copied to dev plugin");
            return true;
        }


        [HttpGet]
        public async Task<bool> SourceLogout(string id)
        {
            var descriptor = (id == StateDeveloper.DEV_ID) ? StatePlatform.GetDevClient()?.Descriptor : StatePlugins.GetPlugin(id);
            var pluginConfig = descriptor.Config;
            var authConfig = pluginConfig.Authentication;
            if(authConfig != null)
            {
                descriptor.SetAuth(null);
                if(id != StateDeveloper.DEV_ID)
                    StatePlugins.UpdatePlugin(id, true);
            }
            _ = StateUI.Dialog("", "Please restart Grayjay before logging in again", "Grayjay does not clear past cookies yet after logout, please restart before trying to login again, or it will reuse your current login.", null, 0, new StateUI.DialogAction("Ok", () => { }));
            return true;
        }

        [HttpGet]
        public async Task<bool> SourceDelete(string id)
        {
            StatePlugins.DeletePlugin(id);
            await StatePlatform.UpdateAvailableClients();
            return true;
        }


        [HttpGet]
        public async Task<StatePlugins.Prompt> SourceInstallPrompt(string url)
        {
            try 
            { 
                return StatePlugins.PromptPlugin(url);
            }
            catch (Exception ex)
            {
                throw DialogException.FromException("Invalid Config", ex);
            }
        }

        [HttpPost]
        public object SourceInstallPeerTubePrompt([FromBody] string url)
        {
            try
            {
                return StatePlugins.PromptPlugin("https://pluginhost.grayjay.app/api/plugin/peertube/Config?url=" + HttpUtility.UrlEncode(url));
            }
            catch (Exception ex)
            {
                throw DialogException.FromException("Invalid Config", ex);
            }
        }
        [HttpGet]
        public async Task<PluginConfig> SourceInstall(string url)
        {
            try
            {
                var plugin =  StatePlugins.InstallPlugin(url);
                if (plugin != null && StatePlatform.IsEnabled(plugin.ID))
                {
                    _=StateUI.Dialog(new StateUI.DialogDescriptor()
                    {
                        Text = $"Would you like to enable [{plugin.Name}]?",
                        TextDetails = $"You just installed [{plugin.Name}], would you like to enable it?",
                        Actions = new List<StateUI.DialogAction>()
                        {
                            new StateUI.DialogAction()
                            {
                                Text = "Keep Disabled",
                                Action = (resp) =>
                                {
                                }
                            },
                            new StateUI.DialogAction()
                            {
                                Text = "Enable",
                                Action = (resp) =>
                                {
                                    StatePlatform.EnableClient(plugin.ID);
                                }
                            }
                        }
                    });
                }

                return plugin;
            }
            catch(Exception ex)
            {
                throw DialogException.FromException("Failed to install plugin", ex);
            }
        }


        //TODO: Remove before deploy
        [HttpGet]
        public IActionResult InstallTemp(string url)
        {
            try
            {
                return Ok(StatePlugins.InstallPlugin(url));
            }
            catch(Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }


        [HttpGet]
        public PluginConfig[] OfficialPlugins()
        {
            if (_official.Length > 0)
                return _official.Where(x => !StatePlugins.HasPlugin(x.ID)).ToArray();
            lock (_official)
            {
                if (_official.Length > 0)
                    return _official;
                _official = _officialUrls.AsParallel()
                    .Select(x => PluginConfig.FromUrl(x))
                    .ToArray();
            }
            return _official.Where(x => !StatePlugins.HasPlugin(x.ID)).ToArray(); ;
        }
        [HttpPost]
        public object InstallOfficialPlugins([FromBody]string[] ids)
        {
            PluginConfig[] toInstall = _official.Where(x => ids.Contains(x.ID)).ToArray();

            List<string> existingIds = StatePlugins.GetPlugins().Select(x => x.Config.ID).ToList();
            List<PluginConfig> toQueryEnable = new List<PluginConfig>();

            List<string> exs = new List<string>();
            int success = 0;
            for (int i = 0; i < toInstall.Length; i++) {
                try
                {
                    var plugin = StatePlugins.InstallPlugin(toInstall[i].SourceUrl, i == toInstall.Length - 1);
                    success++;
                    if (plugin != null && !existingIds.Contains(plugin.ID))
                        toQueryEnable.Add(plugin);
                }
                catch(PluginConfigInstallException ex)
                {
                    exs.Add($"[{ex.Config.Name}] " + ex.Message);
                    if(i == toInstall.Length - 1 && success > 0)
                        StatePlatform.UpdateAvailableClients().Wait();
                }
            }

            if (toQueryEnable.Any())
            {
                string pluginsTxt = string.Join(", ", toQueryEnable.Select(x => x.Name));
                _ = StateUI.Dialog(new StateUI.DialogDescriptor()
                {
                    Text = $"Would you like to enable [{pluginsTxt}]?",
                    TextDetails = $"You just installed [{pluginsTxt}], would you like to enable it?",
                    Actions = new List<StateUI.DialogAction>()
                        {
                            new StateUI.DialogAction()
                            {
                                Text = "Keep Disabled",
                                Action = (resp) =>
                                {
                                }
                            },
                            new StateUI.DialogAction()
                            {
                                Text = "Enable",
                                Action = async (resp) =>
                                {
                                    try {
                                        _ = StatePlatform.EnableClients(toQueryEnable.Select(x=>x.ID).ToArray());
                                    }
                                    catch(Exception ex)
                                    {
                                        StateUI.DialogError("Failed to enable Plugin", ex);
                                    }
                                }
                            }
                        }
                });
            }

            return new
            {
                success = true,
                exceptions = exs
            };
        }

        private static string[] _officialUrls = new string[]
        {
            "https://plugins.grayjay.app/Youtube/YoutubeConfig.json",
            "https://plugins.grayjay.app/Odysee/OdyseeConfig.json",
            "https://plugins.grayjay.app/Rumble/RumbleConfig.json",
            "https://plugins.grayjay.app/Patreon/PatreonConfig.json",
            "https://plugins.grayjay.app/Twitch/TwitchConfig.json",
            "https://plugins.grayjay.app/Kick/KickConfig.json",
            "https://plugins.grayjay.app/Nebula/NebulaConfig.json",
            "https://plugins.grayjay.app/Soundcloud/SoundcloudConfig.json",
            "https://plugins.grayjay.app/Bilibili/BiliBiliConfig.json",
            "https://plugins.grayjay.app/Dailymotion/DailymotionConfig.json",
            "https://plugins.grayjay.app/Bitchute/BitchuteConfig.json",
            "https://plugins.grayjay.app/ApplePodcasts/ApplePodcastsConfig.json",
            "https://plugins.grayjay.app/PeerTube/PeerTubeConfig.json"
        };
        private static PluginConfig[] _official = new PluginConfig[0];


        public class PluginDetails
        {
            public bool Enabled { get; set; }
            public PluginConfig Config { get; set; }
            public PluginConfigState State { get; set; }
            public bool HasLoggedIn { get; set; }
            public bool HasCaptcha { get; set; }
            public bool HasUpdate { get; set; }

            public PluginDetails(PluginDescriptor descriptor)
            {
                Config = descriptor.Config;
                HasLoggedIn = descriptor.HasLoggedIn;
                HasCaptcha = descriptor.HasCaptcha;
                HasUpdate = StatePlugins.HasUpdate(descriptor.Config.ID);

                var enabledPlugin = StatePlatform.GetEnabledClients().FirstOrDefault(x => x.ID == descriptor.Config.ID);
                if (enabledPlugin != null)
                {
                    Enabled = true;
                    State = PluginConfigState.FromClient(enabledPlugin);
                }
                else
                    Enabled = false;
            }
        }
    }
}
