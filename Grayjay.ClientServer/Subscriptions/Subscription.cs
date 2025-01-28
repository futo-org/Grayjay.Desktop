using Grayjay.Desktop.POC;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine.Models.Capabilities;
using Grayjay.Engine.Models.Channel;
using Grayjay.Engine.Models.Feed;

namespace Grayjay.ClientServer.Subscriptions
{
    public class SubscriptionSettings
    {
        public bool DoNotifications { get; set; }
        public bool DoFetchLive { get; set; }
        public bool DoFetchStreams { get; set; }
        public bool DoFetchVideos { get; set; }
        public bool DoFetchPosts { get; set; }
    }

    public class Subscription
    {
        public PlatformChannel Channel { get; set; }

        public bool DoNotifications { get; set; } = false;
        public bool DoFetchLive { get; set; } = false;
        public bool DoFetchStreams { get; set; } = true;
        public bool DoFetchVideos { get; set; } = true;
        public bool DoFetchPosts { get; set; } = false;

        //Last found content
        public DateTime LastVideo { get; set; } = DateTime.MaxValue;
        public DateTime LastLiveStream { get; set; } = DateTime.MaxValue;
        public DateTime LastPost { get; set; } = DateTime.MaxValue;

        //Last update time
        public DateTime LastVideoUpdate { get; set; } = DateTime.MinValue;
        public DateTime LastStreamUpdate { get; set; } = DateTime.MinValue;
        public DateTime LastLiveStreamUpdate { get; set; } = DateTime.MinValue;
        public DateTime LastPostUpdate { get; set; } = DateTime.MinValue;

        public DateTime LastPeekVideo { get; set; } = DateTime.MinValue;

        //Last video interval
        public int UploadInterval { get; set; } = 0;
        public int UploadStreamInterval { get; set; } = 0;
        public int UploadPostInterval { get; set; } = 0;

        public int PlaybackSeconds { get; set; } = 0;
        public int PlaybackViews { get; set; } = 0;

        public bool IsOther = false;

        public DateTime CreationTime { get; set; } = DateTime.Now;

        public Subscription(PlatformChannel channel)
        {
            Channel = channel;
        }


        public bool isChannel(string url)
        {
            return Channel.Url == url || Channel.UrlAlternatives.Contains(url);
        }

        public bool ShouldFetchVideos()
        {
            DateTime now = DateTime.Now;
            TimeSpan lastVideoAgo = now.Subtract(LastVideo);
            TimeSpan lastUpdateAgo = now.Subtract(LastVideoUpdate);
            return DoFetchVideos &&
                (lastVideoAgo.TotalDays < 30 || lastUpdateAgo.TotalDays >= 1) &&
                (lastVideoAgo.TotalDays < 180 || lastUpdateAgo.TotalDays >= 3);
        }

        public bool ShouldFetchStreams()
        {
            DateTime now = DateTime.Now;
            TimeSpan lastVideoAgo = now.Subtract(LastLiveStream);
            TimeSpan lastUpdateAgo = now.Subtract(LastStreamUpdate);
            return DoFetchStreams && lastVideoAgo.TotalDays < 7;
        }
        public bool ShouldFetchLiveStreams()
        {
            DateTime now = DateTime.Now;
            TimeSpan lastStreamAgo = now.Subtract(LastLiveStream);
            TimeSpan lastUpdateAgo = now.Subtract(LastLiveStreamUpdate);
            return DoFetchLive && (lastStreamAgo.TotalDays < 3);
        }
        public bool ShouldFetchPosts()
        {
            DateTime now = DateTime.Now;
            TimeSpan lastPostAgo = now.Subtract(LastPost);
            TimeSpan lastUpdateAgo = now.Subtract(LastPostUpdate);
            return DoFetchPosts && (lastPostAgo.TotalDays < 5);
        }

        public void UpdateWatchTime(int deltaSeconds)
        {
            PlaybackSeconds += deltaSeconds;
            SaveAsync();
        }

        public void Save()
        {
            if (IsOther)
                StateSubscriptions.SaveSubscriptionOther(this);
            else
                StateSubscriptions.SaveSubscription(this);
        }
        public void SaveAsync()
        {
            if (IsOther)
                StateSubscriptions.SaveSubscriptionOtherAsync(this);
            else
                StateSubscriptions.SaveSubscriptionAsync(this);
        }


        public void UpdateSubscriptionState(string type, PlatformContent[] initialPager)
        {
            int interval;
            DateTime mostRecent;

            if(initialPager.Any())
            {
                int newestVideoDays = (int)initialPager[0].DateTime.NowDiff().TotalDays;
                List<int> diffs = new List<int>();
                for(int i = (initialPager.Length - 1); i > 0; i--)
                {
                    int currentVideoDays = ((int)initialPager[i].DateTime.NowDiff().TotalDays);
                    int nextVideoDays = ((int)initialPager[i - 1].DateTime.NowDiff().TotalDays);

                    if(currentVideoDays != null && nextVideoDays != null)
                    {
                        var diff = nextVideoDays - currentVideoDays;
                        diffs.Add(diff);
                    }
                }
                var averageDiff = (diffs.Count > 0) ? ((int)Math.Min(newestVideoDays, diffs.Average())) : newestVideoDays;
                interval = Math.Max(1, averageDiff);
                mostRecent = initialPager[0].DateTime;
            }
            else
            {
                interval = 5;
                mostRecent = DateTime.MinValue;
                Logger.i("Subscription", $"Subscription [{Channel.Name}]:{type} no results found");
            }

            switch(type)
            {
                case ResultCapabilities.TYPE_VIDEOS:
                    UploadInterval = interval;
                    if (mostRecent != DateTime.MinValue)
                        LastVideo = mostRecent;
                    else if (LastVideo.Year > 3000)
                        LastVideo = DateTime.MinValue;
                    LastVideoUpdate = DateTime.Now;
                    break;
                case ResultCapabilities.TYPE_MIXED:
                    UploadInterval = interval;
                    if (mostRecent != DateTime.MinValue)
                        LastVideo = mostRecent;
                    else if (LastVideo.Year > 3000)
                        LastVideo = DateTime.MinValue;
                    LastVideoUpdate = DateTime.Now;
                    break;
                case ResultCapabilities.TYPE_SUBSCRIPTIONS:
                    UploadInterval = interval;
                    if (mostRecent != DateTime.MinValue)
                        LastVideo = mostRecent;
                    else if (LastVideo.Year > 3000)
                        LastVideo = DateTime.MinValue;
                    LastVideoUpdate = DateTime.Now;
                    break;
                case ResultCapabilities.TYPE_STREAMS:
                    UploadStreamInterval = interval;
                    if (mostRecent != DateTime.MinValue)
                        LastLiveStream = mostRecent;
                    else if (LastVideo.Year > 3000)
                        LastLiveStream = DateTime.MinValue;
                    LastStreamUpdate = DateTime.Now;
                    break;
                case ResultCapabilities.TYPE_POSTS:
                    UploadInterval = interval;
                    if (mostRecent != DateTime.MinValue)
                        LastPost = mostRecent;
                    else if (LastVideo.Year > 3000)
                        LastPost = DateTime.MinValue;
                    LastPostUpdate = DateTime.Now;
                    break;
            }
        }
    }
}
