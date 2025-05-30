﻿using Grayjay.ClientServer.Controllers;
using Grayjay.ClientServer.States;
using Grayjay.ClientServer.SubsExchange;
using Grayjay.ClientServer.Threading;
using Grayjay.Desktop.POC;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine;
using Grayjay.Engine.Exceptions;
using Grayjay.Engine.Models.Capabilities;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Pagers;
using System.Diagnostics;
using System.Linq;
using static System.Formats.Asn1.AsnWriter;

using Logger = Grayjay.Desktop.POC.Logger;

namespace Grayjay.ClientServer.Subscriptions.Algorithms
{
    public abstract class SubscriptionsTaskFetchAlgorithm : SubscriptionFetchAlgorithm
    {
        private const string TAG = "SubscriptionsTaskFetchAlgorithm";

        private ManagedThreadPool _pool = null;
        private SubsExchangeClient _subsExchangeClient = null;

        protected SubscriptionsTaskFetchAlgorithm(bool allowFailure = false, bool withCacheFallback = true, ManagedThreadPool pool = null, SubsExchangeClient subsExchangeClient = null) : base(allowFailure, withCacheFallback)
        {
            _pool = pool;
            _subsExchangeClient = subsExchangeClient;
        }

        public override Dictionary<GrayjayPlugin, int> CountRequests(Dictionary<Subscription, List<string>> subs)
        {
            return GetSubscriptionTasks(subs)
                .GroupBy(task => task.Client)
                .ToDictionary(group => group.Key, group => group.Count(task => !task.FromCache));
        }

        public override Result GetSubscriptions(Dictionary<Subscription, List<string>> subs)
        {
            var tasks = GetSubscriptionTasks(subs);

            var tasksGrouped = tasks.GroupBy(task => task.Client);

            Logger.i(TAG, $"Starting Subscriptions Fetch:\n{string.Join("\n", tasksGrouped.Select(group => $"   {group.Key.Config.Name}: {group.Count(task => !task.FromCache)}, Cached({group.Count(task => task.FromCache && !task.FromPeek)}), Peek({group.Count(task => task.FromPeek)})"))}");

            try
            {
                foreach (var clientTasks in tasksGrouped)
                {
                    var clientTaskCount = clientTasks.Count(task => !task.FromCache);
                    var clientCacheCount = clientTasks.Count(task => task.FromCache && !task.FromPeek);
                    var clientPeekCount = clientTasks.Count(task => !task.FromPeek);
                    var limit = clientTasks.Key.GetSubscriptionRateLimit();

                    /*
                    if (clientCacheCount > 0 && clientTaskCount > 0 && limit != null && clientTaskCount >= limit && StateApp.instance.contextOrNull?.let { it is MainActivity && it.isFragmentActive<SubscriptionsFeedFragment>() } == true)
                    {
                        UIDialogs.toast($"[{clientTasks.Key.Name}] only updating {clientTaskCount} most urgent channels (rqs). ({clientCacheCount} cached)");
                    }
                    */
                }
            }
            catch (Exception e)
            {
                Logger.e(TAG, "Error occurred in task.", e);
            }

            var exs = new List<Exception>();

            ExchangeContract contract = null;
            List<SubscriptionTask> providedTasks = null;

            try
            {
                var contractableTasks = tasks.Where(x => !x.FromPeek && !x.FromCache && (x.Type == ResultCapabilities.TYPE_VIDEOS || x.Type == ResultCapabilities.TYPE_MIXED)).ToList();
                contract = (contractableTasks.Count > 10) ? _subsExchangeClient?.RequestContract(contractableTasks.Select(y => new ChannelRequest(y.Url)).ToArray()) : null;
                if ((contract?.Provided?.Length ?? 0) > 0)
                    Logger.i<SubscriptionFetchAlgorithm>($"Received subscription exchange contract (Requires {contract?.Required?.Length}, Provides {contract?.Provided?.Length}), ID: {contract.ID}");
                if (contract != null && contract.Required.Length > 0)
                {
                    providedTasks = new List<SubscriptionTask>();
                    foreach (var task in tasks.ToList())
                    {
                        if (!task.FromCache && !task.FromPeek && contract.Provided.Contains(task.Url))
                        {
                            providedTasks.Add(task);
                            tasks.Remove(task);
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                Logger.e<SubscriptionFetchAlgorithm>("Failed to retrieve SubsExchange contract due to: " + ex.Message, ex);
            }



            var failedPlugins = new List<string>();
            var cachedChannels = new List<string>();
            var forkTasks = ExecuteSubscriptionTasks(tasks, failedPlugins, cachedChannels);

            var taskResults = new List<SubscriptionTaskResult>();

            int resolveCount = 0;
            long resolveTime = 0;
            Stopwatch sw = new Stopwatch();
            sw.Start();
            foreach (var task in forkTasks)
            {
                try
                {
                    task.Wait();
                    var result = task.Result;
                    if (result != null)
                    {
                        if (result.Pager != null)
                        {
                            taskResults.Add(result);
                        }

                        if (result.Exception != null)
                        {
                            var ex = result.Exception;
                            Exception nonRuntimeEx = ex;// findNonRuntimeException(ex);
                            if (nonRuntimeEx != null && (nonRuntimeEx is PluginException || nonRuntimeEx is ChannelException))
                            {
                                exs.Add(nonRuntimeEx);
                            }
                            else
                            {
                                throw ex.InnerException ?? ex;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Exception nonRuntimeEx = ex;// findNonRuntimeException(ex.InnerException);
                    if (nonRuntimeEx != null && (nonRuntimeEx is PluginException || nonRuntimeEx is ChannelException))
                    {
                        exs.Add(nonRuntimeEx);
                    }
                    else
                    {
                        throw ex.InnerException ?? ex;
                    }
                }
            }
            sw.Stop();
            int timeTotal = ((int)sw.ElapsedMilliseconds);

            //Resolve Subscription Exchange
            if(contract != null)
            {
                void Resolve()
                {
                    try
                    {
                        Stopwatch sw = Stopwatch.StartNew();
                        var resolves = taskResults.Where(x => x.Pager != null && (x.Task.Type == ResultCapabilities.TYPE_MIXED || x.Task.Type == ResultCapabilities.TYPE_VIDEOS) && contract.Required.Contains(x.Task.Url))
                            .Select(x => new ChannelResolve(x.Task.Url, x.Pager.GetResults().Where(x => x is PlatformVideo).ToArray()))
                            .ToArray();

                        var resolveRequestStart = DateTime.Now;

                        var resolve = _subsExchangeClient.ResolveContract(contract, resolves);

                        Logger.i<Subscription>($"Subscription Exchange contract resolved request in {DateTime.Now.Subtract(resolveRequestStart).TotalMilliseconds}ms");

                        if (resolve != null)
                        {
                            resolveCount = resolves.Length;
                            StateUI.Toast($"SubsExchange (Res: {resolves.Length}, Prov: {resolve.Length})");
                            foreach (var result in resolve)
                            {
                                var task = providedTasks?.FirstOrDefault(x => x.Url == result.ChannelUrl);
                                if (task != null)
                                {
                                    taskResults.Add(new SubscriptionTaskResult(task, new AdhocPager<PlatformContent>((i) => new PlatformContent[0], result.Content), null));
                                    providedTasks.Remove(task);
                                }
                            }
                        }

                        if (providedTasks != null)
                        {
                            foreach (var task in providedTasks)
                            {
                                taskResults.Add(new SubscriptionTaskResult(task, null, new InvalidOperationException("No data received from exchange")));
                            }
                        }
                        sw.Stop();
                        resolveTime = (long)sw.Elapsed.TotalMilliseconds;
                        Logger.i<SubscriptionFetchAlgorithm>($"Subscription Exchange contract resolved in {resolveTime}ms");
                    }
                    catch(Exception ex)
                    {
                        Logger.e<SubscriptionFetchAlgorithm>("Failed to resolve Subscription Exchange contract due to: " + ex.Message, ex);
                    }
                }
                try
                {
                    if ((providedTasks?.Count ?? 0) == 0)
                    {
                        StateApp.ThreadPool.Run(() =>
                        {
                            Resolve();
                        });
                    }
                    else
                        Resolve();
                }
                catch(Exception ex)
                {
                    Logger.e<SubscriptionFetchAlgorithm>($"Resolve failed", ex);
                }
            }



            Logger.i("StateSubscriptions", $"Subscriptions results in {timeTotal}ms");

            var groupedPagers = taskResults
                .GroupBy(result => result.Task.Sub.Channel.Url)
                .Select(entry =>
                {
                    try
                    {
                        var sub = entry.Any() ? entry.First().Task.Sub : null;
                        var liveTasks = entry.Where(result => !result.Task.FromCache).ToList();
                        var cachedTasks = entry.Where(result => result.Task.FromCache);
                        var failedLiveTasks = liveTasks.Where(x => x.Exception != null);
                        liveTasks = liveTasks.Where(x => x.Exception == null).ToList();
                        if (failedLiveTasks.Any())
                        {
                            foreach (var item in failedLiveTasks)
                                Logger.e<SubscriptionFetchAlgorithm>($"Subscription live task failed for [{item.Task.Sub.Channel.Name}]", item.Exception); 
                            exs.AddRange(failedLiveTasks.Select(x => x.Exception).DistinctBy(x => x.Message).ToList());
                        }
                        var innerLivePager = new MultiChronoContentPager(liveTasks.Select(result => result.Pager), true);
                        innerLivePager.Initialize();
                        var livePager = liveTasks.Any() ? StateCache.CachePagerResults(innerLivePager, it => SetNewCacheHit(sub, it)) : null;

                        IPager<PlatformContent> cachedPager = cachedTasks.Any() ? new MultiChronoContentPager(cachedTasks.Select(result => result.Pager).ToList(), true) : null;
                        if (cachedPager != null && cachedPager is MultiPager<PlatformContent> mcp)
                            mcp.Initialize();


                        if (livePager != null && cachedPager == null)
                            return livePager;
                        else if (livePager == null && cachedPager != null)
                            return cachedPager;
                        else if (cachedPager == null)
                            return new EmptyPager<PlatformContent>();
                        else
                        {
                            var mergedPager = new MultiChronoContentPager(new List<IPager<PlatformContent>>() { livePager, cachedPager }, true);
                            mergedPager.Initialize();
                            return mergedPager;
                        }
                    }
                    catch(Exception ex)
                    {
                        Logger.e<SubscriptionFetchAlgorithm>($"Failed to resolve a pager for subscription task", ex);
                        return new EmptyPager<PlatformContent>();
                    }
                }).ToList();

            var pager = new MultiChronoContentPager(groupedPagers, AllowFailure, 30);
            pager.Initialize();

            return new Result(new DedupContentPager(pager), exs);
        }

        public List<Task<SubscriptionTaskResult>> ExecuteSubscriptionTasks(List<SubscriptionTask> tasks, List<string> failedPlugins, List<string> cachedChannels)
        {
            var forkTasks = new List<Task<SubscriptionTaskResult>>();
            var finished = 0;

            foreach (var task in tasks)
            {
                Task<SubscriptionTaskResult> forkTask = null;

                if (_pool == null)
                {
                    forkTask = new Task<SubscriptionTaskResult>(() =>
                    {
                        if(task.FromPeek)
                        {
                            try
                            {
                                var peekResults = StatePlatform.PeekChannelContents(task.Client, task.Url, task.Type);
                                var mostRecent = peekResults.FirstOrDefault();
                                task.Sub.LastPeekVideo = mostRecent?.DateTime ?? DateTime.MinValue;
                                task.Sub.SaveAsync();
                                var cacheItems = peekResults.Where(x => x.DateTime != default(DateTime) && x.DateTime > task.Sub.LastVideoUpdate);
                                foreach(var item in cacheItems)
                                {
                                    if (item.Author.Thumbnail == null || item.Author.Thumbnail.Length == 0)
                                        item.Author.Thumbnail = task.Sub.Channel.Thumbnail;
                                }
                                StateCache.CacheContents(cacheItems, false);
                            }
                            catch(Exception ex)
                            {
                                Logger.e(nameof(SubscriptionsTaskFetchAlgorithm), $"Subscription peek [{task.Sub.Channel.Name}] failed", ex);
                            }
                        }

                        lock (cachedChannels)
                        {
                            if (task.FromCache || task.FromPeek)
                            {
                                finished++;
                                SetProgress(finished, forkTasks.Count);
                                if (!cachedChannels.Contains(task.Url))
                                {
                                    return new SubscriptionTaskResult(task, null, null);
                                }
                                else
                                {
                                    cachedChannels.Add(task.Url);
                                    return new SubscriptionTaskResult(task, StateCache.GetChannelCachePager(task.Url), null);
                                }
                            }
                        }

                        var shouldIgnore = failedPlugins.Contains(task.Client.ID);
                        if (shouldIgnore)
                        {
                            return new SubscriptionTaskResult(task, null, null); //skipped
                        }

                        Exception taskEx = null;
                        IPager<PlatformContent> pager = null;
                        try
                        {
                            Stopwatch watch = new Stopwatch();
                            watch.Start();
                            pager = StatePlatform.GetChannelContent(task.Client,
                                task.Url, task.Type, ResultCapabilities.ORDER_CHONOLOGICAL);

                            var initialPage = pager.GetResults();
                            task.Sub.UpdateSubscriptionState(task.Type, initialPage);
                            task.Sub.Save();

                            finished++;
                            SetProgress(finished, forkTasks.Count);
                            watch.Stop();

                            Logger.i("StateSubscriptions", $"Subscription [{task.Sub.Channel.Name}]:{task.Type} results in {watch.ElapsedMilliseconds}ms");
                            return new SubscriptionTaskResult(task, pager, null);
                        }
                        catch (Exception ex)
                        {
                            Logger.e(nameof(StateSubscriptions), $"Subscription [{task.Sub.Channel.Name}] failed", ex);
                            var channelEx = new ChannelException(task.Sub.Channel, ex);
                            finished++;
                            SetProgress(finished, forkTasks.Count);

                            if (ex is ScriptCaptchaRequiredException capEx)
                            {
                                lock (failedPlugins)
                                {
                                    if (capEx.Config is PluginConfig && !failedPlugins.Contains(capEx.Config.ID))
                                    {
                                        Logger.w(nameof(StateSubscriptions), $"Subscriptions ignoring plugin [{capEx.Config.Name}] due to Captcha");
                                        failedPlugins.Add(capEx.Config.ID);
                                    }
                                }
                            }
                            else if (ex is ScriptCriticalException critEx)
                            {
                                lock (failedPlugins)
                                {
                                    if (critEx.Config is PluginConfig && !failedPlugins.Contains(critEx.Config.ID))
                                    {
                                        Logger.w(nameof(StateSubscriptions), $"Subscriptions ignoring plugin [{critEx.Config.Name}] due to critical exception:\n{ex.Message}");
                                        failedPlugins.Add(critEx.Config.ID);
                                    }
                                }
                            }

                            if (!WithCacheFallback)
                                throw channelEx;
                            else
                            {
                                Logger.i(nameof(StateSubscriptions), $"Channel {task.Sub.Channel.Name} failed, substituting with cache");
                                pager = StateCache.GetChannelCachePager(task.Sub.Channel.Url);
                                taskEx = ex;
                                return new SubscriptionTaskResult(task, pager, taskEx);
                            }
                        }
                    });

                    forkTask.Start();//TODO: Threadpooled
                }
                else
                {
                    TaskCompletionSource<SubscriptionTaskResult> source = new TaskCompletionSource<SubscriptionTaskResult>();
                    Action act = () =>
                    {
                        lock (cachedChannels)
                        {
                            if (task.FromCache)
                            {
                                finished++;
                                SetProgress(finished, forkTasks.Count);
                                if (!cachedChannels.Contains(task.Url))
                                {
                                    source.SetResult(new SubscriptionTaskResult(task, null, null));
                                    return;
                                }
                                else
                                {
                                    cachedChannels.Add(task.Url);
                                    source.SetResult(new SubscriptionTaskResult(task, StateCache.GetChannelCachePager(task.Url), null));
                                    return;
                                }
                            }
                        }

                        var shouldIgnore = failedPlugins.Contains(task.Client.ID);
                        if (shouldIgnore)
                        {
                            source.SetResult(new SubscriptionTaskResult(task, null, null)); //skipped
                            return;
                        }

                        Exception taskEx = null;
                        IPager<PlatformContent> pager = null;
                        try
                        {
                            Stopwatch watch = new Stopwatch();
                            watch.Start();
                            pager = StatePlatform.GetChannelContent(task.Client,
                                task.Url, task.Type, ResultCapabilities.ORDER_CHONOLOGICAL);

                            var initialPage = pager.GetResults();
                            task.Sub.UpdateSubscriptionState(task.Type, initialPage);
                            task.Sub.Save();

                            finished++;
                            SetProgress(finished, forkTasks.Count);
                            watch.Stop();

                            Logger.i("StateSubscriptions", $"Subscription [{task.Sub.Channel.Name}]:{task.Type} results in {watch.ElapsedMilliseconds}ms");
                            source.SetResult(new SubscriptionTaskResult(task, pager, null));
                            return;
                        }
                        catch (Exception ex)
                        {
                            Logger.e(nameof(StateSubscriptions), $"Subscription [{task.Sub.Channel.Name}] failed", ex);
                            var channelEx = new ChannelException(task.Sub.Channel, ex);
                            finished++;
                            SetProgress(finished, forkTasks.Count);

                            if (ex is ScriptCaptchaRequiredException capEx)
                            {
                                lock (failedPlugins)
                                {
                                    if (capEx.Config is PluginConfig && !failedPlugins.Contains(capEx.Config.ID))
                                    {
                                        Logger.w(nameof(StateSubscriptions), $"Subscriptions ignoring plugin [{capEx.Config.Name}] due to Captcha");
                                        failedPlugins.Add(capEx.Config.ID);
                                    }
                                }
                            }
                            else if (ex is ScriptCriticalException critEx)
                            {
                                lock (failedPlugins)
                                {
                                    if (critEx.Config is PluginConfig && !failedPlugins.Contains(critEx.Config.ID))
                                    {
                                        Logger.w(nameof(StateSubscriptions), $"Subscriptions ignoring plugin [{critEx.Config.Name}] due to critical exception:\n{ex.Message}");
                                        failedPlugins.Add(critEx.Config.ID);
                                    }
                                }
                            }

                            if (!WithCacheFallback)
                                throw channelEx;
                            else
                            {
                                Logger.i(nameof(StateSubscriptions), $"Channel {task.Sub.Channel.Name} failed, substituting with cache");
                                pager = StateCache.GetChannelCachePager(task.Sub.Channel.Url);
                                taskEx = ex;
                                source.SetResult(new SubscriptionTaskResult(task, pager, taskEx));
                                return;
                            }
                        }
                    };
                    _pool.Run(act);
                    forkTask = source.Task;
                }
                forkTasks.Add(forkTask);
            }

            return forkTasks;
        }

        public abstract List<SubscriptionTask> GetSubscriptionTasks(Dictionary<Subscription, List<string>> subs);

        public class SubscriptionTask
        {
            public GrayjayPlugin Client { get; }
            public Subscription Sub { get; }
            public string Url { get; }
            public string Type { get; }
            public bool FromPeek { get; set; }
            public bool FromCache { get; set; }
            public int Urgency { get; set; }

            public SubscriptionTask(GrayjayPlugin client, Subscription sub, string url, string type, bool fromCache = false, int urgency = 0)
            {
                Client = client;
                Sub = sub;
                Url = url;
                Type = type;
                FromCache = fromCache;
                Urgency = urgency;
            }
        }

        public class SubscriptionTaskResult
        {
            public SubscriptionTask Task { get; }
            public IPager<PlatformContent> Pager { get; }
            public Exception Exception { get; }

            public SubscriptionTaskResult(SubscriptionTask task, IPager<PlatformContent> pager, Exception exception)
            {
                Task = task;
                Pager = pager;
                Exception = exception;
            }
        }
    }
}
