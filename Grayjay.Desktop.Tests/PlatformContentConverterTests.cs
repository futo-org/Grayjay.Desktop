using System.Text.Json;
using Grayjay.ClientServer.Serializers;
using Grayjay.Engine.Models.Feed;

namespace Grayjay.Desktop.Tests
{
    [TestClass]
    public class PlatformContentConverterTests
    {
        [TestMethod]
        public void Deserialize_PlatformVideo_WhenContentTypeIsNotFirstProperty()
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new PlatformContentConverter());

            const string json = """
            {
                "Description": "Plugin-specific metadata before the discriminator",
                "ContentType": 1,
                "Name": "Example video",
                "Author": {
                    "Name": "Example channel",
                    "Url": "https://example.com/channel"
                },
                "Url": "https://example.com/video",
                "Duration": 120,
                "ViewCount": 42
            }
            """;

            var content = JsonSerializer.Deserialize<PlatformContent>(json, options);

            Assert.IsInstanceOfType(content, typeof(PlatformVideo));
            Assert.AreEqual("Example video", content!.Name);
            Assert.AreEqual("https://example.com/video", content.Url);
        }
    }
}
