using Grayjay.ClientServer.Constants;
using Grayjay.ClientServer.Models;
using Grayjay.ClientServer.States;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Models.General;
using Grayjay.Engine.Pagers;
using System.Reflection;
using PlatformID = Grayjay.Engine.Models.General.PlatformID;

namespace Grayjay.Desktop.Tests;

[TestClass]
public class BlockedChannelsTests
{
    private static FieldInfo _baseDirectoryField =
        typeof(Directories).GetField("_baseDirectory", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static object? _originalBaseDirectory;
    private static string? _testDirectory;

    private static void RedirectBaseDirectory()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "grayjay_blocked_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
        _originalBaseDirectory = _baseDirectoryField.GetValue(null);
        _baseDirectoryField.SetValue(null, _testDirectory);
    }

    private static void RestoreBaseDirectory()
    {
        _baseDirectoryField.SetValue(null, _originalBaseDirectory);
        _originalBaseDirectory = null;
        if (_testDirectory != null && Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, true);
            }
            catch
            {
                // Ignored
            }
            _testDirectory = null;
        }
    }

    [TestCleanup]
    public void Cleanup()
    {
        RestoreBaseDirectory();
    }

    private static StateBlockedChannels CreateState() => new StateBlockedChannels();

    private static BlockedChannel Block(string url, string? name = null, string[]? alternatives = null, long blockedTime = 0, string? channelId = null)
    {
        return new BlockedChannel()
        {
            Url = url,
            Name = name ?? url,
            Thumbnail = null,
            PluginId = "test",
            UrlAlternatives = alternatives?.ToList() ?? new List<string>(),
            BlockedTime = blockedTime,
            ChannelId = channelId
        };
    }

    private class StubContentPager<T> : IPager<T>
    {
        public string ID { get; set; } = "stub";
        private readonly T[][] _pages;
        private int _pageIndex = 0;

        public StubContentPager(params T[][] pages)
        {
            _pages = pages;
        }

        public bool HasMorePages() => _pageIndex < _pages.Length - 1;
        public void NextPage() { if (HasMorePages()) _pageIndex++; }
        public T[] GetResults() => _pages[_pageIndex];
    }

    [TestMethod]
    public void TestModel_IsSameUrlCaseInsensitive()
    {
        var blocked = Block("HTTPS://EXAMPLE.COM/CHANNEL");

        Assert.IsTrue(blocked.IsSameUrl("https://example.com/channel"));
        Assert.IsTrue(blocked.IsSameUrl("HTTPS://EXAMPLE.COM/CHANNEL"));
        Assert.IsFalse(blocked.IsSameUrl("https://other.com/channel"));
    }

    [TestMethod]
    public void TestModel_IsSameUrlAlternatives()
    {
        var blocked = Block("https://example.com/channel", alternatives: new[] { "https://example.com/c/alt", "https://example.com/@handle" });

        Assert.IsTrue(blocked.IsSameUrl("https://example.com/c/alt"));
        Assert.IsTrue(blocked.IsSameUrl("https://example.com/@handle"));
        Assert.IsFalse(blocked.IsSameUrl("https://example.com/nope"));
    }

    [TestMethod]
    public void TestModel_IsSameUrlList()
    {
        var blocked = Block("https://example.com/channel", alternatives: new[] { "https://example.com/@handle" });

        Assert.IsTrue(blocked.IsSameUrl(new[] { "https://example.com/CHANNEL" }));
        Assert.IsTrue(blocked.IsSameUrl(new[] { "https://example.com/@handle" }));
        Assert.IsFalse(blocked.IsSameUrl(new[] { "https://example.com/other" }));
    }

    [TestMethod]
    public void TestModel_IsSameUrlEmpty()
    {
        var blocked = Block("https://example.com/channel");

        Assert.IsFalse(blocked.IsSameUrl(""));
        Assert.IsFalse(blocked.IsSameUrl((string)null));
        Assert.IsFalse(blocked.IsSameUrl(new string[0]));
    }

    [TestMethod]
    public void TestState_AddAndIsBlocked()
    {
        RedirectBaseDirectory();
        var state = CreateState();

        state.Add(Block("HTTPS://EXAMPLE.COM/CHANNEL"), time: DateTimeOffset.FromUnixTimeSeconds(100));

        Assert.IsTrue(state.IsBlocked("https://example.com/channel"));
        Assert.IsTrue(state.IsBlocked("HTTPS://EXAMPLE.COM/CHANNEL"));
        Assert.IsFalse(state.IsBlocked("https://example.com/other"));
        Assert.AreEqual(1, state.GetBlocked().Count);
        Assert.AreEqual("https://example.com/channel", state.GetBlockedUrls().Single());
    }

    [TestMethod]
    public void TestState_IsBlockedAlternatives()
    {
        RedirectBaseDirectory();
        var state = CreateState();

        state.Add(Block("https://example.com/channel", alternatives: new[] { "https://example.com/@handle" }));

        Assert.IsTrue(state.IsBlocked("https://example.com/@handle"));
        Assert.IsTrue(state.IsBlocked(new[] { "https://example.com/@handle", "https://example.com/nope" }));
        Assert.IsFalse(state.IsBlocked(new[] { "https://example.com/nope" }));
    }

    [TestMethod]
    public void TestState_AddSameUrlIsUnique()
    {
        RedirectBaseDirectory();
        var state = CreateState();

        state.Add(Block("https://example.com/channel", "First"), time: DateTimeOffset.FromUnixTimeSeconds(100));
        state.Add(Block("https://example.com/channel", "Second"), time: DateTimeOffset.FromUnixTimeSeconds(200));

        Assert.AreEqual(1, state.GetBlocked().Count);
        Assert.AreEqual("Second", state.GetBlocked().Single().Name);
        Assert.AreEqual(200, state.GetBlocked().Single().BlockedTime);
    }

    [TestMethod]
    public void TestState_RemoveAndTombstone()
    {
        RedirectBaseDirectory();
        var state = CreateState();

        state.Add(Block("https://example.com/channel", blockedTime: 100));
        state.Remove("https://example.com/channel", time: DateTimeOffset.FromUnixTimeSeconds(500));

        Assert.IsFalse(state.IsBlocked("https://example.com/channel"));
        Assert.AreEqual(0, state.GetBlocked().Count);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(500), state.GetBlockedRemovalTime("https://example.com/channel"));
    }

    [TestMethod]
    public void TestState_ReaddClearsRemovalTombstone()
    {
        RedirectBaseDirectory();
        var state = CreateState();

        state.Add(Block("https://example.com/channel", blockedTime: 100));
        state.Remove("https://example.com/channel", time: DateTimeOffset.FromUnixTimeSeconds(500));
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(500), state.GetBlockedRemovalTime("https://example.com/channel"));

        state.Add(Block("https://example.com/channel", blockedTime: 600));
        Assert.IsTrue(state.IsBlocked("https://example.com/channel"));
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(0), state.GetBlockedRemovalTime("https://example.com/channel"));
    }

    [TestMethod]
    public void TestState_GetBlockedOrderedNewestFirst()
    {
        RedirectBaseDirectory();
        var state = CreateState();

        state.Add(Block("https://example.com/a", blockedTime: 100));
        state.Add(Block("https://example.com/b", blockedTime: 300));
        state.Add(Block("https://example.com/c", blockedTime: 200));

        var urls = state.GetBlocked().Select(x => x.Url).ToList();
        CollectionAssert.AreEqual(new[] { "https://example.com/b", "https://example.com/c", "https://example.com/a" }, urls);
    }

    [TestMethod]
    public void TestState_ReplaceAll()
    {
        RedirectBaseDirectory();
        var state = CreateState();

        state.Add(Block("https://example.com/a", blockedTime: 100));
        state.Add(Block("https://example.com/b", blockedTime: 200));

        state.ReplaceAll(new List<BlockedChannel>()
        {
            Block("https://example.com/x", blockedTime: 300)
        });

        Assert.AreEqual(1, state.GetBlocked().Count);
        Assert.AreEqual("https://example.com/x", state.GetBlocked().Single().Url);
    }

    [TestMethod]
    public void TestState_PersistsAcrossInstances()
    {
        RedirectBaseDirectory();
        var state = CreateState();
        state.Add(Block("https://example.com/channel", blockedTime: 100));

        var reloaded = CreateState();
        Assert.IsTrue(reloaded.IsBlocked("https://example.com/channel"));
    }

    [TestMethod]
    public void TestIsBlockedMatchesChannelIdWhenUrlVariantDiffers()
    {
        RedirectBaseDirectory();
        var state = CreateState();

        state.Add(Block("https://www.youtube.com/channel/UCABC123", channelId: "UCABC123"));

        var author = new PlatformAuthorLink()
        {
            ID = new PlatformID() { Platform = "YouTube", Value = "UCABC123" },
            Name = "Blocked",
            Url = "https://www.youtube.com/@handle"
        };
        Assert.IsTrue(state.IsBlocked(author));

        var other = new PlatformAuthorLink()
        {
            ID = new PlatformID() { Platform = "YouTube", Value = "UCOTHER999" },
            Name = "Other",
            Url = "https://www.youtube.com/@other"
        };
        Assert.IsFalse(state.IsBlocked(other));
    }

    [TestMethod]
    public void TestIsBlockedMatchesUrlWhenChannelIdMissing()
    {
        RedirectBaseDirectory();
        var state = CreateState();

        state.Add(Block("https://example.com/channel"));

        var author = new PlatformAuthorLink()
        {
            Name = "Test",
            Url = "https://example.com/channel"
        };
        Assert.IsTrue(state.IsBlocked(author));
    }

    [TestMethod]
    public void TestIsBlockedMatchesDerivedChannelIdFromUrl()
    {
        RedirectBaseDirectory();
        var state = CreateState();

        state.Add(Block("https://www.youtube.com/channel/UCABC123"));

        var author = new PlatformAuthorLink()
        {
            Name = "Test",
            Url = "https://www.youtube.com/channel/UCABC123?sub_confirmation=1"
        };
        Assert.IsTrue(state.IsBlocked(author));

        var other = new PlatformAuthorLink()
        {
            Name = "Other",
            Url = "https://www.youtube.com/channel/UCOTHER999"
        };
        Assert.IsFalse(state.IsBlocked(other));
    }

    [TestMethod]
    public void TestFilterPagerDropsBlockedChannelContent()
    {
        RedirectBaseDirectory();
        var state = CreateState();

        state.Add(Block("https://www.youtube.com/channel/UCABC123", channelId: "UCABC123"));

        var blockedAuthor = new PlatformAuthorLink()
        {
            ID = new PlatformID() { Platform = "YouTube", Value = "UCABC123" },
            Name = "Blocked",
            Url = "https://www.youtube.com/@blocked"
        };
        var allowedAuthor = new PlatformAuthorLink()
        {
            ID = new PlatformID() { Platform = "YouTube", Value = "UCOTHER999" },
            Name = "Allowed",
            Url = "https://www.youtube.com/@allowed"
        };

        var pager = new FilterPager<PlatformContent>(
            new StubContentPager<PlatformContent>(new[]
            {
                new PlatformVideo() { Name = "blocked-video", Author = blockedAuthor },
                new PlatformVideo() { Name = "allowed-video", Author = allowedAuthor }
            }),
            content => !state.IsBlocked(content.Author));

        CollectionAssert.AreEqual(new[] { "allowed-video" }, pager.GetResults().Select(x => x.Name).ToArray());
    }
}
