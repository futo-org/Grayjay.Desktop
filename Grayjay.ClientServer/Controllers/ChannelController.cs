using Grayjay.ClientServer.Exceptions;
using Grayjay.ClientServer.Models;
using Grayjay.ClientServer.Pagers;
using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine.Models.Channel;
using Grayjay.Engine.Models.Detail;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Pagers;
using Microsoft.AspNetCore.Mvc;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class ChannelController : ControllerBase
    {
        public class ChannelState
        {
            public PlatformChannel ChannelLoaded { get; set; }
            public IPager<PlatformContent> ChannelPager { get; set; }
        }

        private IPager<PlatformContent> EnsureChannelPager() => this.State().ChannelState.ChannelPager ?? throw new BadHttpRequestException("No channel loaded");



        [HttpGet]
        public PlatformChannel Channel(string url)
        {
            var state = this.State().ChannelState;
            try
            {
                state.ChannelLoaded = StatePlatform.GetChannel(url);
            }
            catch(Exception ex)
            {
                throw DialogException.FromException("Failed to get channel", ex);
            }


            //Update channel subscription
            if (state.ChannelLoaded != null && !string.IsNullOrEmpty(state.ChannelLoaded.Thumbnail))
            {
                var sub = StateSubscriptions.GetSubscription(url);
                if (sub != null)
                {
                    sub.Channel = state.ChannelLoaded;
                    sub.SaveAsync();
                }
            }

            return state.ChannelLoaded;
        }

        [HttpGet]
        public PagerResult<PlatformContent> ChannelContentLoad()
        {
            var state = this.State().ChannelState;
            var pager = new AnonymousContentRefPager(StatePlatform.GetChannelContent(state.ChannelLoaded?.Url ?? ""));
            state.ChannelPager = pager;
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
        public PagerResult<PlatformVideo> ChannelContentLoadSearch(string query)
        {
            var state = this.State().ChannelState;
            var pager = StatePlatform.SearchChannelContent(state.ChannelLoaded?.Url ?? "", query);
            state.ChannelPager = pager;
            return pager.AsPagerResult(x => x is PlatformVideo, y => (PlatformVideo)y);
        }
        [HttpGet]
        public PagerResult<PlatformContent> ChannelContentNextPage()
        {
            var state = this.State().ChannelState;
            try
            {
                lock (state.ChannelPager)
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

    }
}
