using Grayjay.ClientServer.Models;
using Grayjay.ClientServer.Models.Subscriptions;
using Grayjay.ClientServer.States;
using Grayjay.ClientServer.Subscriptions;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Pagers;
using Microsoft.AspNetCore.Mvc;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class SubscriptionsController: ControllerBase
    {
        public class SubscriptionsState
        {
            public IPager<PlatformContent> SubscriptionPagerCache { get; set; }
            public IPager<PlatformContent> SubscriptionPager { get; set; }
            public IPager<PlatformContent> FilterPager { get; set; }
            public Dictionary<string, IPager<PlatformContent>> SubscriptionGroupPagers { get; set; } = new Dictionary<string, IPager<PlatformContent>>();

        }

        private IPager<PlatformContent> EnsureSubscriptionPagerCache() => this.State().SubscriptionsState.SubscriptionPagerCache ?? throw new BadHttpRequestException("No subscriptions cache loaded");
        private IPager<PlatformContent> EnsureSubscriptionPager() => this.State().SubscriptionsState.SubscriptionPager ?? throw new BadHttpRequestException("No subscriptions loaded");
        private IPager<PlatformContent> EnsureSubscriptionPagerFilter() => this.State().SubscriptionsState.FilterPager ?? throw new BadHttpRequestException("No filter loaded");


        [HttpGet]
        public Subscription Subscription(string url)
        {
            return StateSubscriptions.GetSubscription(url);
        }

        [HttpGet]
        public bool IsSubscribed(string url)
        {
            return StateSubscriptions.IsSubscribed(url);
        }

        [HttpGet]
        public DateTime LastSubscriptionTime()
        {
            return StateSubscriptions.GetSubscriptions().Max(x => x.CreationTime);
        }

        [HttpGet]
        public List<Subscription> Subscriptions()
        {
            return StateSubscriptions.GetSubscriptions().OrderByDescending(x => x.PlaybackSeconds).ToList();
        }

        [HttpGet]
        public ActionResult<SubscriptionSettings> SubscriptionSettings(string channelUrl)
        {
            var subscription = StateSubscriptions.GetSubscription(channelUrl);
            if (subscription == null)
                return NotFound();

            StateSubscriptions.SaveSubscription(subscription);
            return Ok(new SubscriptionSettings()
            {
                DoFetchLive = subscription.DoFetchLive,
                DoFetchPosts =subscription.DoFetchPosts,
                DoFetchStreams = subscription.DoFetchStreams,
                DoFetchVideos = subscription.DoFetchVideos,
                DoNotifications = subscription.DoNotifications
            });
        }

        [HttpPost()]
        public IActionResult UpdateSubscriptionSettings(string channelUrl, [FromBody] SubscriptionSettings settings)
        {
            var subscription = StateSubscriptions.GetSubscription(channelUrl);
            if (subscription == null)
                return NotFound();

            subscription.DoNotifications = settings.DoNotifications;
            subscription.DoFetchLive = settings.DoFetchLive;
            subscription.DoFetchStreams = settings.DoFetchStreams;
            subscription.DoFetchVideos = settings.DoFetchVideos;
            subscription.DoFetchPosts = settings.DoFetchPosts;

            StateSubscriptions.SaveSubscription(subscription);
            return Ok();
        }


        [HttpGet]
        public bool Subscribe(string url)
        {
            var channel = StatePlatform.GetChannel(url);
            if (channel == null)
                throw new BadHttpRequestException("Channel not found");
            StateSubscriptions.AddSubscription(channel, null, true);
            return true;
        }

        [HttpGet]
        public bool Unsubscribe(string url)
        {
            StateSubscriptions.RemoveSubscription(url, true);
            return false;
        }

        [HttpGet]
        public async Task<PagerResult<PlatformVideo>> SubscriptionsCacheLoad()
        {
            var subs = StateCache.GetSubscriptionCachePager();
            this.State().SubscriptionsState.SubscriptionPagerCache = subs;
            return subs.AsPagerResult(x => x is PlatformVideo, y => StateHistory.AddVideoMetadata((PlatformVideo)y));
        }
        [HttpGet]
        public PagerResult<PlatformVideo> SubscriptionsCacheNextPage()
        {
            try
            {
                lock (this.State().SubscriptionsState.SubscriptionPagerCache)
                {
                    var subs = EnsureSubscriptionPagerCache();
                    subs.NextPage();
                    return subs.AsPagerResult(x => x is PlatformVideo, y => StateHistory.AddVideoMetadata((PlatformVideo)y));
                }
            }
            catch (Exception ex)
            {
                return new PagerResult<PlatformVideo>()
                {
                    Results = new PlatformVideo[0],
                    HasMore = false,
                    Exception = ex.Message
                };
            }
        }

        [HttpGet]
        public PagerResult<PlatformVideo> SubscriptionsLoadLazy(bool updated)
        {
            //return await SubscriptionsCacheLoad(url);
            var subs = StateSubscriptions.GetGlobalSubscriptionFeedLazy(updated);
            this.State().SubscriptionsState.SubscriptionPager = subs;
            return subs.AsPagerResult(x => x is PlatformVideo, y =>
            {
                return StateHistory.AddVideoMetadata((PlatformVideo)y);
            });
        }
        [HttpGet]
        public async Task<PagerResult<PlatformVideo>> SubscriptionsLoad(bool updated)
        {
            //return await SubscriptionsCacheLoad(url);
            var subs = await StateSubscriptions.GetGlobalSubscriptionFeed(updated);
            this.State().SubscriptionsState.SubscriptionPager = subs;
            return subs.AsPagerResult(x => x is PlatformVideo, y =>
            {
                return StateHistory.AddVideoMetadata((PlatformVideo)y);
            });
        }
        [HttpGet]
        public PagerResult<PlatformVideo> SubscriptionsNextPage()
        {
            try
            {
                var subs = EnsureSubscriptionPager();
                lock (subs)
                {
                    subs.NextPage();
                    return subs.AsPagerResult(x => x is PlatformVideo, y =>
                    {
                        return StateHistory.AddVideoMetadata((PlatformVideo)y);
                    });
                }
            }
            catch (Exception ex)
            {
                return new PagerResult<PlatformVideo>()
                {
                    Results = new PlatformVideo[0],
                    HasMore = false,
                    Exception = ex.Message
                };
            }
        }

        [HttpGet]
        public async Task<PagerResult<PlatformVideo>> SubscriptionsFilterChannelLoad(string url)
        {
            var subs = StatePlatform.GetChannelContent(url);
            this.State().SubscriptionsState.FilterPager = subs;
            return subs.AsPagerResult(x => x is PlatformVideo, y =>
            {
                return StateHistory.AddVideoMetadata((PlatformVideo)y);
            });
        }
        [HttpGet]
        public PagerResult<PlatformVideo> SubscriptionsFilterNextPage()
        {
            try
            {
                var subs = EnsureSubscriptionPagerFilter();
                lock (subs)
                {
                    subs.NextPage();
                    return subs.AsPagerResult(x => x is PlatformVideo, y => {
                        return StateHistory.AddVideoMetadata((PlatformVideo)y);
                    });
                }
            }
            catch (Exception ex)
            {
                return new PagerResult<PlatformVideo>()
                {
                    Results = new PlatformVideo[0],
                    HasMore = false,
                    Exception = ex.Message
                };
            }
        }


        [HttpGet]
        public async Task<PagerResult<PlatformVideo>> SubscriptionGroupLoad(string id, bool updated)
        {
            var state = this.State().SubscriptionsState;
            var subs = await StateSubscriptions.GetSubscriptionFeed(id, updated);
            lock (state.SubscriptionGroupPagers)
            {
                if(state.SubscriptionGroupPagers.ContainsKey(id))
                    state.SubscriptionGroupPagers[id] = subs;
                else
                    state.SubscriptionGroupPagers.Add(id, subs);
            }
            return subs.AsPagerResult(x => x is PlatformVideo, y =>
            {
                return StateHistory.AddVideoMetadata((PlatformVideo)y);
            });
        }
        [HttpGet]
        public PagerResult<PlatformVideo> SubscriptionGroupNextPage(string id)
        {
            var state = this.State().SubscriptionsState;
            try
            {
                lock (state.SubscriptionGroupPagers)
                {
                    if(!state.SubscriptionGroupPagers.ContainsKey(id)) 
                        throw new BadHttpRequestException("No subscriptions feed [" + id + "] loaded");
                    var subs = state.SubscriptionGroupPagers[id];
                    subs.NextPage();
                    return subs.AsPagerResult(x => x is PlatformVideo, y =>
                    {
                        return StateHistory.AddVideoMetadata((PlatformVideo)y);
                    });
                }
            }
            catch (Exception ex)
            {
                return new PagerResult<PlatformVideo>()
                {
                    Results = new PlatformVideo[0],
                    HasMore = false,
                    Exception = ex.Message
                };
            }
        }


        [HttpGet]
        public List<SubscriptionGroup> SubscriptionGroups()
        {
            return StateSubscriptions.GetGroups();
        }
        [HttpGet]
        public SubscriptionGroup SubscriptionGroup(string id)
        {
            return StateSubscriptions.GetGroup(id);
        }
        [HttpPost]
        public SubscriptionGroup SubscriptionGroupSave([FromBody]SubscriptionGroup group)
        {
            if(group.Image == null)
            {
                List<Subscription> subs = StateSubscriptions.GetSubscriptions()
                    .Where(x => group.Urls.Any(y => x.isChannel(y)))
                    .ToList();
                group.Image = new ImageVariable()
                {
                    Url = subs.FirstOrDefault()?.Channel.Thumbnail,
                    SubscriptionUrl = subs.FirstOrDefault()?.Channel.Url
                };
            }
            if(group.ID != null)
            {
                var existingGroup = StateSubscriptions.GetGroup(group.ID);
                if(existingGroup != null)
                {
                    existingGroup.Urls = group.Urls;
                    existingGroup.Image = group.Image;
                    existingGroup.Name = group.Name;
                    group = existingGroup;
                }
            }
            var result = StateSubscriptions.SaveGroup(group);
            StateWebsocket.SubscriptionGroupsChanged();
            return result;
        }
        [HttpGet]
        public SubscriptionGroup SubscriptionGroupDelete(string id)
        {
            var result = StateSubscriptions.DeleteGroup(id);
            StateWebsocket.SubscriptionGroupsChanged();
            return result;
        }

    }
}
