using Grayjay.ClientServer.Models;
using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine;
using Grayjay.Engine.Models.Detail;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Pagers;
using Microsoft.AspNetCore.Mvc;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class PlaylistController : ControllerBase
    {
        public class PlaylistState
        {
            public PlatformPlaylistDetails? PlaylistLoaded { get; set; }
            public ReusablePager<PlatformContent>? PlaylistContentsPager { get; set; }
            public IPager<PlatformContent>? PlaylistContentsWindow { get; set; }
        }

        public PlatformPlaylistDetails EnsurePlaylist()
            => this.State().PlaylistState.PlaylistLoaded ?? throw new BadHttpRequestException("No playlist loaded");
        public IPager<PlatformContent> EnsurePlaylistContentsWindow()
            => this.State().PlaylistState.PlaylistContentsWindow ?? throw new BadHttpRequestException("No playlist contents window loaded");
        public ReusablePager<PlatformContent> EnsurePlaylistContentsPager()
            => this.State().PlaylistState.PlaylistContentsPager ?? throw new BadHttpRequestException("No reusable playlist contents loaded");

        [HttpGet]
        public ActionResult<dynamic> PlaylistLoad(string url)
        {
            var state = this.State().PlaylistState;
            state.PlaylistLoaded = StatePlatform.GetPlaylist(url);
            if (state.PlaylistLoaded == null)
                return NotFound();

            state.PlaylistContentsPager = state.PlaylistLoaded.Contents.AsReusable();
            state.PlaylistContentsWindow = state.PlaylistContentsPager.GetWindow();

            return Ok(new 
            {
                state.PlaylistLoaded.Thumbnail,
                state.PlaylistLoaded.VideoCount,
                state.PlaylistLoaded.ID,
                state.PlaylistLoaded.DateTime,
                state.PlaylistLoaded.Name,
                state.PlaylistLoaded.Author,
                state.PlaylistLoaded.Url,
                state.PlaylistLoaded.ShareUrl
            });
        }

        [HttpGet]
        public PlatformPlaylistDetails PlaylistCurrent()
            => EnsurePlaylist();

        [HttpGet]
        public PagerResult<PlatformContent> ContentsLoad()
        {
            var playlistContentsWindow = EnsurePlaylistContentsWindow();
            return playlistContentsWindow.AsPagerResult();
        }

        [HttpGet]
        public PagerResult<PlatformContent> ContentsNextPage()
        {
            var playlistContentsWindow = EnsurePlaylistContentsWindow();
            playlistContentsWindow.NextPage();
            return playlistContentsWindow.AsPagerResult();
        }

        [HttpGet]
        public ActionResult<string> ConvertToLocalPlaylist()
        {
            var playlist = EnsurePlaylist();
            var playlistContentsPager = EnsurePlaylistContentsPager();
            var window = playlistContentsPager.GetWindow();
            while (window.HasMorePages())
                window.NextPage();

            var id = Guid.NewGuid();
            var contents = playlistContentsPager.PreviousResults;
            StatePlaylists.CreateOrUpdate(new Playlist
            {
                Id = id.ToString(),
                Name = playlist.Name,
                Videos = playlistContentsPager.PreviousResults.Select(v => (v as PlatformVideo)!).ToList()
            });

            return Ok(id);
        }
    }
}
