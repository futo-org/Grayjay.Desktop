using Grayjay.ClientServer.Settings;
using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine;
using Grayjay.Engine.Setting;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class HandleController : ControllerBase
    {
        [HttpGet]
        public IActionResult GetHandlePlan(string url)
        {
            if (string.IsNullOrEmpty(url) || url == "undefined")
                return NotFound();
            var contentClient = StatePlatform.GetContentClientOrNull(url);
            if (contentClient != null)
                return Ok(new HandlePlan()
                {
                    Type = "content",
                    Data = url
                });
            var channelClient = StatePlatform.GetChannelClientOrNull(url);
            if (channelClient != null)
                return Ok(new HandlePlan()
                {
                    Type = HandlePlan.TYPE_CHANNEL,
                    Data = url
                });
            var playlistClient = StatePlatform.GetPlaylistClientOrNull(url);
            if (playlistClient != null)
                return Ok(new HandlePlan()
                {
                    Type = HandlePlan.TYPE_PLAYLIST,
                    Data = url
                });
            return Ok(new HandlePlan()
            {
                Type= HandlePlan.TYPE_NONE,
                Data = url
            });
        }
    }

    public class HandlePlan
    {
        public const string TYPE_CONTENT = "content";
        public const string TYPE_CHANNEL = "channel";
        public const string TYPE_PLAYLIST = "playlist";
        public const string TYPE_NONE = "none";

        public string Type { get; set; }
        public string Data { get; set; }
    }
}
