using System.Text.Json.Serialization;

namespace Grayjay.ClientServer.Sync.Models
{
    public class SendToDevicePackage
    {
        [JsonPropertyName("url")]
        public string Url { get; set; }
        [JsonPropertyName("position")]
        public int Position { get; set; }
    }
}
