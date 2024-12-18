using Grayjay.ClientServer.Models;
using Grayjay.ClientServer.Models.Subscriptions;
using Grayjay.ClientServer.States;
using Grayjay.ClientServer.Subscriptions;
using Grayjay.Engine.Models.Channel;
using Grayjay.Engine.Models.Feed;
using System.Text.Json.Serialization;

namespace Grayjay.ClientServer.Sync.Models
{
    public class SyncWatchLaterPackage
    {
        [JsonPropertyName("videos")]
        public List<PlatformVideo> Videos { get; set; } = new List<PlatformVideo>();
        [JsonPropertyName("videoAdds")]
        public Dictionary<string, long> VideoAdds { get; set; } = new Dictionary<string, long>();
        [JsonPropertyName("videoRemovals")]
        public Dictionary<string, long> VideoRemovals { get; set; } = new Dictionary<string, long>();

        [JsonPropertyName("reorderTime")]
        public long ReorderTime { get; set; } = 0;

        [JsonPropertyName("ordering")]
        public List<string>? Ordering { get; set; } = null;
    }

}
