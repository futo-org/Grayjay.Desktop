using System.Reflection;
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
}
