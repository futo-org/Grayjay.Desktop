using Grayjay.ClientServer.Serializers;
using Grayjay.Engine.Models.Feed;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Grayjay.ClientServer.Database.Indexes
{
    public class DBSubscriptionCacheIndex: DBIndex<PlatformContent>
    {
        public const string TABLE_NAME = "subscriptionCache";

        private static JsonSerializerOptions _serializerOptions = new JsonSerializerOptions();
        static DBSubscriptionCacheIndex()
        {
            _serializerOptions.Converters.Add(new PlatformContentConverter());
        }


        [Indexed]
        public string Url { get; set; }
        [Indexed]
        public string ChannelUrl { get; set; }
        [Indexed]
        [Order(0, Ordering.Descending)]
        public DateTime DateTime { get; set; }


        public DBSubscriptionCacheIndex() { }
        public DBSubscriptionCacheIndex(PlatformContent content)
        {
            FromObject(content);
        }

        public override PlatformContent Deserialize()
        {
            string str = Encoding.UTF8.GetString(Serialized);
            return JsonSerializer.Deserialize<PlatformContent>(str, _serializerOptions);
        }

        public override void FromObject(PlatformContent content)
        {
            Url = content.Url;
            ChannelUrl = content.Author.Url;
            DateTime = content.DateTime;
            Serialized = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(content));
        }
    }
}
