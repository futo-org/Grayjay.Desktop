using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Grayjay.ClientServer.Serializers
{
    public class GJsonSerializer
    {
        public static GJsonSerializer AndroidCompatible { get; } = new GJsonSerializer(new JsonSerializerOptions()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new UnixDateTimeConverter(),
                new JsonStringEnumConverter()
            }
        });
        public static JsonSerializerOptions Options { get; }
        public JsonSerializerOptions CustomOptions { get; private set; }

        static GJsonSerializer()
        {
            JsonSerializerOptions serializer = new JsonSerializerOptions();

            serializer.Converters.Add(new PlatformContentConverter());
            serializer.Converters.Add(new RatingConverter());
            serializer.Converters.Add(new VideoSourceConverter());
            serializer.Converters.Add(new AudioSourceConverter());
            serializer.Converters.Add(new SubtitleSourceConverter());

            Options = serializer;
        }

        public GJsonSerializer(JsonSerializerOptions options)
        {
            CustomOptions = options;
            options.Converters.Add(new PlatformContentConverter());
            options.Converters.Add(new RatingConverter());
            options.Converters.Add(new VideoSourceConverter());
            options.Converters.Add(new AudioSourceConverter());
            options.Converters.Add(new SubtitleSourceConverter());
        }
        public object DeserializeObj(string json, Type type)
            => JsonSerializer.Deserialize(json, type, CustomOptions);
        public T DeserializeObj<T>(string json)
            => JsonSerializer.Deserialize<T>(json, CustomOptions);
        public T DeserializeObj<T>(byte[] json)
            => JsonSerializer.Deserialize<T>(json, CustomOptions);
        public string SerializeObj<T>(T obj)
            => JsonSerializer.Serialize<T>(obj, CustomOptions);


        public static T Deserialize<T>(string json)
            => JsonSerializer.Deserialize<T>(json, Options);
        public static T Deserialize<T>(byte[] json)
            => JsonSerializer.Deserialize<T>(json, Options);
        public static T Deserialize<T>(ref Utf8JsonReader reader)
            => JsonSerializer.Deserialize<T>(ref reader);

        public static string Serialize<T>(T obj)
            => JsonSerializer.Serialize<T>(obj, Options);

    }
}
