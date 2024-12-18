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
                throw new JsonException();

            readerClone.Read();
            if (readerClone.TokenType != JsonTokenType.PropertyName)
                throw new JsonException();

            string? propertyName = readerClone.GetString();
            if (propertyName != nameof(PlatformContent.ContentType))
                throw new JsonException();

            readerClone.Read();
            if (readerClone.TokenType != JsonTokenType.Number)
                throw new JsonException();

            ContentType typeDiscriminator = (ContentType)readerClone.GetInt32();
            PlatformContent content = typeDiscriminator switch
            {
                ContentType.MEDIA => JsonSerializer.Deserialize<PlatformVideo>(ref reader)!,
                _ => JsonSerializer.Deserialize<PlatformContent>(ref reader)
            };
            return content;
        }

        public override void Write(Utf8JsonWriter writer, PlatformContent value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value);
        }
    }
}
