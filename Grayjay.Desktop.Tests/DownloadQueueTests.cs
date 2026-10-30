using System.Reflection;
using Grayjay.ClientServer.Models.Downloads;
using Grayjay.ClientServer.States;

namespace Grayjay.Desktop.Tests;

[TestClass]
[DoNotParallelize]
public class DownloadQueueTests
{
    private static Task RunExclusive(Func<Task> runCycle)
    {
        var method = typeof(StateDownloads).GetMethod("RunExclusiveDownloadCycle", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (Task)method.Invoke(null, new object[] { runCycle })!;
    }

    [TestMethod]
    public async Task FailedCycleReleasesGuard()
    {
        await RunExclusive(() => throw new InvalidOperationException("cycle failure"));
        Assert.IsFalse(StateDownloads.IsDownloading);

        int runs = 0;
        await RunExclusive(() =>
        {
            runs++;
            return Task.CompletedTask;
        });
        Assert.AreEqual(1, runs);
        Assert.IsFalse(StateDownloads.IsDownloading);
    }

    [TestMethod]
    public async Task FaultedAsyncCycleReleasesGuard()
    {
        await RunExclusive(async () =>
        {
            await Task.Yield();
            throw new NullReferenceException("cycle failure");
        });
        Assert.IsFalse(StateDownloads.IsDownloading);
    }

    [TestMethod]
    public async Task RequestDuringCycleRunsAnotherCycle()
    {
        var firstCycleBlocked = new TaskCompletionSource();
        var releaseFirstCycle = new TaskCompletionSource();
        int runs = 0;
        int otherRuns = 0;
        Task running = RunExclusive(async () =>
        {
            runs++;
            if (runs == 1)
            {
                firstCycleBlocked.SetResult();
                await releaseFirstCycle.Task;
            }
        });
        await firstCycleBlocked.Task;
        Assert.IsTrue(StateDownloads.IsDownloading);

        await RunExclusive(() =>
        {
            otherRuns++;
            return Task.CompletedTask;
        });
        await RunExclusive(() =>
        {
            otherRuns++;
            return Task.CompletedTask;
        });
        Assert.AreEqual(0, otherRuns, "A second cycle must not run concurrently");

        releaseFirstCycle.SetResult();
        await running;
        Assert.AreEqual(2, runs, "Requests made during the cycle must trigger exactly one more cycle");
        Assert.IsFalse(StateDownloads.IsDownloading);
    }

    [TestMethod]
    public async Task CycleWithoutRequestsRunsOnce()
    {
        int runs = 0;
        await RunExclusive(() =>
        {
            runs++;
            return Task.CompletedTask;
        });
        Assert.AreEqual(1, runs);
        Assert.IsFalse(StateDownloads.IsDownloading);
    }

    [TestMethod]
    public void SelectNextDownloadPrioritizesManualPriorityAndFreshItemsOverDeferredAndFailed()
    {
        var stuckPlaylistItem = new VideoDownload { FailCount = 3, IsDeferred = true, NextRetryAfter = DateTime.UtcNow.AddMinutes(-1) };
        var failedOnceItem = new VideoDownload { FailCount = 1, NextRetryAfter = DateTime.UtcNow.AddMinutes(-1) };
        var normalQueuedItem = new VideoDownload { FailCount = 0, Priority = 0 };
        var prioritizedItem = new VideoDownload { FailCount = 0, Priority = 2 };
        var coolingDownItem = new VideoDownload { FailCount = 1, Priority = 10, NextRetryAfter = DateTime.UtcNow.AddHours(2) };

        var queue = new List<VideoDownload>
        {
            stuckPlaylistItem,
            failedOnceItem,
            normalQueuedItem,
            prioritizedItem,
            coolingDownItem
        };
        var ignore = new List<VideoDownload>();

        var first = StateDownloads.SelectNextDownload(queue, ignore);
        Assert.AreSame(prioritizedItem, first, "Prioritized due item must be selected first");
        ignore.Add(first!);

        var second = StateDownloads.SelectNextDownload(queue, ignore);
        Assert.AreSame(normalQueuedItem, second, "Fresh queued item (FailCount=0) must precede previously failed items");
        ignore.Add(second!);

        var third = StateDownloads.SelectNextDownload(queue, ignore);
        Assert.AreSame(failedOnceItem, third, "Non-deferred retryable item must precede deferred items");
        ignore.Add(third!);

        var fourth = StateDownloads.SelectNextDownload(queue, ignore);
        Assert.AreSame(stuckPlaylistItem, fourth, "Deferred item is checked last only when due");
        ignore.Add(fourth!);

        var fifth = StateDownloads.SelectNextDownload(queue, ignore);
        Assert.IsNull(fifth, "Items with future NextRetryAfter must be skipped");
    }
}
