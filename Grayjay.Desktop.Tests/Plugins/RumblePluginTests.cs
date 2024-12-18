using Grayjay.Engine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Grayjay.Desktop.Tests.Plugins
{
    [TestClass]
    public class RumblePluginTests
    {


        [TestMethod]
        public void TestHome()
        {
            var plugin = GrayjayPlugin.FromUrl("https://plugins.grayjay.app/Rumble/RumbleConfig.json");
            plugin.Initialize();

            plugin.OnLog += (a, b) =>
            {
                Console.WriteLine(b);
            };

            var home = plugin.GetHome();

            var results = home.GetResults();
            Assert.IsTrue(results.Length > 1);
            Assert.IsNotNull(results[0].Name);
            Assert.IsNotNull(results[0].Url);
            Assert.IsNotNull(results[1].Name);
            Assert.IsNotNull(results[1].Url);

            Console.WriteLine(JsonSerializer.Serialize(results));
        }

        [TestMethod]
        public void TestReplies()
        {
            var plugin = GrayjayPlugin.FromUrl("https://plugins.grayjay.app/Rumble/RumbleConfig.json");
            plugin.Initialize();

            plugin.OnLog += (a, b) =>
            {
                Console.WriteLine(b);
            };

            var home = plugin.GetHome();

            var results = home.GetResults();
            Assert.IsTrue(results.Length > 1);

            var video = results.FirstOrDefault();

            var videoDetails = plugin.GetContentDetails(video.Url);

            var comments = plugin.GetComments(videoDetails.Url).GetResults();
            foreach (var comment in comments.Take(5))
            {
                var replies = comment.GetReplies();
                Assert.IsNotNull(replies);
                if(replies.GetResults().Length > 0)
                {
                    Console.WriteLine(JsonSerializer.Serialize(replies));
                    return;
                }
            }
            throw new InvalidDataException("No replies found");
        }
    }
}
