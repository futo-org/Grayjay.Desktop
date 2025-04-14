using Grayjay.Engine.Models.Channel;
using Grayjay.Engine.Models.Feed;
using System.Text.Json.Serialization;

namespace Grayjay.ClientServer.SubsExchange
{
    public class ChannelResult
    {
        [JsonPropertyName("dateTime")]
        [JsonConverter(typeof(UnixSupportedDateTimeConverter))]
        public DateTime DateTime { get; set; }
        [JsonPropertyName("channelUrl")]
        public string ChannelUrl { get; set; }
        [JsonPropertyName("channel")]
        public PlatformChannel Channel { get; set; }
        [JsonPropertyName("content")]
        public PlatformContent[] Content { get; set; }
    }
}
