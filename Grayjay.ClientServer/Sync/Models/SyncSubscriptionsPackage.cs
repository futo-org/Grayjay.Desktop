using Grayjay.ClientServer.Subscriptions;
using Grayjay.Engine.Models.Channel;
using System.Text.Json.Serialization;

namespace Grayjay.ClientServer.Sync.Models
{
    public class SyncSubscriptionsPackage
    {
        [JsonPropertyName("subscriptions")]
        public List<Subscription> Subscriptions { get; set; } = new List<Subscription>();
        [JsonPropertyName("subscriptionRemovals")]
        public Dictionary<string, long> SubscriptionRemovals { get; set; } = new Dictionary<string, long>();
    }

}
