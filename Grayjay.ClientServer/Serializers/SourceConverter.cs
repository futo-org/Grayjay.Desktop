using Grayjay.ClientServer.Models.Downloads;
using Grayjay.Engine.Models;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Models.Video.Sources;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Grayjay.ClientServer.Serializers
{
    public class VideoSourceConverter : JsonConverter<IVideoSource>
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return base.CanConvert(typeToConvert);
        }

        public override IVideoSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Utf8JsonReader readerClone = reader;

            if (readerClone.TokenType != JsonTokenType.StartObject)
                throw new JsonException();

            readerClone.Read();
            if (readerClone.TokenType != JsonTokenType.PropertyName)
                throw new JsonException();

            string? propertyName = readerClone.GetString();
            if (propertyName != nameof(IVideoSource.Type))
                throw new JsonException("Missing Type property in VideoSource");

            readerClone.Read();
            if (readerClone.TokenType != JsonTokenType.String)
                throw new JsonException();

            string typeDiscriminator = readerClone.GetString();
            IVideoSource content = typeDiscriminator switch
            {
                "VideoUrlSource" => GJsonSerializer.Deserialize<VideoUrlSource>(ref reader),
                "VideoUrlRangeSource" => GJsonSerializer.Deserialize<VideoUrlRangeSource>(ref reader),
                "HLSSource" => GJsonSerializer.Deserialize<HLSManifestSource>(ref reader),
                "LocalVideoSource" => GJsonSerializer.Deserialize<LocalVideoSource>(ref reader),
                "DashRawSource" => GJsonSerializer.Deserialize<DashManifestRawSource>(ref reader),
                "VideoSourceDescription" => GJsonSerializer.Deserialize<VideoSourceDescription>(ref reader),
                _ => throw new NotImplementedException()
            };
            return content;
        }

        public override void Write(Utf8JsonWriter writer, IVideoSource value, JsonSerializerOptions options)
        {
            if (value is JSSource jsSource && !jsSource.CanSerialize)
                JsonSerializer.Serialize(writer, VideoSourceDescription.FromSource(value));
            else
                JsonSerializer.Serialize(writer, value);
        }
    }
    public class AudioSourceConverter : JsonConverter<IAudioSource>
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return base.CanConvert(typeToConvert);
        }

        public override IAudioSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Utf8JsonReader readerClone = reader;

            if (readerClone.TokenType != JsonTokenType.StartObject)
                throw new JsonException();

            readerClone.Read();
            if (readerClone.TokenType != JsonTokenType.PropertyName)
                throw new JsonException();

            string? propertyName = readerClone.GetString();
            if (propertyName != nameof(IAudioSource.Type))
                throw new JsonException();

            readerClone.Read();
            if (readerClone.TokenType != JsonTokenType.String)
                throw new JsonException();

            string typeDiscriminator = readerClone.GetString();
            IAudioSource content = typeDiscriminator switch
            {
                "AudioUrlSource" => GJsonSerializer.Deserialize<AudioUrlSource>(ref reader),
                "AudioUrlRangeSource" => GJsonSerializer.Deserialize<AudioUrlRangeSource>(ref reader),
                "HLSSource" => GJsonSerializer.Deserialize<HLSManifestAudioSource>(ref reader),
                "LocalAudioSource" => GJsonSerializer.Deserialize<LocalAudioSource>(ref reader),
                "DashRawAudioSource" => GJsonSerializer.Deserialize<DashManifestRawAudioSource>(ref reader),
                "AudioSourceDescription" => GJsonSerializer.Deserialize<AudioSourceDescription>(ref reader),
                _ => throw new NotImplementedException()
            };
            return content;
        }

        public override void Write(Utf8JsonWriter writer, IAudioSource value, JsonSerializerOptions options)
        {
            if (value is JSSource jsSource && !jsSource.CanSerialize)
                JsonSerializer.Serialize(writer, AudioSourceDescription.FromSource(value));
            else
                JsonSerializer.Serialize(writer, value);
        }
    }
}
