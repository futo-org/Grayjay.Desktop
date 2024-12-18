using Grayjay.Engine.Models;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Models.Ratings;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Grayjay.ClientServer.Serializers
{
    public class RatingConverter : JsonConverter<IRating>
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return base.CanConvert(typeToConvert);
        }

        public override IRating? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Utf8JsonReader readerClone = reader;

            if (readerClone.TokenType != JsonTokenType.StartObject)
                throw new JsonException();

            readerClone.Read();
            if (readerClone.TokenType != JsonTokenType.PropertyName)
                throw new JsonException();

            string? propertyName = readerClone.GetString();
            if (propertyName != "type" && propertyName != nameof(IRating.Type))
                throw new JsonException();

            readerClone.Read();
            if (readerClone.TokenType != JsonTokenType.Number)
                throw new JsonException();



            RatingTypes typeDiscriminator = (RatingTypes)readerClone.GetInt32();
            IRating content = typeDiscriminator switch
            {
                RatingTypes.Likes => JsonSerializer.Deserialize<RatingLikes>(ref reader),
                RatingTypes.Dislikes => JsonSerializer.Deserialize<RatingDislikes>(ref reader),
                RatingTypes.Scaler => JsonSerializer.Deserialize<RatingScaler>(ref reader),
                _ => JsonSerializer.Deserialize<RatingLikes>(ref reader)
            };;
            return content;
        }

        public override void Write(Utf8JsonWriter writer, IRating value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value);
        }
    }
}
