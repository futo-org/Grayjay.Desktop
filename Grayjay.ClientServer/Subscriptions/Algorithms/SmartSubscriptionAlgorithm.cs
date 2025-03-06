using Grayjay.ClientServer.Settings;
using Grayjay.ClientServer.Threading;
using Grayjay.Desktop.POC;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine;
using Grayjay.Engine.Models.Capabilities;

using Logger = Grayjay.Desktop.POC.Logger;

namespace Grayjay.ClientServer.Subscriptions.Algorithms
{
    public class SmartSubscriptionAlgorithm : SubscriptionsTaskFetchAlgorithm
    {
        public SmartSubscriptionAlgorithm(bool allowFailure = false, bool withCacheFallback = true, ManagedThreadPool pool = null) : base( allowFailure, withCacheFallback, pool)
        {
        }

        public override List<SubscriptionTask> GetSubscriptionTasks(Dictionary<Subscription, List<string>> subs)
        {
            var allTasks = subs.SelectMany(entry =>
            {
                var sub = entry.Key;
                var allPlatforms = entry.Value
                    .Select(url => new { Url = url, Client = StatePlatform.GetChannelClientOrNull(url) })
                    .Where(pair => pair.Client is GrayjayPlugin)
                    .ToDictionary(pair => pair.Url, pair => pair.Client);

                return allPlatforms
                    .Where(pair => pair.Value != null)
                    .SelectMany(pair =>
                    {
                        var url = pair.Key;
                        var client = pair.Value;
                        var capabilities = client.GetChannelCapabilities();

                        if (capabilities.HasType(ResultCapabilities.TYPE_MIXED) || capabilities.Types.Count == 0)
                            return new List<SubscriptionTask> { new SubscriptionTask(client, sub, pair.Key, ResultCapabilities.TYPE_MIXED) };
                        else if (capabilities.HasType(ResultCapabilities.TYPE_SUBSCRIPTIONS))
                            return new List<SubscriptionTask> { new SubscriptionTask(client, sub, pair.Key, ResultCapabilities.TYPE_SUBSCRIPTIONS) };
                        else
                        {
                            var types = new List<string>
                            {
                                sub.ShouldFetchVideos() ? ResultCapabilities.TYPE_VIDEOS : null,
                                sub.ShouldFetchStreams() ? ResultCapabilities.TYPE_STREAMS : null,
                                sub.ShouldFetchPosts() ? ResultCapabilities.TYPE_POSTS : null,
                                sub.ShouldFetchLiveStreams() ? ResultCapabilities.TYPE_LIVE : null
                            }.Where(type => type != null && capabilities.HasType(type));

                            return types.Any()
                                ? types.Select(type => new SubscriptionTask(client, sub, url, type))
                                : new List<SubscriptionTask> { new SubscriptionTask(client, sub, url, ResultCapabilities.TYPE_VIDEOS, true) };
                        }
                    });
            }).ToList();

            foreach (var task in allTasks)
            {
                task.Urgency = CalculateUpdateUrgency(task.Sub, task.Type);
            }

            var ordering = allTasks.GroupBy(task => task.Client)
                .Select(group => new { Client = group.Key, Tasks = group.OrderBy(task => task.Urgency) })
                .ToList();

            var finalTasks = new List<SubscriptionTask>();

            foreach (var clientTasks in ordering)
            {
                var limit = clientTasks.Client.GetSubscriptionRateLimit();
                if (limit == null || limit <= 0)
                {
                    finalTasks.AddRange(clientTasks.Tasks);
                }
                else
                {
                    var fetchTasks = new List<SubscriptionTask>();
                    var cacheTasks = new List<SubscriptionTask>();
                    var peekTasks = new List<SubscriptionTask>();

                    foreach (var task in clientTasks.Tasks)
                    {
                        if (!task.FromCache && fetchTasks.Count < limit)
                        {
                            fetchTasks.Add(task);
                        }
                        else
                        {
                            if (peekTasks.Count < 100 && GrayjaySettings.Instance.Subscriptions.PeekChannelContents &&
                                task.Sub.LastPeekVideo.Year < 1971 || task.Sub.LastPeekVideo < task.Sub.LastVideoUpdate &&
                                task.Client.Capabilities.HasPeekChannelContents && task.Client.GetPeekChannelTypes().Contains(task.Type))
                            {
                                task.FromPeek = true;
                                task.FromCache = true;
                                peekTasks.Add(task);
                            }
                            else
                            {
                                task.FromCache = true;
                                cacheTasks.Add(task);
                            }
                        }
                    }

                    Logger.i(nameof(SmartSubscriptionAlgorithm), $"Subscription Client Budget [{clientTasks.Client.Config.Name}]: {fetchTasks.Count}/{limit}");

                    finalTasks.AddRange(fetchTasks.Concat(peekTasks).Concat(cacheTasks));
                }
            }

            return finalTasks;
        }

        public int CalculateUpdateUrgency(Subscription sub, string type)
        {
            var lastItem = type switch
            {
                ResultCapabilities.TYPE_VIDEOS => sub.LastVideo,
                ResultCapabilities.TYPE_STREAMS => sub.LastLiveStream,
                ResultCapabilities.TYPE_LIVE => sub.LastLiveStream,
                ResultCapabilities.TYPE_POSTS => sub.LastPost,
                _ => sub.LastVideo // TODO: minimum of all?
            };

            var lastUpdate = type switch
            {
                ResultCapabilities.TYPE_VIDEOS => sub.LastVideoUpdate,
                ResultCapabilities.TYPE_STREAMS => sub.LastLiveStreamUpdate,
                ResultCapabilities.TYPE_LIVE => sub.LastLiveStreamUpdate,
                ResultCapabilities.TYPE_POSTS => sub.LastPostUpdate,
                _ => sub.LastVideoUpdate // TODO: minimum of all?
            };

            var interval = type switch
            {
                ResultCapabilities.TYPE_VIDEOS => sub.UploadInterval,
                ResultCapabilities.TYPE_STREAMS => sub.UploadStreamInterval,
                ResultCapabilities.TYPE_LIVE => sub.UploadStreamInterval,
                ResultCapabilities.TYPE_POSTS => sub.UploadPostInterval,
                _ => sub.UploadInterval // TODO: minimum of all?
            };

            var lastItemDaysAgo = ((int)lastItem.NowDiff().TotalHours);
            var lastUpdateHoursAgo = ((int)lastUpdate.NowDiff().TotalHours);
            var expectedHours = (interval * 24) - lastUpdateHoursAgo;

            return (int)(expectedHours * 100);
        }
    }
}
