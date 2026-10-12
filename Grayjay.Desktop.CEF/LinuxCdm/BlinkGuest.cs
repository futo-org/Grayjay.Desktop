using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using System.Text;

namespace Grayjay.Desktop.CEF.LinuxCdm;

internal sealed class BlinkGuest : IPlaybackGuest
{
    private readonly PlayerRuntime runtime;
    private readonly string logs;
    private readonly ulong memoryLimitBytes;
    private readonly Func<JsonElement, Task> callback;
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim writer = new(1, 1);
    private sealed record PendingRequest(TaskCompletionSource<JsonElement> Completion, Action<JsonElement>? OnResponse);
    private readonly ConcurrentDictionary<int, PendingRequest> pending = new();
    private readonly Channel<JsonElement> events = Channel.CreateUnbounded<JsonElement>();
    private readonly TaskCompletionSource<bool> initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<Task> workers = [];
    private Process? process;
    private WindowsPlaybackProcess? child;
    private StreamReader? diagnostics;
    private StreamReader? input;
    private StreamWriter? output;
    private int nextId, disposed, failed;
    private string lastDiagnostic = "";
    internal BlinkGuest(PlayerRuntime runtime, string logs, Func<JsonElement, Task> callback, ulong memoryLimitBytes = 0)
    { this.runtime = runtime; this.logs = logs; this.callback = callback; this.memoryLimitBytes = memoryLimitBytes; }

    public bool IsRunning => Volatile.Read(ref failed) == 0 && Volatile.Read(ref disposed) == 0 &&
        initialized.Task.IsCompletedSuccessfully && initialized.Task.Result;

    public async Task StartAsync(string accelerator, CancellationToken cancellation)
    {
        if (!RuntimeAssets.Supported) throw new PlatformNotSupportedException();
        Directory.CreateDirectory(logs);
        if (runtime.GuestRoot is null) throw new InvalidOperationException("Playback guest root is missing.");
        if (OperatingSystem.IsWindows())
        {
            child = WindowsPlaybackProcess.Start(runtime.Executable, runtime.Directory,
                new[] { "-C", CygwinPath(runtime.GuestRoot), "/usr/local/bin/cdm-host", "/opt/widevine/libwidevinecdm.so" }, memoryLimitBytes);
            process = child.Process;
            input = child.Output; output = child.Input; diagnostics = child.Error;
        }
        else if (OperatingSystem.IsMacOS())
        {
            var start = new ProcessStartInfo(runtime.Executable)
            {
                WorkingDirectory = runtime.Directory, UseShellExecute = false,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            start.ArgumentList.Add("-m");
            foreach (var argument in new[] { "-C", runtime.GuestRoot, "/usr/local/bin/cdm-host", "/opt/widevine/libwidevinecdm.so" })
                start.ArgumentList.Add(argument);
            process = Process.Start(start) ?? throw new IOException("Could not start the playback helper.");
            input = process.StandardOutput; output = process.StandardInput; diagnostics = process.StandardError;
        }
        else throw new PlatformNotSupportedException();
        workers.Add(DiagnosticsAsync());
        workers.Add(ReadAsync()); workers.Add(DispatchAsync());
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, stop.Token);
        if (!await initialized.Task.WaitAsync(TimeSpan.FromSeconds(30), linked.Token))
            throw new InvalidOperationException("Widevine initialization failed.");
        workers.Add(HeartbeatAsync());
    }
    public void SetBackground(bool background)
    {
        // Windows only: an unprivileged Linux or macOS process cannot raise its priority again.
        if (!OperatingSystem.IsWindows() || process is null) return;
        try { process.PriorityClass = background ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal; }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
    private async Task ReadAsync()
    {
        Exception failure;
        try
        {
            while (await input!.ReadLineAsync(stop.Token) is { } line)
            {
                if (line.Length > 64 * 1024 * 1024) throw new InvalidDataException("Playback response is too large.");
                using var document = JsonDocument.Parse(line);
                var message = document.RootElement.Clone(); string kind = message.GetProperty("event").GetString()!;
                if (kind == "initialized")
                {
                    if (message.GetProperty("protocol").GetInt32() != RuntimeAssets.ProtocolVersion)
                        throw new InvalidDataException("Playback helper protocol mismatch.");
                    initialized.TrySetResult(message.GetProperty("success").GetBoolean());
                }
                else if (kind is "created" or "resolved" or "rejected" or "fragment" or "error")
                {
                    if (pending.TryRemove(message.GetProperty("id").GetInt32(), out var request))
                    {
                        if (kind is "rejected" or "error") request.Completion.TrySetException(new InvalidOperationException(message.GetProperty("message").GetString()));
                        else
                        {
                            try { request.OnResponse?.Invoke(message); request.Completion.TrySetResult(message); }
                            catch (Exception e) { request.Completion.TrySetException(e); }
                        }
                    }
                }
                else await events.Writer.WriteAsync(message, stop.Token);
            }
            failure = new IOException("Protected playback stopped unexpectedly." + (string.IsNullOrEmpty(lastDiagnostic) ? "" : " " + lastDiagnostic));
        }
        catch (Exception e) { failure = e; }
        Interlocked.Exchange(ref failed, 1);
        initialized.TrySetException(failure);
        foreach (var request in pending.Values) request.Completion.TrySetException(failure);
        if (!stop.IsCancellationRequested)
        {
            await events.Writer.WriteAsync(JsonSerializer.SerializeToElement(new { @event = "fatal", message = failure.Message }));
        }
        events.Writer.TryComplete();
    }
    private async Task DispatchAsync()
    {
        try { await foreach (var message in events.Reader.ReadAllAsync(stop.Token)) await callback(message); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception e) { Console.Error.WriteLine("Protected playback callback failed: " + e.Message); }
    }
    private async Task DiagnosticsAsync()
    {
        try
        {
            await using var log = new StreamWriter(Path.Combine(logs, "runtime.log"), append: true);
            while (await diagnostics!.ReadLineAsync(stop.Token) is { } line)
            { lastDiagnostic = line.Length <= 500 ? line : line[..500]; await log.WriteLineAsync(line); await log.FlushAsync(); Console.WriteLine(line); }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }
    private async Task HeartbeatAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(stop.Token)) await SendAsync("PING", stop.Token);
        }
        catch (Exception e) when (stop.IsCancellationRequested || e is IOException or ObjectDisposedException) { }
    }
    public async Task<JsonElement> RequestAsync(string command, string data, CancellationToken cancellation, Action<JsonElement>? onResponse = null)
    {
        if (!IsRunning) throw new IOException("Protected playback guest is not running.");
        if (data.Length > 48 * 1024 * 1024 || data.Contains('\n') || data.Contains('\r'))
            throw new InvalidDataException("Invalid playback request.");
        int id = Interlocked.Increment(ref nextId);
        var request = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = new(request, onResponse);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, stop.Token);
        linked.CancelAfter(TimeSpan.FromSeconds(180));
        try
        {
            await SendAsync(command + " " + id + " " + data, linked.Token);
            return await request.Task.WaitAsync(linked.Token);
        }
        finally { pending.TryRemove(id, out _); }
    }
    private async Task SendAsync(string line, CancellationToken cancellation)
    {
        await writer.WaitAsync(cancellation);
        try { await output!.WriteLineAsync(line.AsMemory(), cancellation); await output.FlushAsync(cancellation); }
        finally { writer.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try
        {
            if (process is { HasExited: false })
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    if (output is not null) await SendAsync("QUIT", timeout.Token);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
                }
                catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException or InvalidOperationException)
                { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            }
        }
        finally
        {
            stop.Cancel();
            try
            {
                await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (Exception e) when (e is OperationCanceledException or TimeoutException or IOException) { }
            finally { if (child is not null) child.Dispose(); else process?.Dispose(); }

        }
    }
    internal static string CygwinPath(string path)
    {
        string full = Path.GetFullPath(path);
        if (full.Length < 3 || full[1] != ':' || full[2] != '\\') throw new NotSupportedException("CDM runtime requires a local drive path.");
        return "/cygdrive/" + char.ToLowerInvariant(full[0]) + full[2..].Replace('\\', '/');
    }
}
