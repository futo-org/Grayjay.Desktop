using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine.Models.Detail;
using Grayjay.Engine.Models.Ratings;
using Microsoft.AspNetCore.Mvc;
using System.Web;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class PlayerController : ControllerBase
    {
        [HttpGet]
        [HttpPost]
        public IActionResult Play([FromQuery] string? url, [FromQuery] int position = 0)
        {
            if (string.IsNullOrWhiteSpace(url))
                return BadRequest(new { error = "Missing 'url' parameter" });

            url = url.Trim();
            if (url.StartsWith("grayjay://", StringComparison.OrdinalIgnoreCase))
                return Protocol(url);

            Logger.i(nameof(PlayerController), $"Play requested for URL: {url} (position: {position}s)");
            StateWebsocket.OpenUrl(url, position);
            return Ok(new { success = true, action = "play", url, position });
        }

        [HttpGet]
        [HttpPost]
        public IActionResult Stop()
        {
            Logger.i(nameof(PlayerController), "Stop requested");
            StateWebsocket.CloseVideo();
            return Ok(new { success = true, action = "stop" });
        }

        [HttpGet]
        [HttpPost]
        public IActionResult Pause()
        {
            Logger.i(nameof(PlayerController), "Pause requested");
            StateWebsocket.PauseVideo();
            return Ok(new { success = true, action = "pause" });
        }

        [HttpGet]
        [HttpPost]
        public IActionResult Resume()
        {
            Logger.i(nameof(PlayerController), "Resume requested");
            StateWebsocket.ResumeVideo();
            return Ok(new { success = true, action = "resume" });
        }

        [HttpGet]
        [HttpPost]
        public IActionResult Toggle()
        {
            Logger.i(nameof(PlayerController), "Toggle requested");
            StateWebsocket.ToggleVideo();
            return Ok(new { success = true, action = "toggle" });
        }

        [HttpGet]
        [HttpPost]
        public IActionResult Seek([FromQuery] double position)
        {
            Logger.i(nameof(PlayerController), $"Seek requested: {position}s");
            StateWebsocket.SeekVideo(position);
            return Ok(new { success = true, action = "seek", position });
        }

        [HttpGet]
        [HttpPost]
        public IActionResult Volume([FromQuery] double val)
        {
            Logger.i(nameof(PlayerController), $"Volume requested: {val}%");
            StateWebsocket.SetVolume(val);
            return Ok(new { success = true, action = "volume", value = val });
        }

        [HttpGet]
        [HttpPost]
        public IActionResult Exit()
        {
            Logger.i(nameof(PlayerController), "Application exit requested via API");
            _ = Task.Run(async () =>
            {
                await Task.Delay(100);
                await StateApp.ExitAsync();
            });
            return Ok(new { success = true, action = "exit" });
        }

        [HttpGet]
        [HttpPost]
        public IActionResult Quit() => Exit();

        [HttpGet]
        [HttpPost]
        public IActionResult Close() => Exit();

        [HttpGet]
        [HttpPost]
        public IActionResult Protocol([FromQuery] string uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
                return BadRequest(new { error = "Missing 'uri' parameter" });

            uri = uri.Trim();
            if (!uri.StartsWith("grayjay://", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { error = "URI must start with grayjay://" });

            string pathAndQuery = uri.Substring("grayjay://".Length);

            // Handle direct web URLs prefixed with grayjay://
            if (pathAndQuery.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                pathAndQuery.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return Play(pathAndQuery);
            }

            string command = pathAndQuery;
            string queryString = string.Empty;
            int qIndex = pathAndQuery.IndexOf('?');
            if (qIndex >= 0)
            {
                command = pathAndQuery.Substring(0, qIndex).TrimEnd('/');
                queryString = pathAndQuery.Substring(qIndex + 1);
            }
            else
            {
                command = command.TrimEnd('/');
            }

            var queryParams = HttpUtility.ParseQueryString(queryString);

            switch (command.ToLowerInvariant())
            {
                case "exit":
                case "quit":
                case "close":
                    return Exit();

                case "stop":
                    return Stop();

                case "pause":
                    return Pause();

                case "resume":
                    return Resume();

                case "toggle":
                    return Toggle();

                case "play":
                case "open":
                case "content":
                case "video":
                    string? targetUrl = queryParams["url"] ?? queryParams["data"];
                    if (string.IsNullOrWhiteSpace(targetUrl))
                    {
                        // Check if URL was passed in path, e.g. grayjay://play/https://youtube.com/...
                        int slashIndex = pathAndQuery.IndexOf('/');
                        if (slashIndex >= 0)
                            targetUrl = pathAndQuery.Substring(slashIndex + 1);
                    }
                    int pos = 0;
                    if (int.TryParse(queryParams["pos"] ?? queryParams["position"], out int parsedPos))
                        pos = parsedPos;
                    return Play(targetUrl, pos);

                case "seek":
                    double seekPos = 0;
                    if (double.TryParse(queryParams["pos"] ?? queryParams["position"], out double sPos))
                        seekPos = sPos;
                    else
                    {
                        int slashIndex = pathAndQuery.IndexOf('/');
                        if (slashIndex >= 0 && double.TryParse(pathAndQuery.Substring(slashIndex + 1), out double slashPos))
                            seekPos = slashPos;
                    }
                    return Seek(seekPos);

                case "volume":
                    double vol = 100;
                    if (double.TryParse(queryParams["val"] ?? queryParams["value"] ?? queryParams["level"], out double vVal))
                        vol = vVal;
                    return Volume(vol);

                default:
                    // If the path contains a known host like youtube.com, rumble.com etc.
                    if (pathAndQuery.Contains("youtube.com") || pathAndQuery.Contains("youtu.be") || pathAndQuery.Contains("rumble.com"))
                    {
                        string fallbackUrl = pathAndQuery.StartsWith("http") ? pathAndQuery : "https://" + pathAndQuery;
                        return Play(fallbackUrl);
                    }
                    return BadRequest(new { error = $"Unknown grayjay:// command '{command}'" });
            }
        }

        [HttpGet]
        public IActionResult TestSources([FromQuery] string url)
        {
            try
            {
                var contentDetails = StatePlatform.GetContentDetails(url);
                return Ok(contentDetails);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message, stack = ex.StackTrace });
            }
        }

        [HttpGet]
        public IActionResult Status()
        {
            var server = GrayjayServer.Instance;
            var windowStates = StateWindow.GetAllStates();
            WindowState? activeWindow = windowStates
                .OrderByDescending(w => w.DetailsState.VideoLoaded != null)
                .ThenByDescending(w => w.LastAccess)
                .FirstOrDefault();

            object? activeVideoObj = null;
            if (activeWindow?.DetailsState?.VideoLoaded != null)
            {
                var vid = activeWindow.DetailsState.VideoLoaded;
                long lastPosMs = activeWindow.DetailsState._lastWatchPosition;

                object? ratingObj = vid.Rating switch
                {
                    RatingDislikes rd => new { type = "Dislikes", likes = rd.Likes, dislikes = rd.Dislikes },
                    RatingLikes rl => new { type = "Likes", likes = rl.Likes, dislikes = 0 },
                    RatingScaler rs => new { type = "Scaler", value = rs.Value },
                    _ => null
                };

                activeVideoObj = new
                {
                    id = vid.ID?.Value,
                    platform = vid.ID?.Platform,
                    url = vid.Url,
                    title = vid.Name,
                    author = vid.Author != null ? new
                    {
                        id = vid.Author.ID?.Value,
                        name = vid.Author.Name,
                        url = vid.Author.Url,
                        thumbnail = vid.Author.Thumbnail,
                        subscribers = vid.Author.Subscribers
                    } : null,
                    duration = vid.Duration,
                    position = lastPosMs > 0 ? (double)lastPosMs / 1000.0 : 0.0,
                    description = vid.Description,
                    views = vid.ViewCount,
                    isLive = vid.IsLive,
                    rating = ratingObj,
                    thumbnails = vid.Thumbnails?.Sources?.Select(t => new { url = t.Url, quality = t.Quality }),
                    subtitles = vid.Subtitles?.Select(s => new { name = s.Name, format = s.Format, url = s.Url })
                };
            }

            return Ok(new
            {
                success = true,
                status = "running",
                port = server?.BaseUri?.Port ?? -1,
                hasWindow = server?.WindowProvider != null,
                activeVideo = activeVideoObj
            });
        }
    }
}
