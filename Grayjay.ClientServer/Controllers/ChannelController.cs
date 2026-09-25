using Grayjay.ClientServer.Exceptions;
using Grayjay.ClientServer.Models;
using Grayjay.ClientServer.Pagers;
using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine.Models.Channel;
using Grayjay.Engine.Models.Detail;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Pagers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualBasic;
using System.Diagnostics;
using System.Threading.Channels;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class ChannelController : ControllerBase
    {
        public class ChannelState
        {
            public PlatformChannel ChannelLoaded { get; set; }
            public IPager<PlatformContent> ChannelPager { get; set; }
            public IPager<PlatformContent> ChannelPlaylistsPager { get; set; }

            public object ChannelPagerLock { get; } = new object();
            public object ChannelPlaylistsPagerLock { get; } = new object();
        }

        private IPager<PlatformContent> EnsureChannelPager() => this.State().ChannelState.ChannelPager ?? throw new BadHttpRequestException("No channel loaded");
        private IPager<PlatformContent> EnsureChannelPlaylistsPager() => this.State().ChannelState.ChannelPlaylistsPager ?? throw new BadHttpRequestException("No channel playlists loaded");



        [HttpGet]
        public async Task<PlatformChannel> Channel(string url)
        {
            Stopwatch watch = Stopwatch.StartNew();
            Logger.w(nameof(ChannelController), $"ChannelLoad started");
            PlatformChannel channel = null;
            var state = this.State().ChannelState;
            try
            {
                channel = StatePlatform.GetChannel(url);
                state.ChannelLoaded = channel;
            }
            catch(Exception ex)
            {
                throw DialogException.FromException("Failed to get channel", ex);
            }


            //Update channel subscription
            if (channel != null && !string.IsNullOrEmpty(channel.Thumbnail))
            {
                var sub = StateSubscriptions.GetSubscription(url);
                if (sub != null)
                {
                    sub.UpdateChannelObject(channel);
                }
            }

            Logger.w(nameof(ChannelController), $"ChannelLoad took {watch.Elapsed.TotalMilliseconds}ms");
            watch.Stop();

            return channel;
        }

        [HttpGet]
        public PagerResult<PlatformContent> ChannelContentLoad(string url = null)
        {
            Logger.w(nameof(ChannelController), $"ChannelContentLoad started");
            Stopwatch watch = Stopwatch.StartNew();
            var state = this.State().ChannelState;
            var pager = new AnonymousContentRefPager(StatePlatform.GetChannelContent(url ?? state.ChannelLoaded?.Url ?? ""));
            state.ChannelPager = pager;
            watch.Stop();
            Logger.w(nameof(ChannelController), $"ChannelContentLoad took {watch.Elapsed.TotalMilliseconds}ms");
            return pager.AsPagerResult();
        }
        [HttpGet]
        public bool CanSearchChannel(string url)
        {
            var client = StatePlatform.GetChannelClientOrNull(url);
            if (client == null)
                return false;

            return client.Capabilities.HasSearchChannelContents;
        }
        [HttpGet]
        public PagerResult<PlatformVideo> ChannelContentLoadSearch(string query, string url = null)
        {
            var state = this.State().ChannelState;
            var pager = StatePlatform.SearchChannelContent(url ?? state.ChannelLoaded?.Url ?? "", query);
            state.ChannelPager = pager;
            return pager.AsPagerResult(x => x is PlatformVideo, y => (PlatformVideo)y);
        }
        [HttpGet]
        public PagerResult<PlatformContent> ChannelContentNextPage()
        {
            var state = this.State().ChannelState;
            try
            {
                lock (state.ChannelPagerLock)
                {
                    var home = EnsureChannelPager();
                    home.NextPage();
                    return home.AsPagerResult();
                }
            }
            catch (Exception ex)
            {
                return new PagerResult<PlatformContent>()
                {
                    Results = new PlatformVideo[0],
                    HasMore = false,
                    Exception = ex.Message
                };
            }
        }

        [HttpGet]
        public bool CanGetChannelPlaylists(string url)
        {
            var client = StatePlatform.GetChannelClientOrNull(url);
            if (client == null)
                return false;

            return client.Capabilities.HasGetChannelPlaylists;
        }
        [HttpGet]
        public PagerResult<PlatformContent> ChannelPlaylistsLoad(string url = null)
        {
            Logger.w(nameof(ChannelController), $"ChannelPlaylistsLoad started");
            Stopwatch watch = Stopwatch.StartNew();
            var state = this.State().ChannelState;
            try
            {
                var pager = StatePlatform.GetChannelPlaylists(url ?? state.ChannelLoaded?.Url ?? "");
                lock (state.ChannelPlaylistsPagerLock)
                {
                    state.ChannelPlaylistsPager = pager;
                    return pager.AsPagerResult();
                }
            }
            catch (Exception ex)
            {
                throw DialogException.FromException("Failed to get channel playlists", ex);
            }
            finally
            {
                watch.Stop();
                Logger.w(nameof(ChannelController), $"ChannelPlaylistsLoad took {watch.Elapsed.TotalMilliseconds}ms");
            }
        }
        [HttpGet]
        public PagerResult<PlatformContent> ChannelPlaylistsNextPage()
        {
            var state = this.State().ChannelState;
            try
            {
                lock (state.ChannelPlaylistsPagerLock)
                {
                    var playlists = EnsureChannelPlaylistsPager();
                    playlists.NextPage();
                    return playlists.AsPagerResult();
                }
            }
            catch (Exception ex)
            {
                return new PagerResult<PlatformContent>()
                {
                    Results = new PlatformContent[0],
                    HasMore = false,
                    Exception = ex.Message
                };
            }
        }

    }
}
