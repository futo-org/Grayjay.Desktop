using Grayjay.ClientServer.Models;
using Grayjay.ClientServer.Models.Subscriptions;
using Grayjay.ClientServer.Subscriptions;
using Grayjay.Engine.Models.Channel;
using System.Text.Json.Serialization;

namespace Grayjay.ClientServer.Sync.Models
{
    public class SyncPlaylistsPackage
    {
        [JsonPropertyName("playlists")]
        public List<Playlist> Playlists { get; set; } = new List<Playlist>();
        [JsonPropertyName("playlistRemovals")]
        public Dictionary<string, long> PlaylistRemovals { get; set; } = new Dictionary<string, long>();
    }

}
