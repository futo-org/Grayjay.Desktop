using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC;
using Grayjay.Engine.Models.Feed;
using Microsoft.AspNetCore.Mvc;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class WatchLaterController : ControllerBase
    {
        [HttpGet]
        public ActionResult<List<PlatformVideo>> GetAll()
        {
            return Ok(StateWatchLater.Instance.GetWatchLater());
        }

        [HttpGet]
        public ActionResult<Dictionary<string, long>> GetAddTimes()
        {
            return Ok(StateWatchLater.Instance.GetWatchLaterAddTimes());
        }

        /*
        [HttpPost]
        public ActionResult SetAll([FromBody] List<OrderedPlatformVideo> videos)
        {
            StateWatchLater.Instance.UpdateWatchLater(videos, true);
            return Ok();
        }
        */

        [HttpPost]
        public ActionResult ChangeOrder([FromBody] List<string> order)
        {
            StateWatchLater.Instance.UpdateWatchLaterOrder(order, null, true);
            return Ok();
        }

        [HttpPost]
        public ActionResult Add([FromBody] OrderedPlatformVideo video)
        {
            StateWatchLater.Instance.Add(video, true);
            return Ok();
        }

        [HttpGet]
        public ActionResult Remove(string url)
        {
            StateWatchLater.Instance.Remove(url, true);
            return Ok();
        }

        [HttpGet]
        public ActionResult<bool> GetAutoPurgeWatched()
        {
            return Ok(StateWatchLater.Instance.GetAutoPurgeWatched());
        }

        [HttpPost]
        public ActionResult SetAutoPurgeWatched([FromBody] bool enabled)
        {
            StateWatchLater.Instance.SetAutoPurgeWatched(enabled);
            return Ok();
        }

        [HttpPost]
        public ActionResult<int> PurgeWatched()
        {
            return Ok(StateWatchLater.Instance.PurgeWatchedVideos(true));
        }
    }
}
