using Grayjay.ClientServer.Models.Downloads;
using Grayjay.Engine.Models;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Models.Subtitles;
using Grayjay.Engine.Models.Video.Sources;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Grayjay.ClientServer.Serializers
{
    public class SubtitleSourceConverter : JsonConverter<SubtitleSource>
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return base.CanConvert(typeToConvert);
        }

        public override SubtitleSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return GJsonSerializer.Deserialize<SubtitleSource.Serializable>(ref reader);
        }

        public override void Write(Utf8JsonWriter writer, SubtitleSource value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, new SubtitleSource.Serializable(value));
        }
    }
}