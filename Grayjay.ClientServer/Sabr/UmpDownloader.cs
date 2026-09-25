using System.Collections.Concurrent;
using System.Diagnostics;
using Grayjay.ClientServer.Sabr.Proto;
using Grayjay.Desktop.POC;
using Grayjay.Engine.Models.Video.Sources;

namespace Grayjay.ClientServer.Sabr
{
    public static class UmpDownloader
    {
        private const string TAG = "UmpDownloader";
        private const int PARALLEL_MIN_SLICE_SEC = 30;
        private const int MAX_CONCURRENCY = 6;
        private const long DOWNLOAD_KEEP_BEHIND_US = 0;
        private static readonly TimeSpan POLL = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan MAX_STALL = TimeSpan.FromSeconds(90);

        public class Result
        {
            public long Length { get; init; }
            public FormatInitializationMetadata? FormatInitialization { get; init; }

            public StreamMetaData? ToStreamMetaData()
            {
                var init = FormatInitialization;
                if (init?.InitRange == null || init.IndexRange == null || init.IndexRange.End <= 0)
                    return null;
                return new StreamMetaData()
                {
                    FileInitStart = (int)init.InitRange.Start,
                    FileInitEnd = (int)init.InitRange.End,
                    FileIndexStart = (int)init.IndexRange.Start,
                    FileIndexEnd = (int)init.IndexRange.End
                };
            }
        }

        public static async Task<Result> DownloadTrackAsync(SabrStreamSpec spec, int role, UMPFormat format, long durationSec, string targetFile, int concurrency,
            Action<long, long, long> onProgress, Func<bool> isCancelled, CancellationToken cancellationToken = default)
        {
            if (spec.IsLive)
                throw new InvalidOperationException("Live streams cannot be downloaded");

            var fallbackEstimate = Math.Max(0, (long)format.Bitrate / 8 * durationSec);
            var maxBySlice = durationSec > 0 ? (int)(durationSec / PARALLEL_MIN_SLICE_SEC) : 1;
            var n = Math.Min(Math.Clamp(concurrency, 1, MAX_CONCURRENCY), Math.Max(maxBySlice, 1));
            var totalUs = durationSec * 1_000_000L;
            if (totalUs <= 0) n = 1;

            if (File.Exists(targetFile)) File.Delete(targetFile);
            var tmpDir = targetFile + ".umpparts";
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
            Directory.CreateDirectory(tmpDir);

            var claimed = new ConcurrentDictionary<int, bool>();
            long initSize = 0;
            long segmentBytes = 0;
            var endSegment = 0;
            byte[]? initBytes = null;
            FormatInitializationMetadata? formatInit = null;
            var stopwatch = Stopwatch.StartNew();
            var progressLock = new object();

            void Report()
            {
                lock (progressLock)
                {
                    var init = Interlocked.Read(ref initSize);
                    var segments = Interlocked.Read(ref segmentBytes);
                    var count = claimed.Count;
                    var end = Volatile.Read(ref endSegment);
                    var written = init + segments;
                    var estimate = (end > 0 && count > 0) ? init + segments * end / count : fallbackEstimate;
                    var speed = stopwatch.ElapsedMilliseconds > 0 ? written * 1000 / stopwatch.ElapsedMilliseconds : 0;
                    onProgress(Math.Max(estimate, written), written, speed);
                }
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            async Task RunSlice(int i)
            {
                var startUs = totalUs > 0 ? i * totalUs / n : 0;
                var endUs = (i == n - 1) ? long.MaxValue : (i + 1) * totalUs / n;
                using var session = spec.CreateSession();
                session.KeepBehindUs = DOWNLOAD_KEEP_BEHIND_US;
                var buffer = session.BufferFor(format);

                session.SetPlaybackPosition(startUs);
                session.SetDemand(role, format, startUs);
                if (startUs > 0)
                    session.Restart(startUs, true);
                session.Start();

                if (i == 0)
                {
                    var deadline = DateTime.UtcNow + MAX_STALL;
                    SabrSegment? init = null;
                    while (init == null)
                    {
                        CheckState(session, isCancelled, linked.Token);
                        init = await buffer.AwaitInitAsync(POLL, linked.Token);
                        if (init == null && DateTime.UtcNow > deadline)
                            throw new SabrException($"UMP init segment for itag {format.Itag} never arrived");
                    }
                    initBytes = init.ToByteArray();
                    Interlocked.Exchange(ref initSize, initBytes.Length);
                    Report();
                }

                using var output = new FileStream(Path.Combine(tmpDir, $"part_{i}"), FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true);
                var nextSeq = -1;
                var lastProgress = DateTime.UtcNow;
                while (true)
                {
                    CheckState(session, isCancelled, linked.Token);

                    var segment = nextSeq < 0
                        ? await buffer.AwaitCoveringAsync(startUs, POLL, linked.Token)
                        : await buffer.AwaitSequenceAsync(nextSeq, POLL, linked.Token);
                    if (segment != null && !segment.IsComplete)
                    {
                        if (!await buffer.AwaitCompleteAsync(segment, POLL, linked.Token))
                            segment = null;
                    }

                    var fi = session.FormatInitializationFor(format);
                    if (fi != null)
                    {
                        formatInit ??= fi;
                        if (fi.EndSegmentNumber > 0) Volatile.Write(ref endSegment, fi.EndSegmentNumber);
                    }

                    if (segment == null)
                    {
                        if (session.IsComplete(format)) break;
                        if (DateTime.UtcNow - lastProgress > MAX_STALL)
                            throw new SabrException($"UMP download stalled for itag {format.Itag} before reaching the end");
                        continue;
                    }
                    if (segment.StartUs >= endUs) break;

                    nextSeq = segment.SequenceNumber + 1;
                    session.SetPlaybackPosition(segment.EndUs);
                    session.SetDemand(role, format, segment.EndUs);
                    lastProgress = DateTime.UtcNow;

                    if (claimed.TryAdd(segment.SequenceNumber, true))
                    {
                        var bytes = segment.ToByteArray();
                        await output.WriteAsync(bytes, linked.Token);
                        Interlocked.Add(ref segmentBytes, bytes.Length);
                        Report();
                    }
                    buffer.EvictBeforeSequence(segment.SequenceNumber);

                    var lastSeg = Volatile.Read(ref endSegment);
                    if (lastSeg > 0 && nextSeq > lastSeg) break;
                }
            }

            try
            {
                var tasks = Enumerable.Range(0, n).Select(i => Task.Run(async () =>
                {
                    try
                    {
                        await RunSlice(i);
                    }
                    catch
                    {
                        linked.Cancel();
                        throw;
                    }
                })).ToList();
                try
                {
                    await Task.WhenAll(tasks);
                }
                catch
                {
                    var first = tasks.Where(x => x.IsFaulted).Select(x => x.Exception!.InnerException!)
                        .FirstOrDefault(x => x is not OperationCanceledException)
                        ?? tasks.Where(x => x.IsFaulted).Select(x => x.Exception!.InnerException!).FirstOrDefault();
                    if (first != null) throw first;
                    throw;
                }

                var expected = Volatile.Read(ref endSegment);
                if (expected > 0)
                {
                    var missing = Enumerable.Range(1, expected).Where(x => !claimed.ContainsKey(x)).ToList();
                    if (missing.Count > 0)
                        throw new SabrException($"UMP download for itag {format.Itag} is missing {missing.Count} segment(s), first {missing[0]}");
                }

                long total = 0;
                using (var output = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
                {
                    if (initBytes != null)
                    {
                        await output.WriteAsync(initBytes, cancellationToken);
                        total += initBytes.Length;
                    }
                    for (var i = 0; i < n; i++)
                    {
                        var part = Path.Combine(tmpDir, $"part_{i}");
                        if (!File.Exists(part)) continue;
                        using var input = File.OpenRead(part);
                        await input.CopyToAsync(output, cancellationToken);
                        total += input.Length;
                    }
                }
                onProgress(total, total, 0);
                Logger.i(TAG, $"Downloaded itag {format.Itag} ({total} bytes, {claimed.Count} segments, {n} slices) in {stopwatch.Elapsed.TotalSeconds:F1}s");
                return new Result() { Length = total, FormatInitialization = formatInit };
            }
            finally
            {
                try { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); } catch { }
            }
        }

        private static void CheckState(SabrSession session, Func<bool> isCancelled, CancellationToken cancellationToken)
        {
            if (isCancelled()) throw new OperationCanceledException("Download got cancelled");
            cancellationToken.ThrowIfCancellationRequested();
            var fatal = session.FatalError;
            if (fatal != null) throw fatal;
        }
    }
}
