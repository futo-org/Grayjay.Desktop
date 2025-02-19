using Grayjay.ClientServer.Models;
using Grayjay.ClientServer.Pagers;
using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine.Models.Detail;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Pagers;
using Microsoft.AspNetCore.Mvc;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class HomeController : ControllerBase
    {
        public class HomeState
        {
            public IPager<PlatformContent> HomePager { get; set; }
            public int InitialPageSize { get; set; }
        }

        private IPager<PlatformContent> EnsureHomePager() => this.State().HomeState.HomePager ?? throw new BadHttpRequestException("No home loaded");


        [HttpGet]
        public PagerResult<PlatformVideo> HomeLoad(string url)
        {
            var home = new AnonymousContentRefPager(StatePlatform.GetHome());
            this.State().HomeState.HomePager = home;
            return home.AsPagerResult(x => x is PlatformVideo, y => StateHistory.AddVideoMetadata((PlatformVideo)y));
        }
        [HttpGet]
        public async Task<PagerResult<PlatformContent>> HomeLoadLazy(int initialPageSize)
        {
            await StatePlatform.WaitForStartup();
            var state = this.State();
            state.HomeState.InitialPageSize = initialPageSize;
            var home = StatePlatform.GetHomeLazy((x)=>(x is PlatformVideo vx) ? StateHistory.AddVideoMetadata(vx) : x, ()=>state.HomeState.InitialPageSize);
            state.HomeState.HomePager = home;
            return home.AsPagerResult();
        }
        [HttpGet]
        public bool HomeSetInitialPageSize(int initialPageSize)
        {
            var state = this.State();
            state.HomeState.InitialPageSize = initialPageSize;
            return true;
        }
        [HttpGet]
        public PagerResult<PlatformContent> HomeNextPage()
        {
            var state = this.State().HomeState;
            try
            {
                lock (state.HomePager)
                {
                    var home = EnsureHomePager();
                    home.NextPage();
                    return home.AsPagerResult(y => y is PlatformVideo ? StateHistory.AddVideoMetadata((PlatformVideo)y) : y);
                }
            }
            catch(Exception ex)
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
