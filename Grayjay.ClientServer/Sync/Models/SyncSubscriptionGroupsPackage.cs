using Grayjay.ClientServer.Models.Subscriptions;
using Grayjay.ClientServer.Subscriptions;
using Grayjay.Engine.Models.Channel;
using System.Text.Json.Serialization;

namespace Grayjay.ClientServer.Sync.Models
{
    public class SyncSubscriptionGroupsPackage
    {
        [JsonPropertyName("groups")]
        public List<SubscriptionGroup> Groups { get; set; } = new List<SubscriptionGroup>();
        [JsonPropertyName("groupRemovals")]
        public Dictionary<string, long> GroupRemovals { get; set; } = new Dictionary<string, long>();
    }

}
