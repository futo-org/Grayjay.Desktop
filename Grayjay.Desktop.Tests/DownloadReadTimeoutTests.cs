using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Grayjay.ClientServer;
using Grayjay.ClientServer.Helpers;
using Grayjay.ClientServer.Models.Downloads;
using Grayjay.Engine.Packages;
using Grayjay.Engine.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Grayjay.Desktop.Tests;

[TestClass]
public class DownloadReadTimeoutTests
{
    // Returns one byte per read after the given delay; the delay honours the read's cancellation token.
    private sealed class DelayedStream(TimeSpan delay, int length) : Stream
    {
        private int _remaining = length;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken);
            if (_remaining == 0)
            {
                return 0;
            }
            _remaining--;
            buffer.Span[0] = 42;
            return 1;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [TestMethod]
    public void TimeBetweenReadsIsNotCounted()
    {
        using var stream = new IdleTimeoutStream(new DelayedStream(TimeSpan.FromMilliseconds(10), 3), TimeSpan.FromMilliseconds(200));
        var buffer = new byte[16];
        for (int index = 0; index < 3; index++)
        {
            Assert.AreEqual(1, stream.Read(buffer, 0, buffer.Length));
            Thread.Sleep(400);
        }
        Assert.AreEqual(0, stream.Read(buffer, 0, buffer.Length));
    }

    [TestMethod]
    public void SlowReadFailsWithIOException()
    {
        using var stream = new IdleTimeoutStream(new DelayedStream(TimeSpan.FromMinutes(5), 1), TimeSpan.FromMilliseconds(200));
        var stopwatch = Stopwatch.StartNew();
        Assert.ThrowsException<IOException>(() => stream.Read(new byte[16], 0, 16));
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Timed out after {stopwatch.Elapsed}");
    }

    [TestMethod]
    public void CancelFailsWithOperationCanceled()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        using var stream = new IdleTimeoutStream(new DelayedStream(TimeSpan.FromMinutes(5), 1), TimeSpan.FromMinutes(5), cancellation.Token);
        Exception? error = CaptureException(() => stream.Read(new byte[16], 0, 16));
        Assert.IsInstanceOfType(error, typeof(OperationCanceledException));
    }

    [TestMethod]
    public async Task ReadAsyncCancelFailsWithOperationCanceled()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        using var stream = new IdleTimeoutStream(new DelayedStream(TimeSpan.FromMinutes(5), 1), TimeSpan.FromMinutes(5));
        Exception? error = null;
        try
        {
            await stream.ReadAsync(new byte[16], cancellation.Token);
        }
        catch (Exception exception)
        {
            error = exception;
        }
        Assert.IsInstanceOfType(error, typeof(OperationCanceledException));
    }

    private static Exception? CaptureException(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task<(WebApplication app, string url)> StartServer(Func<HttpContext, int, Task> handler)
    {
        int requestCount = 0;
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapGet("/source", context => handler(context, Interlocked.Increment(ref requestCount)));
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return (app, address + "/source");
    }

    private static async Task StopServer(WebApplication app)
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    private static async Task WritePartialThenStall(HttpContext context, byte[] bytes, int start, int count)
    {
        context.Response.ContentLength = bytes.Length - start;
        await context.Response.Body.WriteAsync(bytes.AsMemory(start, count));
        await context.Response.Body.FlushAsync();
        try
        {
            await Task.Delay(Timeout.Infinite, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
        }
    }

    [TestMethod]
    public async Task StalledBodyFailsAfterIdleTimeout()
    {
        var bytes = new byte[4096];
        var (app, url) = await StartServer((context, _) => WritePartialThenStall(context, bytes, 0, 100));
        try
        {
            var stopwatch = Stopwatch.StartNew();
            Assert.ThrowsException<IOException>(() =>
                ModifierHttp.GetBytes(new ManagedHttpClient(), url, readIdleTimeout: TimeSpan.FromMilliseconds(500)));
            Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Timed out after {stopwatch.Elapsed}");

            var result = ModifierHttp.GetStream(new ManagedHttpClient(), url, readIdleTimeout: TimeSpan.FromMilliseconds(500));
            using var stream = result.Stream;
            Assert.ThrowsException<IOException>(() => stream.CopyTo(Stream.Null));
        }
        finally
        {
            await StopServer(app);
        }
    }

    [TestMethod]
    public async Task CancelInterruptsStalledBody()
    {
        var bytes = new byte[4096];
        var (app, url) = await StartServer((context, _) => WritePartialThenStall(context, bytes, 0, 100));
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            var stopwatch = Stopwatch.StartNew();
            Exception? error = CaptureException(() =>
                ModifierHttp.GetBytes(new ManagedHttpClient(), url, cancel: cancellation.Token, readIdleTimeout: TimeSpan.FromMinutes(5)));
            Assert.IsInstanceOfType(error, typeof(OperationCanceledException));
            Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Cancelled after {stopwatch.Elapsed}");
        }
        finally
        {
            await StopServer(app);
        }
    }

    [TestMethod]
    public async Task BodyIsUnchangedWithAndWithoutTimeout()
    {
        var bytes = Enumerable.Range(0, 50000).Select(index => (byte)(index * 7)).ToArray();
        var (app, url) = await StartServer(async (context, _) =>
        {
            context.Response.ContentLength = bytes.Length;
            await context.Response.Body.WriteAsync(bytes);
        });
        try
        {
            CollectionAssert.AreEqual(bytes, ModifierHttp.GetBytes(new ManagedHttpClient(), url).Bytes);
            CollectionAssert.AreEqual(bytes, ModifierHttp.GetBytes(new ManagedHttpClient(), url, readIdleTimeout: TimeSpan.FromSeconds(5)).Bytes);
        }
        finally
        {
            await StopServer(app);
        }
    }

    [TestMethod]
    public async Task RangeRequestRetriesAfterStall()
    {
        var bytes = Enumerable.Range(0, 4000).Select(index => (byte)(index * 13)).ToArray();
        var (app, url) = await StartServer(async (context, requestNumber) =>
        {
            if (requestNumber == 1)
            {
                await WritePartialThenStall(context, bytes, 0, 1000);
                return;
            }
            context.Response.StatusCode = 206;
            context.Response.Headers.ContentRange = $"bytes 0-{bytes.Length - 1}/{bytes.Length}";
            context.Response.ContentLength = bytes.Length;
            await context.Response.Body.WriteAsync(bytes);
        });
        try
        {
            var download = new VideoDownload();
            typeof(VideoDownload).GetProperty("ReadIdleTimeout", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(download, TimeSpan.FromMilliseconds(500));
            var method = typeof(VideoDownload).GetMethod("RequestByteRange", BindingFlags.NonPublic | BindingFlags.Instance)!;
            (byte[] data, long start, long end) result;
            try
            {
                result = ((byte[], long, long))method.Invoke(download, new object?[]
                {
                    new ManagedHttpClient(), null, url, 0L, (long)bytes.Length - 1, CancellationToken.None
                })!;
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
            CollectionAssert.AreEqual(bytes, result.data);
        }
        finally
        {
            await StopServer(app);
        }
    }

    [TestMethod]
    public async Task RangeRetryWaitHonoursCancel()
    {
        var (app, url) = await StartServer((context, _) =>
        {
            context.Response.StatusCode = 500;
            return Task.CompletedTask;
        });
        try
        {
            var method = typeof(VideoDownload).GetMethod("RequestByteRange", BindingFlags.NonPublic | BindingFlags.Instance)!;
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            var stopwatch = Stopwatch.StartNew();
            Exception? error = CaptureException(() =>
            {
                try
                {
                    method.Invoke(new VideoDownload(), new object?[] { new ManagedHttpClient(), null, url, 0L, 99L, cancellation.Token });
                }
                catch (TargetInvocationException exception) when (exception.InnerException != null)
                {
                    ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                    throw;
                }
            });
            Assert.IsInstanceOfType(error, typeof(OperationCanceledException));
            // The shortest retry wait is one second, so finishing sooner means the wait itself was cancelled.
            Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromMilliseconds(900), $"Cancelled after {stopwatch.Elapsed}");
        }
        finally
        {
            await StopServer(app);
        }
    }

    [TestMethod]
    public async Task SequentialDownloadResumesAfterStall()
    {
        var bytes = Enumerable.Range(0, 16000).Select(index => (byte)(index * 31)).ToArray();
        var ranges = new List<string>();
        var (app, url) = await StartServer(async (context, requestNumber) =>
        {
            var range = context.Request.Headers.Range.ToString();
            lock (ranges)
            {
                ranges.Add(range);
            }
            if (requestNumber == 1)
            {
                await WritePartialThenStall(context, bytes, 0, 2000);
                return;
            }
            int start = int.Parse(range[6..^1]);
            context.Response.StatusCode = 206;
            context.Response.Headers.ContentRange = $"bytes {start}-{bytes.Length - 1}/{bytes.Length}";
            context.Response.ContentLength = bytes.Length - start;
            await context.Response.Body.WriteAsync(bytes.AsMemory(start));
        });
        try
        {
            var download = new VideoDownload();
            typeof(VideoDownload).GetProperty("ReadIdleTimeout", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(download, TimeSpan.FromMilliseconds(500));
            using var output = new MemoryStream();
            var method = typeof(VideoDownload).GetMethod("DownloadSourceSequential", BindingFlags.NonPublic | BindingFlags.Instance)!;
            long length;
            try
            {
                length = (long)method.Invoke(download, new object?[]
                {
                    new ManagedHttpClient(), null, output, url, null, CancellationToken.None
                })!;
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
            Assert.AreEqual((long)bytes.Length, length);
            CollectionAssert.AreEqual(bytes, output.ToArray());
            CollectionAssert.AreEqual(new[] { "", "bytes=2000-" }, ranges);
        }
        finally
        {
            await StopServer(app);
        }
    }

    private static Libcurl.Response PerformImpersonated(Libcurl.Request request, CancellationToken cancel, Func<Libcurl.Request, Libcurl.Response> perform)
    {
        var method = typeof(ModifierHttp).GetMethod("PerformImpersonated", BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            return (Libcurl.Response)method.Invoke(null, new object?[] { request, cancel, perform })!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static Libcurl.Request ImpersonatedRequest() => new Libcurl.Request { Url = "https://test/segment.ts", Method = "GET" };

    [TestMethod]
    public void CancellingAnImpersonatedRequestStopsWaitingForIt()
    {
        using var release = new ManualResetEventSlim(false);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var stopwatch = Stopwatch.StartNew();
        try
        {
            Assert.ThrowsException<TaskCanceledException>(() => PerformImpersonated(ImpersonatedRequest(), cancellation.Token, request =>
            {
                release.Wait(TimeSpan.FromSeconds(30));
                return new Libcurl.Response { Status = 200, BodyBytes = Array.Empty<byte>() };
            }));
            Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Cancel took {stopwatch.Elapsed}");
        }
        finally
        {
            release.Set();
        }
    }

    [TestMethod]
    public void ImpersonatedRequestsReturnTheirResponseAndErrors()
    {
        bool performed = false;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsException<OperationCanceledException>(() => PerformImpersonated(ImpersonatedRequest(), cancelled.Token, request =>
        {
            performed = true;
            return new Libcurl.Response { Status = 200, BodyBytes = Array.Empty<byte>() };
        }));
        Assert.IsFalse(performed, "An already cancelled request is not sent");

        using var open = new CancellationTokenSource();
        var body = new byte[] { 1, 2, 3 };
        foreach (var token in new[] { CancellationToken.None, open.Token })
        {
            var response = PerformImpersonated(ImpersonatedRequest(), token, request => new Libcurl.Response { Status = 206, BodyBytes = body });
            Assert.AreEqual(206, response.Status);
            CollectionAssert.AreEqual(body, response.BodyBytes);
            Assert.ThrowsException<InvalidOperationException>(() => PerformImpersonated(ImpersonatedRequest(), token, request => throw new InvalidOperationException("curl_easy_perform failed")));
        }
    }
}
