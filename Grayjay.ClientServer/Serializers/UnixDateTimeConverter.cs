using Grayjay.Engine.Models.Video.Sources;
using Newtonsoft.Json.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Grayjay.ClientServer.Serializers
{
    public class UnixDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if(reader.TokenType == JsonTokenType.Number)
            {
                long val = reader.GetInt64();
                if (val < 0)
                    return DateTime.MinValue;
                if (val > 4070912400)
                    return DateTime.MaxValue;
                return DateTimeOffset.FromUnixTimeSeconds(val).DateTime;
            }
            else
                return reader.GetDateTime();
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            if (value.Year > 9000)
                writer.WriteNumberValue(DateTimeOffset.MaxValue.ToUnixTimeSeconds());
            else if (value.Year < 1971)
                writer.WriteNumberValue(DateTimeOffset.MinValue.ToUnixTimeSeconds());
            else
                writer.WriteNumberValue(((DateTimeOffset)value).ToUnixTimeSeconds());
        }
    }
}
