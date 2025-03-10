using Grayjay.ClientServer.Models;
using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC;
using Grayjay.Engine.Models.Feed;
using Microsoft.AspNetCore.Mvc;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class PlaylistsController : ControllerBase
    {
        [HttpGet]
        public ActionResult<List<Playlist>> GetAll()
        {
            return Ok(StatePlaylists.All);
        }

        [HttpPost]
        public ActionResult CreateOrUpdate([FromBody] Playlist playlist)
        {
            StatePlaylists.CreateOrUpdate(playlist);
            return Ok();
        }
        [HttpPost]
        public ActionResult RenamePlaylist(string id, [FromBody] string newName)
        {
            var playlist = StatePlaylists.Get(id);
            if (id == null)
                return NotFound();

            playlist.Name = newName;
            StatePlaylists.CreateOrUpdate(playlist, true);
            return Ok();
        }


        public class AddContentToPlaylistsRequest
        {
            public required string[] PlaylistIds { get; set; }
            public required PlatformVideo Content { get; set; }
        }

        [HttpPost]
        public ActionResult AddContentToPlaylists([FromBody] AddContentToPlaylistsRequest request)
        {
            StatePlaylists.AddContentToPlaylists(request.PlaylistIds, request.Content);
            return Ok();
        }

        [HttpGet]
        public ActionResult RemoveContentFromPlaylist(string id, int index)
        {
            StatePlaylists.RemoveContentFromPlaylist(id, index);
            return Ok();
        }

        [HttpGet]
        public ActionResult<Playlist> Get(string id)
        {
            return Ok(StatePlaylists.Get(id));
        }

        [HttpDelete]
        public ActionResult Delete(string id)
        {
            StatePlaylists.Remove(id);
            return Ok();
        }
    }
}
