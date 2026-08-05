using Grayjay.ClientServer.Models;
using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC;
using Grayjay.Desktop.POC.Port.States;
using Microsoft.AspNetCore.Mvc;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class BlockedChannelsController : ControllerBase
    {
        public class BlockChannelRequest
        {
            public string Url { get; set; }
            public string Name { get; set; }
            public string Thumbnail { get; set; }
            public string PluginId { get; set; }
        }

        [HttpGet]
        public ActionResult<List<BlockedChannel>> List()
        {
            return Ok(StateBlockedChannels.Instance.GetBlocked());
        }

        [HttpGet]
        public ActionResult<bool> IsBlocked(string url)
        {
            return Ok(StateBlockedChannels.Instance.IsBlocked(url));
        }

        [HttpPost]
        public ActionResult<BlockedChannel> Add([FromBody] BlockChannelRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Url))
                throw new BadHttpRequestException("A channel url is required");

            var blocked = new BlockedChannel()
            {
                Url = request.Url,
                Name = request.Name,
                Thumbnail = request.Thumbnail,
                PluginId = request.PluginId,
                BlockedTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            try
            {
                var client = StatePlatform.GetChannelClientOrNull(request.Url);
                if (client != null)
                {
                    var channel = StatePlatform.GetChannel(request.Url);
                    if (channel != null)
                    {
                        blocked.Url = channel.Url;
                        blocked.Name = channel.Name;
                        blocked.Thumbnail = channel.Thumbnail;
                        blocked.PluginId = client.Config.ID;
                        blocked.ChannelId = channel.ID?.Value;
                        blocked.UrlAlternatives = channel.UrlAlternatives?.ToList() ?? new List<string>();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.w(nameof(BlockedChannelsController), $"Failed to resolve channel [{request.Url}], using provided data", ex);
            }

            StateBlockedChannels.Instance.Add(blocked, true);
            return Ok(blocked);
        }

        [HttpGet]
        public ActionResult<bool> Remove(string url)
        {
            if (string.IsNullOrEmpty(url))
                throw new BadHttpRequestException("A channel url is required");
            StateBlockedChannels.Instance.Remove(url, true);
            return Ok(true);
        }
    }
}
