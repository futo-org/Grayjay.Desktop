using System.Collections.Concurrent;
using System.Text.Json;
using JustCef;

namespace Grayjay.Desktop.CEF.LinuxCdm;

internal sealed class LinuxCdmBridge : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly RuntimeAssets assets;
    private readonly string cache;
    private readonly ConcurrentDictionary<int, PlaybackGuestSession> windows = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> setups = new();
    private readonly CancellationTokenSource stop = new();
    internal LinuxCdmBridge(string cache) { this.cache = cache; assets = new(cache); }
    internal static LinuxCdmBridge? Create(string cache) => RuntimeAssets.Supported ? new(cache) : null;
    internal void AttachWindow(JustCefWindow window, bool warm = false) => _ = ForWindow(window, warm);
    private PlaybackGuestSession ForWindow(JustCefWindow window, bool warm = false) => windows.GetOrAdd(window.Identifier, identifier =>
    {
        var session = new PlaybackGuestSession(
            cancellation => assets.EnsureAsync(_ => Task.CompletedTask, cancellation),
            (runtime, callback) => runtime.UsesBlink ? new BlinkGuest(runtime,
                Path.Combine(cache, "logs", identifier + "-" + Guid.NewGuid().ToString("N")), callback) : new QemuGuest(runtime,
                Path.Combine(cache, "logs", identifier + "-" + Guid.NewGuid().ToString("N")), callback),
            async (token, message) => await window.CallBridgeRpcAsync("linuxCdm.event",
                JsonSerializer.Serialize(new { token, @event = message }, Json)).WaitAsync(TimeSpan.FromSeconds(10), stop.Token),
            stop.Token);
        window.OnClose += () => { _ = CloseWindowAsync(window.Identifier); };
        window.OnFrameLoadStart += info => { if (info.IsMainFrame) _ = CloseWindowAsync(window.Identifier, remove: false); };
        if (warm)
        {
            window.OnFrameLoadEnd += info =>
            {
                if (info.IsMainFrame && Uri.TryCreate(info.Url, UriKind.Absolute, out var page) && page.AbsolutePath.StartsWith("/web/", StringComparison.Ordinal))
                {
                    _ = WarmupWindowAsync(window);
                }
            };
        }
        // When enabled, only the main window starts an already downloaded Widevine CDM with the app (licence warm-up after its page loads);
        // otherwise the helper starts through protected playback preparation or start.
        return session;
    });
    internal Task WarmupWindowAsync(JustCefWindow window) =>
        assets.Ready ? WarmupAsync(ForWindow(window), licence: true) : Task.CompletedTask;
    private async Task WarmupAsync(PlaybackGuestSession session, bool licence = false)
    {
        if (!assets.Ready || stop.IsCancellationRequested) return;
        try { await session.WarmupAsync(licence); }
        catch (OperationCanceledException) { }
        catch (Exception e) { Console.Error.WriteLine("Protected playback warmup failed: " + e.Message); }
    }
    internal async Task<string?> HandleAsync(JustCefWindow window, string method, string? json)
    {
        if (!RuntimeAssets.Supported) throw new PlatformNotSupportedException();
        using var document = JsonDocument.Parse(json ?? "null"); var payload = document.RootElement;
        if (method == "linuxCdm.capabilities") return JsonSerializer.Serialize(new { enabled = true, ready = assets.Ready, protocol = RuntimeAssets.ProtocolVersion }, Json);
        string token = payload.GetProperty("token").GetString() ?? throw new ArgumentException("Missing playback token.");
        if (token.Length is < 1 or > 128) throw new ArgumentException("Invalid playback token.");
        if (method == "linuxCdm.prepare")
        {
            _ = ForWindow(window);
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            if (!setups.TryAdd(window.Identifier + ":" + token, cancel)) throw new InvalidOperationException("Playback preparation is already running.");
            try
            {
                await assets.EnsureAsync(async progress =>
                {
                    await window.CallBridgeRpcAsync("linuxCdm.progress", JsonSerializer.Serialize(new { token, progress }, Json))
                        .WaitAsync(TimeSpan.FromSeconds(10), cancel.Token);
                }, cancel.Token);
                _ = WarmupAsync(ForWindow(window));
                return "true";
            }
            finally { setups.TryRemove(window.Identifier + ":" + token, out _); }
        }
        if (method == "linuxCdm.cancelSetup")
        { if (setups.TryGetValue(window.Identifier + ":" + token, out var setup)) setup.Cancel(); return "true"; }
        var session = ForWindow(window);
        if (method == "linuxCdm.start")
        {
            string certificate = payload.TryGetProperty("data", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()! : "";
            await session.StartAsync(token, certificate);
            return "true";
        }
        if (method == "linuxCdm.close")
        {
            await session.CloseAsync(token);
            return "true";
        }
        string data = payload.GetProperty("data").GetString() ?? throw new ArgumentException("Missing CDM request data.");
        string command = method switch
        {
            "linuxCdm.session" => "SESSION", "linuxCdm.fragment" => "FRAGMENT",
            "linuxCdm.update" => "UPDATE", _ => throw new ArgumentException("Unknown protected playback method.")
        };
        string? licenseSession = null;
        if (command == "UPDATE")
        {
            string id = payload.GetProperty("session").GetString()!;
            _ = Convert.FromBase64String(id); licenseSession = id; data = id + " " + data;
        }
        return (await session.RequestAsync(token, command, data, licenseSession)).GetRawText();
    }
    private async Task CloseWindowAsync(int id, bool remove = true)
    {
        foreach (var pair in setups) if (pair.Key.StartsWith(id + ":", StringComparison.Ordinal)) pair.Value.Cancel();
        if (!windows.TryGetValue(id, out var session)) return;
        if (remove)
        {
            await session.DisposeAsync();
            windows.TryRemove(id, out _);
        }
        else await session.StopAsync();
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        await Task.WhenAll(windows.Keys.Select(id => CloseWindowAsync(id)));
        await assets.DisposeAsync();
    }
}
