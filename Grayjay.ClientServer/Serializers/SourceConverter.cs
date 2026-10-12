using Grayjay.ClientServer.Models.Downloads;
using Grayjay.Engine.Models;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Models.Video.Sources;
using Grayjay.Desktop.POC;
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
            string typeDiscriminator = SourceTypeReader.ReadTypeDiscriminator(reader, "VideoSource");
            IVideoSource content = typeDiscriminator switch
            {
                "VideoUrlSource" => GJsonSerializer.Deserialize<VideoUrlSource>(ref reader),
                "VideoUrlRangeSource" => GJsonSerializer.Deserialize<VideoUrlRangeSource>(ref reader),
                "HLSSource" => GJsonSerializer.Deserialize<HLSManifestSource>(ref reader),
                "LocalVideoSource" => GJsonSerializer.Deserialize<LocalVideoSource>(ref reader),
                "DashRawSource" => GJsonSerializer.Deserialize<DashManifestRawSource>(ref reader),
                "VideoSourceDescription" => GJsonSerializer.Deserialize<VideoSourceDescription>(ref reader),
                _ => SourceTypeReader.ReadPlaceholder<VideoSourceDescription>(ref reader, typeDiscriminator, "VideoSource")
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
            string typeDiscriminator = SourceTypeReader.ReadTypeDiscriminator(reader, "AudioSource");
            IAudioSource content = typeDiscriminator switch
            {
                "AudioUrlSource" => GJsonSerializer.Deserialize<AudioUrlSource>(ref reader),
                "AudioUrlRangeSource" => GJsonSerializer.Deserialize<AudioUrlRangeSource>(ref reader),
                "HLSSource" => GJsonSerializer.Deserialize<HLSManifestAudioSource>(ref reader),
                "LocalAudioSource" => GJsonSerializer.Deserialize<LocalAudioSource>(ref reader),
                "DashRawAudioSource" => GJsonSerializer.Deserialize<DashManifestRawAudioSource>(ref reader),
                "AudioSourceDescription" => GJsonSerializer.Deserialize<AudioSourceDescription>(ref reader),
                _ => SourceTypeReader.ReadPlaceholder<AudioSourceDescription>(ref reader, typeDiscriminator, "AudioSource")
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
    internal static class SourceTypeReader
    {
        //Takes the reader by value, so the caller's reader stays at the start of the object
        public static string ReadTypeDiscriminator(Utf8JsonReader reader, string sourceName)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException();

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                string? propertyName = reader.GetString();
                reader.Read();
                if (propertyName == "Type" && reader.TokenType == JsonTokenType.String)
                    return reader.GetString()!;
                reader.Skip();
            }
            throw new JsonException($"Missing Type property in {sourceName}");
        }

        //Unsupported entries are kept as descriptions so the rest of the saved object still loads
        public static TDescription ReadPlaceholder<TDescription>(ref Utf8JsonReader reader, string typeDiscriminator, string sourceName)
        {
            Logger.w(nameof(SourceTypeReader), $"Unsupported {sourceName} type [{typeDiscriminator}], loading it as a description");
            return GJsonSerializer.Deserialize<TDescription>(ref reader);
        }
    }
}
