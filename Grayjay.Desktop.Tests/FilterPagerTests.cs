using Grayjay.ClientServer.Pagers;
using Grayjay.Engine.Pagers;

namespace Grayjay.Desktop.Tests;

[TestClass]
public class FilterPagerTests
{
    private class StubPager<T> : IPager<T>
    {
        public string ID { get; set; } = "stub";
        private readonly List<T[]> _pages;
        private int _pageIndex = 0;
        public int NextPageCalls = 0;

        public StubPager(params T[][] pages)
        {
            _pages = pages.ToList();
        }

        public bool HasMorePages() => _pageIndex < _pages.Count - 1;
        public void NextPage() { if (HasMorePages()) _pageIndex++; NextPageCalls++; }
        public T[] GetResults() => _pages[_pageIndex];
    }

    [TestMethod]
    public void TestFilterDropsExcludedItems()
    {
        var pager = new FilterPager<string>(
            new StubPager<string>(new[] { "a", "b", "c" }),
            x => x != "b");

        CollectionAssert.AreEqual(new[] { "a", "c" }, pager.GetResults());
    }

    [TestMethod]
    public void TestFilterDropsEverything()
    {
        var pager = new FilterPager<string>(
            new StubPager<string>(new[] { "a", "b" }),
            x => false);

        CollectionAssert.AreEqual(new string[0], pager.GetResults());
    }

    [TestMethod]
    public void TestFilterPreservesEverything()
    {
        var pager = new FilterPager<string>(
            new StubPager<string>(new[] { "a", "b" }),
            x => true);

        CollectionAssert.AreEqual(new[] { "a", "b" }, pager.GetResults());
    }

    [TestMethod]
    public void TestHasMorePagesAndNextPageDelegateToUnderlying()
    {
        var inner = new StubPager<string>(
            new[] { "a", "b" },
            new[] { "c", "d" },
            new[] { "e" });
        var pager = new FilterPager<string>(inner, x => x != "d");

        Assert.IsTrue(pager.HasMorePages());
        pager.NextPage();
        Assert.IsTrue(inner.NextPageCalls == 1);
        CollectionAssert.AreEqual(new[] { "c" }, pager.GetResults());

        pager.NextPage();
        Assert.IsFalse(pager.HasMorePages());
        CollectionAssert.AreEqual(new[] { "e" }, pager.GetResults());
    }

    [TestMethod]
    public void TestFindPagerMatchesWrappedPager()
    {
        var inner = new StubPager<string>(new[] { "a" }) { ID = "inner" };
        var pager = new FilterPager<string>(inner, x => true);

        var found = pager.FindPager(p => p.ID == "inner");
        Assert.AreSame(inner, found);
    }

    [TestMethod]
    public void TestFindPagerRecursesIntoNested()
    {
        var leaf = new StubPager<string>(new[] { "a" }) { ID = "leaf" };
        var nested = new FilterPager<string>(leaf, x => true);
        var outer = new FilterPager<string>(nested, x => true);

        var found = outer.FindPager(p => ReferenceEquals(p, leaf));
        Assert.AreSame(leaf, found);
    }

    [TestMethod]
    public void TestFindPagerReturnsNullWhenNoMatch()
    {
        var inner = new StubPager<string>(new[] { "a" }) { ID = "inner" };
        var pager = new FilterPager<string>(inner, x => true);

        Assert.IsNull(pager.FindPager(p => p.ID == "missing"));
    }

    [TestMethod]
    public void TestIDSetterDelegatesToUnderlying()
    {
        var inner = new StubPager<string>(new[] { "a" }) { ID = "old" };
        var pager = new FilterPager<string>(inner, x => true);

        pager.ID = "new";
        Assert.AreEqual("new", inner.ID);
    }
}
