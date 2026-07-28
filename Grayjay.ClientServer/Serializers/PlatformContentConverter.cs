using Grayjay.Engine.Models;
using Grayjay.Engine.Models.Feed;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Grayjay.ClientServer.Serializers
{
    public class PlatformContentConverter : JsonConverter<PlatformContent>
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return base.CanConvert(typeToConvert);
        }

        public override PlatformContent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Utf8JsonReader readerClone = reader;

            if (readerClone.TokenType != JsonTokenType.StartObject)
                throw new JsonException("Expected object");

            ContentType typeDiscriminator = ReadContentType(ref readerClone);
            PlatformContent content = typeDiscriminator switch
            {
                ContentType.MEDIA => JsonSerializer.Deserialize<PlatformVideo>(ref reader)!,
                _ => JsonSerializer.Deserialize<PlatformContent>(ref reader)
            };
            return content;
        }

        private static ContentType ReadContentType(ref Utf8JsonReader reader)
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;
                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException("Expected property");

                string? propertyName = reader.GetString();
                if (!reader.Read())
                    throw new JsonException("Expected property value");

                if (propertyName != null && propertyName.Equals(nameof(PlatformContent.ContentType), StringComparison.OrdinalIgnoreCase))
                    return ReadContentTypeValue(ref reader);

                reader.Skip();
            }

            throw new JsonException("Expected property ContentType");
        }

        private static ContentType ReadContentTypeValue(ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int intValue))
                return (ContentType)intValue;

            if (reader.TokenType == JsonTokenType.String)
            {
                string? stringValue = reader.GetString();
                if (int.TryParse(stringValue, out int parsedInt))
                    return (ContentType)parsedInt;
                if (Enum.TryParse(stringValue, true, out ContentType parsedEnum))
                    return parsedEnum;
            }

            throw new JsonException("Expected numeric or string ContentType");
        }

        public override void Write(Utf8JsonWriter writer, PlatformContent value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value);
        }
    }
}
