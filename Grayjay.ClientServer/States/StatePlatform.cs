using Futo.PlatformPlayer.States;
using Grayjay.ClientServer;
using Grayjay.ClientServer.Controllers;
using Grayjay.ClientServer.Developer;
using Grayjay.ClientServer.Exceptions;
using Grayjay.ClientServer.Models;
using Grayjay.ClientServer.Models.Sources;
using Grayjay.ClientServer.Pagers;
using Grayjay.ClientServer.Pooling;
using Grayjay.ClientServer.Settings;
using Grayjay.ClientServer.States;
using Grayjay.ClientServer.Store;
using Grayjay.Engine;
using Grayjay.Engine.Exceptions;
using Grayjay.Engine.Models.Capabilities;
using Grayjay.Engine.Models.Channel;
using Grayjay.Engine.Models.Comments;
using Grayjay.Engine.Models.Detail;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Models.Playback;
using Grayjay.Engine.Pagers;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Web;

namespace Grayjay.Desktop.POC.Port.States
{
    public static class StatePlatform
    {
        public static string TAG = "StatePlatform";

        private static StringArrayStore _enabledClientsPersistent = new StringArrayStore("enabledClients", Array.Empty<string>()).Load();

        private static List<GrayjayPlugin> _availableClients = new List<GrayjayPlugin>();
        private static List<GrayjayPlugin> _enabledClients = new List<GrayjayPlugin>();

        private static object _clientsLock = new object();

        //ClientPools are used to isolate plugin usage of certain components from others
        //This prevents for example a background task like subscriptions from blocking a user from opening a video
        //It also allows parallel usage of plugins that would otherwise be impossible.
        //Pools always follow the behavior of the base client. So if user disables a plugin, it kills all pooled clients.
        //Each pooled client adds additional memory usage.
        //WARNING: Be careful with pooling some calls, as they might use the plugin subsequently afterwards. For example pagers might block plugins in future calls.
        private static PlatformMultiClientPool _mainClientPool = new PlatformMultiClientPool("Main", 2); //Used for all main user events, generally user critical
        private static PlatformMultiClientPool _utilityClientPool = new PlatformMultiClientPool("Utility", 2); //Used for all main user events, generally user critical
        private static PlatformMultiClientPool _pagerClientPool = new PlatformMultiClientPool("Pagers", 2); //Used primarily for calls that result in front-end pagers, preventing them from blocking other calls.
        private static PlatformMultiClientPool _channelClientPool = new PlatformMultiClientPool("Channels", GrayjaySettings.Instance.Subscriptions.SubscriptionConcurrency); //Used primarily for subscription/background channel fetches
        private static PlatformMultiClientPool _trackerClientPool = new PlatformMultiClientPool("Trackers", 1); //Used exclusively for playback trackers
        private static PlatformMultiClientPool _liveEventClientPool = new PlatformMultiClientPool("LiveEvents", 1); //Used exclusively for live events

        private static Dictionary<string, string> _icons = new Dictionary<string, string>();

        private static event Action<bool> OnSourcesAvailableChanged;
        private static event Action<GrayjayPlugin> OnSourceEnabled;
        private static event Action<GrayjayPlugin> OnSourceDisabled;
        private static event Action OnDevSourceChanged;

        private static bool _didStartup = false;

        private static Regex REGEX_PAGER_REF = new Regex("[a-zA-Z]://grayjay\\.internal/refPager");
        private static ConcurrentDictionary<string, WeakReference<RefPager<PlatformContent>>> _refPagers = new ConcurrentDictionary<string, WeakReference<RefPager<PlatformContent>>>(); 
        public static void RegisterRefPager(RefPager<PlatformContent> refPager)
        {
            _refPagers.TryAdd(refPager.ID, new WeakReference<RefPager<PlatformContent>>(refPager));
        }


        static StatePlatform()
        {
            UpdateAvailableClients(true).Wait();

            OnSourcesAvailableChanged += (asd) =>
            {
                GrayjayServer.Instance?.WebSocket?.Broadcast("", "PluginAvailable", "");
            };
            OnSourceEnabled += (client) =>
            {
                GrayjayServer.Instance?.WebSocket?.Broadcast(client.ID, "PluginEnabled", client.ID);
                StateUI.Toast($"Source [{client.Config.Name}] enabled");
            };
            OnSourceDisabled += (client) =>
            {
                GrayjayServer.Instance?.WebSocket?.Broadcast(client.ID, "PluginDisabled", client.ID);
                StateUI.Toast($"Source [{client.Config.Name}] disabled");
            };
            StatePlugins.OnPluginSettingsChanged += async (plugin, needReload) =>
            {
                var client = GetClient(plugin.Config.ID);
                Logger.i(nameof(StatePlatform), $"Client [{plugin.Config.Name}] settings changed, reloading");
                client.UpdateDescriptor(plugin);
                await ReloadClient(plugin.Config.ID, needReload);
            };
            StatePlugins.OnPluginAuthChanged += async (plugin) =>
            {
                var client = GetClient(plugin.Config.ID);
                Logger.i(nameof(StatePlatform), $"Client [{plugin.Config.Name}] auth changed, reloading");
                await ReloadClient(plugin.Config.ID, true);
            };
            StatePlugins.OnPluginCaptchaChanged += async (plugin) =>
            {
                var client = GetClient(plugin.Config.ID);
                Logger.i(nameof(StatePlatform), $"Client [{plugin.Config.Name}] captcha changed, reloading");
                await ReloadClient(plugin.Config.ID, true);
            };
            PlatformNestedMedia.SetPluginResolver((url) =>
            {
                var contentPlugin = GetContentClientOrNull(url);
                if (contentPlugin != null)
                    return (contentPlugin.ID, contentPlugin.Config.Name, contentPlugin.Config.AbsoluteIconUrl);
                else
                    return (null, null, null);
            });
        }

        public static void InjectPlugin(GrayjayPlugin plugin)
        {
            lock(_clientsLock)
            {
                if (!_availableClients.Any(x=>x.Config.ID == plugin.Config.ID))
                    _availableClients.Add(plugin);
            }
        }
        public static string InjectDevPlugin(PluginConfig config, string source)
        {
            string devId = StateDeveloper.DEV_ID;
            config.ID = devId;

            lock (_clientsLock)
            {
                var enabledExisting = _enabledClients.Where(x => x is GrayjayPlugin).ToList();
                var isEnabled = enabledExisting.Any();
                foreach (var enabled in enabledExisting)
                    enabled.Disable();


                _enabledClients.RemoveAll(x => x is DevGrayjayPlugin);
                _availableClients.RemoveAll(x => x is DevGrayjayPlugin);


                DevGrayjayPlugin newClient = new DevGrayjayPlugin(new PluginDescriptor(config), source);
                StatePlugins.RegisterDescriptor(newClient.Descriptor);
                devId = newClient.DevID;
                try
                {
                    StateDeveloper.Instance.InitializeDev(devId);
                    if(isEnabled)
                    {
                        _enabledClients.Add(newClient);
                        newClient.Initialize();
                    }
                    _availableClients.Add(newClient);
                }
                catch(Exception ex)
                {
                    Logger.e(nameof(StatePlatform), $"Failed to initialize DevPlugin: {ex.Message}", ex);
                    StateDeveloper.Instance.LogDevException(devId, $"Failed to initialize due to: {ex.Message}");
                }
            }
            OnDevSourceChanged?.Invoke();
            return devId;
        }


        public static IPlatformContentDetails GetContentDetails(string url)
        {
            if (REGEX_PAGER_REF.IsMatch(url))
            {
                var uri = new Uri(url);
                var query = HttpUtility.ParseQueryString(uri.Query);
                var queryKeys = query.AllKeys;
                if(!queryKeys.Contains("pagerId") || !queryKeys.Contains("itemId"))
                    throw new ArgumentException("Url misses pagerId or itemId");

                bool found = _refPagers.TryGetValue(query["pagerId"], out var pagerRef);
                if (!found)
                    throw new ArgumentException("RefPager does not exists");
                bool available = pagerRef.TryGetTarget(out var pager);
                if (!available)
                    throw new ArgumentException("RefPager no longer available");
                var item = pager.FindRef(query["itemId"], true);
                if (!(item.Object is IPlatformContentDetails))
                    throw new ArgumentException("RefItem is not a detail object");
                return item.Object as IPlatformContentDetails;
            }

            return GetContentClient(url)
                .FromPool(_mainClientPool)
                .GetContentDetails(url);
        }

        public static List<Chapter> GetContentChapters(string url)
        {
            var baseClient = GetContentClientOrNull(url);
            if (baseClient == null)
                return null;
            var client = baseClient.FromPool(_mainClientPool);
            return client.GetContentChapters(url);
        }

        public static LiveChatWindowDescriptor GetLiveChatWindow(string url)
        {
            var baseClient = GetContentClientOrNull(url);
            if (baseClient == null)
                return null;
            return baseClient.GetLiveChatWindow(url);
        }

        public static IPager<PlatformComment> GetComments(string url)
            => GetContentClient(url)
                .FromPool(_mainClientPool)
                .GetComments(url);

        public static IPager<PlatformComment> GetComments(PlatformVideoDetails video)
        {
            var client = GetContentClient(video.Url);
            if(!client.Capabilities.HasGetComments)
                return new EmptyPager<PlatformComment>();
            return client
                .FromPool(_utilityClientPool)
                .GetComments(video.Url);
        }

        public static IPager<PlatformComment> GetSubComments(PlatformComment comment)
            => GetContentClient(comment.ContextUrl)
                .FromPool(_utilityClientPool)
                .GetSubComments(comment);

        private static Dictionary<string, string[]>? GetClientSpecificFilters(ResultCapabilities capabilities, Dictionary<string, string[]>? filters)
        {
            if (filters == null)
                return null;

            foreach (var filter in filters)
            {
                var capabilityFilter = capabilities.Filters.FirstOrDefault(f => f.IDOrName == filter.Key);
                if (capabilityFilter == null)
                    continue;

                
            }

            var mappedFilters = filters.ToDictionary(
                pair => pair.Key,
                pair => pair.Value
                    .Select(v => capabilities.Filters
                        .FirstOrDefault(g => g.IDOrName == pair.Key)?
                        .Filters.FirstOrDefault(f => f.IDOrName == v)?.Value)
                    .Where(value => value != null)
                    .Select(value => value!)
                    .ToArray());

            return mappedFilters;
        }

        public static IPager<PlatformContent> SearchLazy(string query, string? type = null, string? order = null, Dictionary<string, string[]>? filters = null, List<string>? excludeClientIds = null)
        {
            return CreateDistributedLazyPager(
                (client) => excludeClientIds != null ? !excludeClientIds.Contains(client.ID) : true,
                (client) => client.Search(query, type, order, GetClientSpecificFilters(PluginConfigState.FromClient(client).CapabilitiesSearch, filters)),
                (client, task) => new PlatformContentPlaceholder(client.Config)
            );
        }
        public static IPager<PlatformContent> SearchChannelsLazy(string query, List<string>? excludeClientIds = null)
        {
            return CreateDistributedLazyPager(
                (client) => client.Capabilities.HasChannelSearch && (excludeClientIds != null ? !excludeClientIds.Contains(client.ID) : true),
                (client) => client.SearchChannelsAsContent(query),
                (client, task) => new PlatformContentPlaceholder(client.Config)
            );
        }
        public static IPager<PlatformContent> SearchPlaylistsLazy(string query, List<string>? excludeClientIds = null)
        {
            return CreateDistributedLazyPager(
                (client) => 
                    client.Capabilities.HasSearchPlaylists && (excludeClientIds != null ? !excludeClientIds.Contains(client.ID) : true),
                (client) => client.SearchPlaylists(query),
                (client, task) => new PlatformContentPlaceholder(client.Config)
            );
        }

        public static PlatformPlaylistDetails? GetPlaylist(string url)
        {
            var plugin = GetPlaylistClientOrNull(url);
            if (plugin == null) {
                return null;
            }

            if (!plugin.Capabilities.HasGetPlaylist) {
                return null;
            }
            

            return plugin.GetPlaylist(url);
        }

        public static List<string> SearchSuggestions(string query)
        {
            var client = _enabledClients.FirstOrDefault(c => c.Capabilities.HasSearchSuggestions);
            if (client == null)
                return [];
            return client.SearchSuggestions(query);
        }

        public static IPager<PlatformContent> GetHome()
        {
            List<string> clientIdsOngoing = new List<string>();
            List<GrayjayPlugin> clients = GetEnabledClients().Where(x => true).ToList();

            var pages = clients.AsParallel()
                .Select(client =>
                {
                    lock (clientIdsOngoing)
                        clientIdsOngoing.Add(client.Config.ID);
                    return client.GetHome();
                }).ToDictionary(x => x, y => 1f);

            var pager = new MultiDistributionPager<PlatformContent>(pages, true);
            pager.Initialize();
            return pager;
        }
        public static IPager<PlatformContent> GetHomeLazy()
        {
            return CreateDistributedLazyPager(
                (client) => client.Descriptor.AppSettings.TabEnabled.EnableHome,
                (client) => {
                    try
                    {
                        for (int i = 0; i < 2; i++)
                        {
                            try
                            {
                                var result = client.GetHome();
                                return result;
                            }
                            catch(Exception ex)
                            {
                                if (i == 1 || ex is ScriptCriticalException || ex is ScriptCaptchaRequiredException)
                                    throw;
                            }
                        }
                    }
                    catch(Exception ex)
                    {
                        Logger.e(nameof(StatePlatform), $"Home failed for plugin [{client.Config.Name}]: " + ex.Message, ex);
                        var errorContentType = new PlatformContentPlaceholder(client.Config, ex);
                        PlatformContentPlaceholder[] cps = new PlatformContentPlaceholder[5];
                        for (int i = 0; i < 5; i++)
                            cps[i] = errorContentType;
                        return new AdhocPager<PlatformContent>((i) =>
                        {
                            return cps.ToArray();
                        }, cps.ToArray());
                    }
                    throw new InvalidProgramException();
                },
                (client, task) => new PlatformContentPlaceholder(client.Config)
            );
        }


        public static PlatformChannel GetChannel(string url)
            => GetChannelClient(url)
                .FromPool(_mainClientPool)
                .GetChannel(url);

        public static IPager<PlatformContent> GetChannelContent(string url)
        {
            var client = GetChannelClientOrNull(url);
            if (url == null)
                throw new ArgumentException($"No plugin to load channel [{url}]");
            var results =  GetChannelContent(client, url, false, 0);
            return results;
        }
        public static IPager<PlatformContent> SearchChannelContent(string url, string query)
        {
            var client = GetChannelClientOrNull(url);
            if (url == null)
                throw new ArgumentException($"No plugin to search channel [{url}]");
            return client.FromPool(_channelClientPool).SearchChannelContents(url, query);
        }
        public static IPager<PlatformContent> GetChannelContent(GrayjayPlugin client, string url = null, string type = null, string order = null)
        {
            return client.FromPool(_channelClientPool)
                .GetChannelContents(url, type, order);
        }
        public static IPager<PlatformContent> GetChannelContent(GrayjayPlugin baseClient, string channelUrl, bool isSubscriptionOptimized = false, int usePooledClients = 0)
        {
            var clientCapabilities = baseClient.GetChannelCapabilities();
            GrayjayPlugin client;

            if (usePooledClients > 1)
                client = baseClient.FromPool(_channelClientPool);
            else
                client = baseClient;

            DateTime? lastStream = null;

            IPager<PlatformContent> pagerResult;

            if (!clientCapabilities.HasType(ResultCapabilities.TYPE_MIXED) && (
                    clientCapabilities.HasType(ResultCapabilities.TYPE_VIDEOS) || 
                    clientCapabilities.HasType(ResultCapabilities.TYPE_STREAMS) || 
                    clientCapabilities.HasType(ResultCapabilities.TYPE_LIVE) || 
                    clientCapabilities.HasType(ResultCapabilities.TYPE_POSTS)
                ))
            {
                var toQuery = new List<string>();
                if (clientCapabilities.HasType(ResultCapabilities.TYPE_VIDEOS))
                    toQuery.Add(ResultCapabilities.TYPE_VIDEOS);
                if (clientCapabilities.HasType(ResultCapabilities.TYPE_STREAMS))
                    toQuery.Add(ResultCapabilities.TYPE_STREAMS);
                if (clientCapabilities.HasType(ResultCapabilities.TYPE_LIVE))
                    toQuery.Add(ResultCapabilities.TYPE_LIVE);
                if (clientCapabilities.HasType(ResultCapabilities.TYPE_POSTS))
                    toQuery.Add(ResultCapabilities.TYPE_POSTS);

                if (isSubscriptionOptimized)
                {
                    var optSub = StateSubscriptions.GetSubscription(channelUrl);
                    if (optSub != null)
                    {
                        if (!optSub.ShouldFetchStreams())
                        {
                            Logger.i(TAG, $"Subscription [{optSub.Channel.Name}:{channelUrl}] Last livestream > 7 days, skipping live streams [{(int)DateTime.Now.Subtract(optSub.LastLiveStream).TotalDays} days ago]");
                            toQuery.Remove(ResultCapabilities.TYPE_LIVE);
                        }
                    }
                }

                var pagers = toQuery
                    .AsParallel()
                    .Select(type =>
                    {
                        var results = client.GetChannelContents(channelUrl, type, ResultCapabilities.ORDER_CHONOLOGICAL);

                        switch (type)
                        {
                            case ResultCapabilities.TYPE_STREAMS:
                                var streamResults = results.GetResults();
                                lastStream = streamResults.Length == 0 ? DateTime.MinValue : streamResults.First().DateTime;
                                break;
                        }

                        return results;
                    })
                    .ToList();

                var pager = new MultiChronoContentPager(pagers.ToList());
                pager.Initialize();
                pagerResult = pager;
            }
            else if(!clientCapabilities.HasType(ResultCapabilities.TYPE_MIXED) && clientCapabilities.HasType(ResultCapabilities.TYPE_VIDEOS))
            {
                pagerResult = client.GetChannelContents(channelUrl, ResultCapabilities.TYPE_VIDEOS, ResultCapabilities.ORDER_CHONOLOGICAL);
            }
            else
            {
                pagerResult = client.GetChannelContents(channelUrl, ResultCapabilities.TYPE_MIXED, ResultCapabilities.ORDER_CHONOLOGICAL);
            }

            var sub = StateSubscriptions.GetSubscription(channelUrl);

            if (sub != null)
            {
                var hasChanges = false;
                var lastVideo = pagerResult.GetResults().FirstOrDefault();

                var now = DateTime.Now;

                if (lastVideo?.DateTime != null && (int)now.Subtract(sub.LastVideo).TotalDays != (int)now.Subtract(lastVideo.DateTime).TotalDays)
                {
                    Logger.i(TAG, $"Subscription [{channelUrl}] has new last video date [{(int)now.Subtract(lastVideo.DateTime).TotalDays} Days]");
                    sub.LastVideo = lastVideo.DateTime;
                    hasChanges = true;
                }

                if (lastStream != null && (int)now.Subtract(sub.LastLiveStream).TotalDays != (int)now.Subtract(lastStream.Value).TotalDays)
                {
                    Logger.i(TAG, $"Subscription [{channelUrl}] has new last stream date [{(int)now.Subtract(lastStream.Value).TotalDays} Days]");
                    sub.LastLiveStream = lastStream ?? sub.LastLiveStream;
                    hasChanges = true;
                }
                var firstPage = pagerResult.GetResults().Where(video => video.DateTime != null && video.DateTime < now).ToList();

                if (firstPage.Count > 0)
                {
                    var newestVideoDays = now.Subtract(firstPage[0].DateTime).TotalDays;
                    var diffs = new List<int>();

                    for (var i = firstPage.Count - 1; i >= 1; i--)
                    {
                        var currentVideoDays = now.Subtract(firstPage[i].DateTime).TotalDays;
                        var nextVideoDays = now.Subtract(firstPage[i - 1].DateTime).TotalDays;

                        if (currentVideoDays != null && nextVideoDays != null)
                        {
                            var diff = nextVideoDays - currentVideoDays;
                            diffs.Add((int)diff);
                        }
                    }

                    var averageDiff = diffs.Count > 0 ? Math.Max(newestVideoDays, (int)diffs.Average()) : newestVideoDays;

                    if (sub.UploadInterval != averageDiff)
                    {
                        Logger.i(TAG, $"Subscription [{channelUrl}] has new upload interval [{averageDiff} Days]");
                        sub.UploadInterval = (int)averageDiff;
                        hasChanges = true;
                    }
                }

                //if (hasChanges)
                //    sub.Save();
            }

            return pagerResult;
        }

        public static List<PlatformContent> PeekChannelContents(GrayjayPlugin baseClient, string channelUrl, string type)
        {
            return baseClient.FromPool(_channelClientPool)
                .PeekChannelContents(channelUrl, type);
        }

        public static List<string> GetUserSubscriptions(string pluginId)
        {
            var plugin = GetEnabledClient(pluginId);
            if (plugin == null)
                return new List<string>();
            return plugin.GetUserSubscriptions();
        }
        public static List<string> GetUserPlaylists(string pluginId)
        {
            var plugin = GetEnabledClient(pluginId);
            if (plugin == null)
                return new List<string>();
            return plugin.GetUserPlaylists();
        }

        public static PlaybackTracker GetPlaybackTracker(string url)
        {
            var baseClient = GetContentClientOrNull(url);
            if (baseClient == null)
                return null;
            return baseClient.FromPool(_trackerClientPool).GetPlaybackTracker(url);
        }



        //Standardization
        private static MultiRefreshPager<T> CreateDistributedLazyPager<T>(Func<GrayjayPlugin, bool> clientCondition, Func<GrayjayPlugin, IPager<T>> action, Func<GrayjayPlugin, Task, T> placeholderCreator)
        {
            List<string> clientIdsOngoing = new List<string>();
            List<GrayjayPlugin> clients = GetEnabledClients().Where(x => clientCondition(x)).ToList();

            var pageTasks = clients
                .Select(client =>
                {
                    return (client, Task.Run(() =>
                    {
                        lock (clientIdsOngoing)
                            clientIdsOngoing.Add(client.Config.ID);
                        return action(client);
                    }));
                }).ToList();

            //Task.WaitAny(pageTasks.Select(x => x.Item2).ToArray());

            var finishedPagers = pageTasks.Where(x => x.Item2.IsCompleted).ToList();
            var unfinishedPagers = pageTasks.Where(x => !finishedPagers.Contains(x)).ToList();

            var pager = new RefreshDistributionContentPager<T>(
                finishedPagers.Select(x => x.Item2.Result),
                unfinishedPagers.Select(x => x.Item2),
                unfinishedPagers.Select(x => new PlaceholderPager<T>(5, () => placeholderCreator(x.client, x.Item2))),
                async (changedPager) => {
                    var result = changedPager.AsPagerResult();
                    Logger.i(TAG, $"Resolving {result.Results.Length} lazy results ({result.PagerID})");
                    await GrayjayServer.Instance.WebSocket.Broadcast(result, "PagerUpdated", changedPager.ID);
                });
            return pager;
        }



        //Manage clients

        public static List<GrayjayPlugin> GetEnabledClients()
        {
            lock (_clientsLock)
            {
                return _enabledClients.ToList();
            }
        }
        public static List<GrayjayPlugin> GetDisabledClients()
        {
            lock (_clientsLock)
            {
                return _availableClients.Where(x=>!_enabledClients.Contains(x)).ToList();
            }
        }
        public static List<GrayjayPlugin> GetAvailableClients()
        {
            lock (_clientsLock)
            {
                return _availableClients.ToList();
            }
        }

        public static GrayjayPlugin GetClient(string id)
        {
            lock(_clientsLock)
                return _availableClients.FirstOrDefault(x=>x.Config.ID == id);
        }
        public static GrayjayPlugin GetEnabledClient(string id)
        {
            lock (_clientsLock)
                return _enabledClients.FirstOrDefault(x => x.Config.ID == id);
        }

        public static DevGrayjayPlugin GetDevClient()
        {
            return (DevGrayjayPlugin)GetClient(StateDeveloper.DEV_ID);
        }

        public static bool IsEnabled(string id)
        {
            return GetEnabledClient(id) != null;
        }

        public static GrayjayPlugin GetContentClientOrNull(string url)
        {
            lock (_clientsLock)
                return _enabledClients.FirstOrDefault(x => x.IsContentDetailsUrl(url));
        }
        public static GrayjayPlugin GetContentClient(string url) 
            => GetContentClientOrNull(url) ?? throw new NoPlatformClientException($"No client enabled that supports this content url ({url})");

        public static GrayjayPlugin GetChannelClientOrNull(string url)
        {
            lock (_clientsLock)
                return _enabledClients.FirstOrDefault(x => x.IsChannelUrl(url));
        }
        public static GrayjayPlugin GetPlaylistClientOrNull(string url)
        {
            lock (_clientsLock)
                return _enabledClients.FirstOrDefault(x => x.IsPlaylistUrl(url));
        }
        public static GrayjayPlugin GetChannelClient(string url)
            => GetChannelClientOrNull(url) ?? throw new NoPlatformClientException($"No client enabled that supports this content url ({url})");
        public static bool HasChannelClientFor(string url) => GetChannelClientOrNull(url) != null;

        public static async Task ReloadClient(string id, bool needReload = false)
        {
            if (needReload)
                await UpdateAvailableClient(id);
            else
            {
                if (IsEnabled(id))
                {
                    await DisableClient(id);
                    await EnableClient(id);
                }
            }
        }
        public static async Task EnableClient(string id, bool allowThrow = false)
        {
            Exception ex = null;
            await SelectClients((eId, exception) =>
            {
                if (id == eId)
                    ex = exception;
            }, GetEnabledClients().Select(x => x.ID).Concat(new[] { id }).Distinct().ToArray());
            if (ex != null)
                throw ex;
        }
        public static async Task EnableClients(string[] ids)
        {
            await SelectClients(GetEnabledClients().Select(x => x.ID).Concat(ids).Distinct().ToArray());
        }
        public static async Task DisableClient(string id)
        {
            await SelectClients(GetEnabledClients().Select(x => x.ID).Where(x=>x != id).Distinct().ToArray());
        }
        public static async Task SelectClients(Action<string, Exception> onEx, params string[] ids)
        {
            List<GrayjayPlugin> removed;
            lock (_clientsLock)
            {
                removed = _enabledClients.ToList();
                _enabledClients.Clear();

                foreach (var id in ids)
                {
                    var client = GetClient(id);
                    try
                    {
                        bool isNew = false;
                        if (removed.RemoveAll(it => it == client) == 0)
                        {
                            client.Initialize();
                            isNew = true;
                        }

                        _enabledClients.Add(client);
                        if (isNew)
                            OnSourceEnabled?.Invoke(client);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Plugin [{client.Config.Name}] failed to initialize due to: {ex.Message}\n{ex.StackTrace}");
                        onEx?.Invoke(id, ex);
                    }
                }

                _enabledClientsPersistent.Save(ids.ToArray());

                foreach (var oldClient in removed)
                {
                    oldClient.Disable();
                    OnSourceDisabled?.Invoke(oldClient);
                }
            }
            StateWebsocket.EnabledClientsChanged();
        }
        public static async Task SelectClients(params string[] ids)
        {
            await SelectClients(null, ids);
        }

        public static async Task UpdateAvailableClient(string id)
        {
            lock (_clientsLock)
            {
                var devPlugin = id == StateDeveloper.DEV_ID ? GetDevClient() : null;



                var enabledClient = _enabledClients.FirstOrDefault(X => X.ID == id);
                if (enabledClient != null)
                {
                    enabledClient.Disable();
                    _enabledClients.Remove(enabledClient);
                    OnSourceDisabled?.Invoke(enabledClient);
                }
                var availableClient = _availableClients.FirstOrDefault(x => x.ID == id);
                _availableClients.Remove(availableClient);

                var plugin = (id != StateDeveloper.DEV_ID) ? StatePlugins.GetPlugin(id) : devPlugin.Descriptor;
                var newClient = (id != StateDeveloper.DEV_ID) ? new GrayjayPlugin(plugin, StatePlugins.GetPluginScript(plugin.Config.ID)) : new DevGrayjayPlugin(plugin, (devPlugin?.DevScript));

                newClient.OnLog += (a, b) => Logger.i($"Plugin [{a.Name}]", b);
                newClient.OnScriptException += (config, ex) =>
                {
                    if (ex is ScriptCaptchaRequiredException capEx)
                    {
                        Console.WriteLine($"Captcha required: " + capEx.Message + "\n" + capEx.Url + "\n" + "Has Body: " + (capEx.Body != null).ToString());
                        StateApp.HandleCaptchaException(config, capEx);
                    }
                };
                _availableClients.Add(newClient);

                if (enabledClient != null)
                {
                    var client = GetClient(id);
                    try
                    {
                        bool isNew = false;
                        client.Initialize();

                        _enabledClients.Add(client);
                        if (isNew)
                            OnSourceEnabled?.Invoke(client);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Plugin [{client.Config.Name}] failed to initialize due to: {ex.Message}\n{ex.StackTrace}");
                    }
                }
            }
        }

        public static async Task UpdateAvailableClients(bool reloadPlugins = false)
        {
            if (reloadPlugins)
            {
                StatePlugins.ReloadPluginFile();
            }
            string[] enabled;
            lock (_clientsLock)
            {
                foreach (var e in _enabledClients)
                {
                    e.Disable();
                    OnSourceDisabled?.Invoke(e);
                }

                _enabledClients.Clear();
                _availableClients.Clear();
                _icons.Clear();

                StatePlugins.UpdateEmbeddedPlugins();
                StatePlugins.InstallMissingEmbeddedPlugins();

                foreach (var plugin in StatePlugins.GetPlugins())
                {
                    _icons[plugin.Config.ID] = StatePlugins.GetPluginIconOrNull(plugin.Config.ID) ??
                                              (plugin.Config.AbsoluteIconUrl);

                    //TODO: script
                    var client = new GrayjayPlugin(plugin, StatePlugins.GetPluginScript(plugin.Config.ID));
                    client.OnLog += (a, b) => Logger.i($"Plugin [{a.Name}]", b);
                    client.OnScriptException += (config, ex) =>
                    {
                        if(ex is ScriptCaptchaRequiredException capEx)
                        {
                            Console.WriteLine($"Captcha required: " + capEx.Message + "\n" + capEx.Url + "\n" + "Has Body: " + (capEx.Body != null).ToString());
                            StateApp.HandleCaptchaException(config, capEx);
                        }
                    };
                    //TODO: Captcha
                    _availableClients.Add(client);
                }

                if (_availableClients.DistinctBy(it => it.Config.ID).Count() < _availableClients.Count)
                {
                    throw new InvalidOperationException("Attempted to add 2 clients with the same ID");
                }

                enabled = _enabledClientsPersistent.GetCopy()
                    .Where(x => _availableClients.Any(y => y.ID == x))
                    .ToArray();
                    
                if(enabled.Length == 0)
                    StatePlugins.GetEmbeddedSourcesDefault()
                        .Where(id => _availableClients.Any(it => it.Config.ID == id))
                        .ToArray();

            }
            await SelectClients(enabled);

            _didStartup = true;
            OnSourcesAvailableChanged?.Invoke(false);
        }


        public static async Task WaitForStartup()
        {
            if (_didStartup)
                return;
            var waitTask = new TaskCompletionSource<bool>();
            Action<bool> handler = null;
            handler = (val) =>
            {
                OnSourcesAvailableChanged -= handler;
                waitTask.SetResult(true);
            };
            OnSourcesAvailableChanged += handler;
            await waitTask.Task;
        }
    }
}
