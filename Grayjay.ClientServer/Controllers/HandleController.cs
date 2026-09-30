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

            if (url.StartsWith("grayjay://", StringComparison.OrdinalIgnoreCase))
            {
                var stripped = url.Substring("grayjay://".Length);
                if (stripped.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    stripped.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    url = stripped;
                }
                else if (stripped.StartsWith("play/", StringComparison.OrdinalIgnoreCase) ||
                         stripped.StartsWith("open/", StringComparison.OrdinalIgnoreCase) ||
                         stripped.StartsWith("content/", StringComparison.OrdinalIgnoreCase) ||
                         stripped.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                {
                    int slash = stripped.IndexOf('/');
                    url = stripped.Substring(slash + 1);
                }
                else if (stripped.StartsWith("play?", StringComparison.OrdinalIgnoreCase) ||
                         stripped.StartsWith("open?", StringComparison.OrdinalIgnoreCase) ||
                         stripped.StartsWith("content?", StringComparison.OrdinalIgnoreCase) ||
                         stripped.StartsWith("video?", StringComparison.OrdinalIgnoreCase))
                {
                    int q = stripped.IndexOf('?');
                    var qs = System.Web.HttpUtility.ParseQueryString(stripped.Substring(q + 1));
                    url = qs["url"] ?? qs["data"] ?? stripped;
                }
                else if (stripped.Contains("youtube.com") || stripped.Contains("youtu.be") || stripped.Contains("rumble.com"))
                {
                    url = stripped.StartsWith("http") ? stripped : "https://" + stripped;
                }
            }

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
