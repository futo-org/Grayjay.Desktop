using Grayjay.ClientServer.Models;
using System.Text.Json.Serialization;

namespace Grayjay.ClientServer.Sync.Models
{
    public class SyncBlockedChannelsPackage
    {
        [JsonPropertyName("channels")]
        public List<BlockedChannel> Channels { get; set; } = new List<BlockedChannel>();

        [JsonPropertyName("channelRemovals")]
        public Dictionary<string, long> ChannelRemovals { get; set; } = new Dictionary<string, long>();
    }
}
