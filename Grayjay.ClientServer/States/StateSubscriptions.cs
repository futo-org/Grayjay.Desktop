
using Grayjay.ClientServer;
using Grayjay.ClientServer.Models.Subscriptions;
using Grayjay.ClientServer.Settings;
using Grayjay.ClientServer.States;
using Grayjay.ClientServer.Store;
using Grayjay.ClientServer.Subscriptions;
using Grayjay.ClientServer.Subscriptions.Algorithms;
using Grayjay.ClientServer.Sync;
using Grayjay.ClientServer.Sync.Models;
using Grayjay.ClientServer.Threading;
using Grayjay.Engine;
using Grayjay.Engine.Models.Channel;
using Grayjay.Engine.Models.Detail;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Models.Video.Sources;
using Grayjay.Engine.Pagers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grayjay.Desktop.POC.Port.States
{
    public static class StateSubscriptions
    {
        private class SubscriptionReconstructionStore : ReconstructStore<Subscription>
        {
            public override string ToReconstruction(Subscription obj) => obj.Channel.Url;
            public override Subscription ToObject(string id, string backup, Builder builder, StateBackup.ImportCache cache = null) =>
                new Subscription(cache?.Channels?.FirstOrDefault(x => x.IsSameUrl(backup)) ?? StatePlatform.GetChannel(backup));
        }
        private static ManagedStore<Subscription> _subscriptions = new ManagedStore<Subscription>("subscriptions")
            .WithUnique(x => x.Channel.Url)
            .WithRestore<SubscriptionReconstructionStore>()
            .Load();
        private static ManagedStore<Subscription> _subscriptionsOthers = new ManagedStore<Subscription>("subscriptions_others")
            .WithUnique(x => x.Channel.Url)
            .Load();

        private static ManagedStore<SubscriptionGroup> _subscriptionGroups = new ManagedStore<SubscriptionGroup>("subscription_groups")
            .WithUnique(x => x.ID)
            .Load();

        private static DictionaryStore<string, long> _subscriptionsRemoved = new DictionaryStore<string, long>("subscriptions_removed", new Dictionary<string, long>())
            .Load();
        private static DictionaryStore<string, long> _groupsRemoved = new DictionaryStore<string, long>("groups_removed", new Dictionary<string, long>())
            .Load();

        private static ChannelFeed _global = new ChannelFeed();
        private static Dictionary<string, ChannelFeed> _feeds = new Dictionary<string, ChannelFeed>();

        private static ManagedThreadPool _threadPool = new ManagedThreadPool(GrayjaySettings.Instance.Subscriptions.SubscriptionConcurrency);

        public static event Action<List<Subscription>, bool> OnSubscriptionsChanged;

        static StateSubscriptions()
        {
            _global.OnUpdateProgress += (int progress, int total) => GrayjayServer.Instance?.WebSocket?.Broadcast(new SubscriptionProgress(progress, total), "subProgress", "global");

            OnSubscriptionsChanged += (subs, n) =>
            {
                StateWebsocket.SubscriptionsChanged();
            };
        }

        public static List<IManagedStore> ToMigrateCheck()
        {
            return new List<IManagedStore>()
            {
                _subscriptions
            };
        }
        public static ManagedStore<Subscription> GetUnderlyingSubscriptionsStore() => _subscriptions;


        public static ChannelFeed GetFeed(string id, bool create = false)
        {
            lock(_feeds)
            {
                if (!_feeds.ContainsKey(id))
                {
                    if (create)
                    {
                        var group = _subscriptionGroups.FindObject(x => x.ID == id);
                        if (group == null)
                            return null;
                        _feeds[id] = new ChannelFeed(group);
                        _feeds[id].OnUpdateProgress += (int progress, int total) => GrayjayServer.Instance?.WebSocket?.Broadcast(new SubscriptionProgress(progress, total), "subProgress", id);
                    }
                    else
                        return null;
                }
                return _feeds[id];
            }
        }

        public static Task<IPager<PlatformContent>> GetSubscriptionFeed(string id, bool updated)
        {
            var feed = GetFeed(id, true);
            var result = new TaskCompletionSource<IPager<PlatformContent>>();

            lock (feed.LockObject)
            {
                if (feed.Feed != null && !updated)
                {
                    Logger.i(nameof(StateSubscriptions), "Subscriptions got feed preloaded");
                    result.SetResult(feed.Feed.GetWindow());
                }
                else
                {
                    var loadIndex = _loadIndex++;
                    Logger.i(nameof(StateSubscriptions), $"[{loadIndex}] Starting await update");
                    feed.OnUpdatedOnce += (exception) =>
                    {
                        Logger.i(nameof(StateSubscriptions), $"[{loadIndex}] Subscriptions got feed after update");
                        if (exception != null)
                            result.SetException(exception);
                        else if (feed.Feed != null)
                            result.SetResult(feed.Feed.GetWindow());
                        else
                            throw new InvalidOperationException("No subscription pager after change? Illegal null set on global subscriptions");
                    };
                    UpdateSubscriptionFeed(!updated, null, id);
                }
            }

            return result.Task;
        }

        public static async Task<(IPager<PlatformContent>, List<Exception>)> getSubscriptionsFeedWithExceptions(bool allowFailure, bool withCacheFallback, List<string> urls, Action<int,int> onProgress, Action<Subscription, PlatformContent> onNewCacheHit = null)
        {
            var algo = SubscriptionFetchAlgorithm.GetAlgorithm(SubscriptionFetchAlgorithms.Smart, allowFailure, withCacheFallback, _threadPool);
            algo.OnNewCacheHit += onNewCacheHit;
            algo.OnProgress += onProgress;

            var subs = GetSubscriptions();
            var emulatedSubs = (urls != null) ? subs.Where(x=>urls.Any(y=>x.isChannel(y))).ToList() : subs;

            var subUrls = emulatedSubs.ToDictionary(x => x, y => new List<string>() { y.Channel.Url });
            var result = algo.GetSubscriptions(subUrls);
            return (result.Pager, result.Exceptions);
        }

        public static void UpdateSubscriptionFeed(bool onlyIfNull, Action<int, int> onProgress, string feedId = null)
        {
            var feed = (feedId == null) ? _global : GetFeed(feedId);
            Logger.v(nameof(StateSubscriptions), "updateSubscriptionFeed");
            Task.Run(async () =>
            {
                lock(feed.LockObject)
                {
                    if(feed.IsGlobalUpdating || (onlyIfNull && feed.Feed != null))
                    {
                        Logger.i(nameof(StateSubscriptions), "Already updating subscriptions or not required");
                        return;
                    }
                    feed.IsGlobalUpdating = true;
                }
                try
                {
                    var (subsPager, exceptions) = await getSubscriptionsFeedWithExceptions(true, true, (feed.Group != null) ? feed.Group.Urls : null, (progress, total) =>
                    {
                        feed.SetProgress(progress, total);
                        onProgress?.Invoke(progress, total);
                    });
                    feed.SetExceptions(exceptions);
                    feed.SetFeed(subsPager);
                }
                catch(Exception ex)
                {
                    feed.SetFeedException(ex);
                }
                finally
                {
                    feed.IsGlobalUpdating = false;
                }
            });
        }

        private static int _loadIndex = 0;
        public static ChannelFeed GetGlobalFeed() => _global;
        public static Task<IPager<PlatformContent>> GetGlobalSubscriptionFeed(bool updated)//, SubscriptionGroup group = null)
        {
            var feed = _global;
            var result = new TaskCompletionSource<IPager<PlatformContent>>();

            lock(feed.LockObject)
            {
                if(feed.Feed != null && !updated)
                {
                    Logger.i(nameof(StateSubscriptions), "Subscriptions got feed preloaded");
                    var newSubscriptions = StateSubscriptions.GetSubscriptions()
                        .Where(x => x.CreationTime > feed.LastUpdate)
                        .ToList();

                    if(newSubscriptions.Count > 0 && newSubscriptions.Count < 5)
                    {
                        //Fetch only new feeds of new subscriptions, then combine with the current feed
                        var intermediateResults = getSubscriptionsFeedWithExceptions(true, false, newSubscriptions
                            .Select(x => x.Channel.Url)
                            .ToList(), (progress, total) =>
                            {
                                feed.SetProgress(progress, total);
                            }).Result;
                        var newFeed = feed.Feed.GetWindow()
                            .CombineOrdered(intermediateResults.Item1,
                                (a, b) => a.DateTime > b.DateTime, true, 30);
                        newFeed.Initialize();
                        feed.SetFeed(newFeed);
                    }

                    result.SetResult(feed.Feed.GetWindow());
                }
                else
                {
                    var loadIndex = _loadIndex++;
                    Logger.i(nameof(StateSubscriptions), $"[{loadIndex}] Starting await update");
                    feed.OnUpdatedOnce += (exception) =>
                    {
                        Logger.i(nameof(StateSubscriptions), $"[{loadIndex}] Subscriptions got feed after update");
                        if (exception != null)
                            result.SetException(exception);
                        else if (feed.Feed != null)
                            result.SetResult(feed.Feed.GetWindow());
                        else
                            throw new InvalidOperationException("No subscription pager after change? Illegal null set on global subscriptions");
                    };
                    UpdateSubscriptionFeed(!updated, null);
                }
            }

            return result.Task;
        }





        public static bool IsSubscribed(PlatformChannel channel)
        {
            var urls = channel.UrlAlternatives.ToList().Concat(new string[] { channel.Url });
            return IsSubscribed(urls);
        }
        public static bool IsSubscribed(string url)
        {
            return _subscriptions.HasObject(x => x.Channel.Url == url);
        }
        public static bool IsSubscribed(IEnumerable<string> urls)
        {
            if (!urls.Any())
                return false;
            return _subscriptions.HasObject(x => urls.Contains(x.Channel.Url));
        }



        public static List<Subscription> GetSubscriptions()
        {
            return _subscriptions.GetObjects();
        }
        public static Dictionary<string, long> GetSubscriptionRemovals()
        {
            return _subscriptionsRemoved.All();
        }
        public static Dictionary<string, long> GetSubscriptionGroupRemovals()
        {
            return _groupsRemoved.All();
        }

        public static Subscription GetSubscription(string channelUrl)
        {
            if (channelUrl == null)
                return null;
            return _subscriptions.FindObject(x => x.isChannel(channelUrl));
        }
        public static Subscription GetSubscriptionOther(string url)
        {
            return _subscriptionsOthers.FindObject(x => x.isChannel(url));
        }

        public static Subscription AddSubscription(PlatformChannel channel, DateTime? creationDate = null, bool isUserInteraction = false)
        {
            var subObj = new Subscription(channel);
            if(creationDate != null)
                subObj.CreationTime = creationDate.Value;
            _subscriptions.Save(subObj);
            OnSubscriptionsChanged?.Invoke(GetSubscriptions(), true);

            if (isUserInteraction)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await StateSync.Instance.BroadcastJsonAsync(GJSyncOpcodes.SyncSubscriptions, new SyncSubscriptionsPackage()
                        {
                            Subscriptions = new List<Subscription>()
                            {
                            subObj
                            },
                            SubscriptionRemovals = new Dictionary<string, long>()
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.w(nameof(StateSubscriptions), "Failed to send subs changes to sync clients", ex);
                    }
                });
            }

            return subObj;
        }

        public static DateTime GetSubscriptionRemovalTime(string url)
        {
            return DateTimeOffset.FromUnixTimeSeconds(_subscriptionsRemoved.GetValue(url, 0)).DateTime;
        }
        public static List<Subscription> ApplySubscriptionRemovals(Dictionary<string, long> removals)
        {
            List<Subscription> removed = new List<Subscription>();
            var subs = GetSubscriptions().ToDictionary(x => x.Channel.Url.ToLower(), y => y);
            foreach(var removal in removals)
            {
                if (subs.ContainsKey(removal.Key.ToLower()))
                {
                    var sub = subs[removal.Key.ToLower()];
                    var datetime = (DateTime)DateTimeOffset.FromUnixTimeSeconds(removal.Value).DateTime;
                    if(sub.CreationTime < datetime)
                    {
                        RemoveSubscription(sub.Channel.Url);
                        removed.Add(sub);
                    }
                }
            }
            _subscriptionsRemoved.SetAllAndSave(removals, (key, value, oldValue) => value > oldValue);
            return removed;
        }

        public static Subscription RemoveSubscription(string url, bool isUserAction = false)
        {
            var sub = GetSubscription(url);
            if(sub != null)
            {
                _subscriptions.Delete(sub);
                OnSubscriptionsChanged?.Invoke(GetSubscriptions(), false);
                if (isUserAction)
                {
                    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    _subscriptionsRemoved.SetAndSave(sub.Channel.Url, now);

                    Task.Run(async () =>
                    {
                        try
                        {
                            await StateSync.Instance.BroadcastJsonAsync(GJSyncOpcodes.SyncSubscriptions, new SyncSubscriptionsPackage()
                            {
                                Subscriptions = new List<Subscription>(){},
                                SubscriptionRemovals = new Dictionary<string, long>()
                                {
                                    { sub.Channel.Url, now }
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            Logger.w(nameof(StateSubscriptions), "Failed to send subs changes to sync clients", ex);
                        }
                    });
                }
            }
            return sub;
        }
        public static void RemoveAllSubscriptions()
        {
            var subs = GetSubscriptions();
            foreach (var sub in subs)
                RemoveSubscription(sub.Channel.Url);
        }

        public static void SaveSubscription(Subscription subscription)
        {
            _subscriptions.Save(subscription, false, true);
        }
        public static void SaveSubscriptionAsync(Subscription subscription)
        {
            _subscriptions.SaveAsync(subscription, false, true);
        }
        public static void SaveSubscriptionOther(Subscription subscription)
        {
            _subscriptionsOthers.Save(subscription);
        }
        public static void SaveSubscriptionOtherAsync(Subscription subscription)
        {
            _subscriptionsOthers.SaveAsync(subscription);
        }


        //SubGroups
        public static List<SubscriptionGroup> GetGroups()
        {
            return _subscriptionGroups.GetObjects();
        }
        public static SubscriptionGroup GetGroup(string id)
        {
            return _subscriptionGroups.FindObject(x => x.ID == id);
        }
        public static SubscriptionGroup SaveGroup(SubscriptionGroup group, bool isUserInteraction = true)
        {
            if (string.IsNullOrEmpty(group.ID))
                group.ID = Guid.NewGuid().ToString();
            group.LastChange = DateTime.Now;
            _subscriptionGroups.Save(group);

            if(isUserInteraction)
                Task.Run(async () =>
                {
                    try
                    {
                        await StateSync.Instance.BroadcastJsonAsync(GJSyncOpcodes.SyncSubscriptionGroups, new SyncSubscriptionGroupsPackage()
                        {
                            Groups = new List<SubscriptionGroup>()
                            {
                                group
                            },
                            GroupRemovals = new Dictionary<string, long>()
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.w(nameof(StateSubscriptions), "Failed to send subs changes to sync clients", ex);
                    }
                });

            return group;
        }
        public static SubscriptionGroup DeleteGroup(string id)
        {
            SubscriptionGroup group = GetGroup(id);
            if(group != null)
            {
                _subscriptionGroups.Delete(group);
            }
            return group;
        }

        public static void SyncSubscriptions(SyncSubscriptionsPackage subs)
        {

        }


        public class ChannelFeed
        {
            public object LockObject = new object();

            public SubscriptionGroup Group { get; set; }

            public ReusablePager<PlatformContent> Feed { get; set; }
            public bool IsGlobalUpdating { get; set; }
            public List<Exception> Exceptions { get; set; }

            public int LastProgress { get; set; }
            public int LastTotal { get; set; }

            public DateTime LastUpdate { get; set; } = DateTime.Now;
            
            public event Action<int, int> OnUpdateProgress;
            public event Action OnUpdated;
            public event Action<Exception?> OnUpdatedOnce;
            public event Action<List<Exception>> OnException;

            public ChannelFeed(SubscriptionGroup group = null)
            {
                Group = group;
            }


            public void SetFeed(IPager<PlatformContent> feed)
            {
                lock (LockObject)
                {
                    Feed = feed.AsReusable();
                    LastUpdate = DateTime.Now;
                    OnUpdatedOnce?.Invoke(null);
                    if(OnUpdatedOnce != null)
                        foreach (var ev in OnUpdatedOnce?.GetInvocationList())
                            OnUpdatedOnce -= (Action<Exception?>)ev;
                }
            }
            public void SetFeedException(Exception ex)
            {
                lock (LockObject)
                {
                    OnUpdatedOnce?.Invoke(ex);
                    foreach (var ev in OnUpdatedOnce?.GetInvocationList())
                        OnUpdatedOnce -= (Action<Exception?>)ev;
                }
            }

            public void SetProgress(int progress, int total)
            {
                LastProgress = progress;
                LastTotal = total;
                OnUpdateProgress?.Invoke(LastProgress, LastTotal);
            }
            public void SetExceptions(List<Exception> exceptions)
            {
                Exceptions = exceptions;
                if (exceptions.Any())
                    OnException?.Invoke(exceptions);
            }
        }

        public static void Shutdown()
        {
            _threadPool.Stop();
        }

        public class SubscriptionProgress
        {
            public int Progress { get; set; }
            public int Total { get; set; }

            public SubscriptionProgress(int progress, int total)
            {
                Progress = progress;
                Total = total;
            }
        }
    }
}
