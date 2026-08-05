using System.Text.Json.Serialization;

namespace Grayjay.ClientServer.Models
{
    public class BlockedChannel
    {
        [JsonPropertyName("url")]
        public string Url { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("thumbnail")]
        public string? Thumbnail { get; set; }
        [JsonPropertyName("pluginId")]
        public string? PluginId { get; set; }
        [JsonPropertyName("blockedTime")]
        public long BlockedTime { get; set; }
        [JsonPropertyName("channelId")]
        public string? ChannelId { get; set; }
        [JsonPropertyName("urlAlternatives")]
        public List<string> UrlAlternatives { get; set; } = new List<string>();

        public bool IsSameUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;
            return string.Equals(Url, url, StringComparison.OrdinalIgnoreCase) ||
                UrlAlternatives.Any(x => string.Equals(x, url, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsSameUrl(IEnumerable<string> urls)
        {
            if (urls == null)
                return false;
            var list = urls.Where(x => !string.IsNullOrEmpty(x)).Select(x => x.ToLower()).ToList();
            if (list.Count == 0)
                return false;
            return list.Contains(Url.ToLower()) || UrlAlternatives.Any(a => list.Contains(a.ToLower()));
        }
    }
}
