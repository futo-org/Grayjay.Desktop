using Grayjay.ClientServer.Models;
using Grayjay.ClientServer.Models.History;
using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine.Models.Detail;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Pagers;
using Microsoft.AspNetCore.Mvc;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class HistoryController : ControllerBase
    {
        public class HistoryState
        {
            public IPager<HistoryVideo> HistoryPager { get; set; }
        }

        

        private IPager<HistoryVideo> EnsureHistoryPager() => this.State().HistoryState.HistoryPager ?? throw new BadHttpRequestException("No home loaded");

        [HttpGet]
        public long GetHistoricalPosition(string url)
        {
            return StateHistory.GetHistoryPosition(url);
        }

        [HttpGet]
        public PagerResult<HistoryVideo> HistoryLoad()
        {
            var pager = StateHistory.GetHistoryPager();
            this.State().HistoryState.HistoryPager = pager;
            return pager.AsPagerResult();
        }
        [HttpGet]
        public PagerResult<HistoryVideo> HistoryLoadSearch(string query)
        {
            var pager = StateHistory.GetHistorySearchPager(query);
            this.State().HistoryState.HistoryPager = pager;
            return pager.AsPagerResult();
        }

        [HttpGet]
        public PagerResult<HistoryVideo> HistoryNextPage()
        {
            try
            {
                lock (this.State().HistoryState.HistoryPager)
                {
                    var pager = EnsureHistoryPager();
                    pager.NextPage();
                    return pager.AsPagerResult();
                }
            }
            catch(Exception ex)
            {
                return new PagerResult<HistoryVideo>()
                {
                    Results = new HistoryVideo[0],
                    HasMore = false,
                    Exception = ex.Message
                };
            }
        }


        [HttpGet]
        public bool RemoveHistory(string url)
        {
            StateHistory.RemoveHistory(url);
            return true;
        }

        [HttpGet]
        public bool RemoveHistoryRange(long minutes)
        {
            StateHistory.RemoveHistoryRange(minutes);
            return true;
        }
    }
}
