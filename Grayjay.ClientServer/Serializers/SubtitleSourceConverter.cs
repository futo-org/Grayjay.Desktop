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
    public enum SubtitleConvertBehavior
    {
        Null = 0,
        Fetch = 1,
        Serialize = 2
    }

    public class SubtitleSourceConverter : JsonConverter<SubtitleSource>
    {
        private SubtitleConvertBehavior _subBehavior = SubtitleConvertBehavior.Null;
        public SubtitleSourceConverter(SubtitleConvertBehavior subBehavior = SubtitleConvertBehavior.Null)
        {
            _subBehavior = subBehavior;
        }

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
            if (value is SubtitleRawSource raw)
            {
                JsonSerializer.Serialize(writer, raw);
            }
            else if (value.HasFetch)
            {
                switch (_subBehavior)
                {
                    case SubtitleConvertBehavior.Null:
                        writer.WriteNullValue();
                        break;
                    case SubtitleConvertBehavior.Serialize:
                        JsonSerializer.Serialize(writer, new SubtitleSource.Serializable()
                        {
                            Url = value.Url,
                            Name = value.Name,
                            Format = value.Format,
                            HasFetch = true
                        });
                        break;
                    case SubtitleConvertBehavior.Fetch:
                        var rawSubs = value.ToRaw();
                        JsonSerializer.Serialize(writer, rawSubs);
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
            else
                JsonSerializer.Serialize(writer, new SubtitleSource.Serializable(value));
        }
    }
}