using System.Collections.Concurrent;
using System.Text.Json;

namespace Grayjay.Desktop.CEF.LinuxCdm;

internal interface IPlaybackGuest : IAsyncDisposable
{
    bool IsRunning { get; }
    Task StartAsync(string accelerator, CancellationToken cancellation);
    Task<JsonElement> RequestAsync(string command, string data, CancellationToken cancellation, Action<JsonElement>? onResponse = null);
    void SetBackground(bool background) { }
}

internal sealed class PlaybackGuestSession : IAsyncDisposable
{
    // Generic Widevine PSSH for the licence warm-up; the warmth does not depend on the content key.
    private const string WarmupPssh = "AAAANHBzc2gAAAAA7e+LqXnWSs6jyCfc1R0h7QAAABQIARIQAAECAwQFBgcICQoLDA0ODw==";
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private readonly ConcurrentDictionary<string, string> owners = new();
    private readonly CancellationTokenSource stop;
    private readonly Func<CancellationToken, Task<PlayerRuntime>> prepare;
    private readonly Func<PlayerRuntime, Func<JsonElement, Task>, IPlaybackGuest> create;
    private readonly Func<string, JsonElement, Task> callback;
    private readonly string preferredAccelerator;
    private IPlaybackGuest? guest;
    private PlayerRuntime? runtime;
    private string? certificate, token;
    private bool licenceWarm;

    internal PlaybackGuestSession(Func<CancellationToken, Task<PlayerRuntime>> prepare,
        Func<PlayerRuntime, Func<JsonElement, Task>, IPlaybackGuest> create,
        Func<string, JsonElement, Task> callback, CancellationToken cancellation, string? preferredAccelerator = null)
    {
        this.prepare = prepare; this.create = create; this.callback = callback;
        this.preferredAccelerator = preferredAccelerator ?? QemuGuest.PreferredAccelerator;
        stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
    }

    internal async Task StartAsync(string playbackToken, string serviceCertificate)
    {
        _ = Convert.FromBase64String(serviceCertificate);
        await lifecycle.WaitAsync(stop.Token);
        try
        {
            await EndPlaybackAsync();
            var prepared = await prepare(stop.Token);
            if (guest is null || !guest.IsRunning || runtime != prepared)
            {
                await DisposeGuestAsync();
                await StartWithFallbackAsync(prepared);
                runtime = prepared;
            }
            guest!.SetBackground(false);
            if (certificate != serviceCertificate)
            {
                // The guest keeps one CDM instance per service certificate and refuses above its limit.
                try { await guest.RequestAsync("CERT", serviceCertificate, stop.Token); }
                catch (InvalidOperationException e) when (e.Message == "instance limit")
                {
                    await DisposeGuestAsync();
                    await StartWithFallbackAsync(prepared);
                    runtime = prepared;
                    await guest!.RequestAsync("CERT", serviceCertificate, stop.Token);
                }
                certificate = serviceCertificate;
            }
            token = playbackToken;
        }
        catch { await DisposeGuestAsync(); throw; }
        finally { lifecycle.Release(); }
    }

    internal async Task WarmupAsync(bool licence)
    {
        IPlaybackGuest? target = null;
        await lifecycle.WaitAsync(stop.Token);
        try
        {
            if (guest?.IsRunning != true)
            {
                var prepared = await prepare(stop.Token);
                await DisposeGuestAsync();
                await StartWithFallbackAsync(prepared);
                runtime = prepared;
            }
            if (licence && !licenceWarm && token is null)
            {
                target = guest; licenceWarm = true;
                target!.SetBackground(true);
            }
        }
        catch { await DisposeGuestAsync(); throw; }
        finally { lifecycle.Release(); }
        if (target is null) return;
        // Runs outside the lifecycle lock; the guest queues a playback start behind it.
        try { await target.RequestAsync("WARMUP", WarmupPssh, stop.Token); }
        catch (Exception e) when (e is InvalidOperationException or IOException) { }
    }

    private async Task StartWithFallbackAsync(PlayerRuntime prepared)
    {
        if (prepared.UsesBlink)
        {
            await StartGuestAsync(prepared, "tcg");
            return;
        }
        try { await StartGuestAsync(prepared, preferredAccelerator); }
        catch (Exception e) when (preferredAccelerator != "tcg" && !stop.IsCancellationRequested)
        {
            Console.Error.WriteLine("Playback guest startup with " + preferredAccelerator + " failed; retrying with tcg: " + e);
            await DisposeGuestAsync();
            await StartGuestAsync(prepared, "tcg");
        }
    }

    private async Task StartGuestAsync(PlayerRuntime prepared, string accelerator)
    {
        IPlaybackGuest? started = null;
        started = create(prepared, async message =>
        {
            if (!ReferenceEquals(guest, started)) return;
            string? owner;
            if (message.TryGetProperty("session", out var session))
                owners.TryGetValue(session.GetString()!, out owner);
            else owner = token;
            if (owner is not null) await callback(owner, message);
        });
        guest = started;
        await started.StartAsync(accelerator, stop.Token);
    }

    internal async Task<JsonElement> RequestAsync(string playbackToken, string command, string data, string? session = null)
    {
        await lifecycle.WaitAsync(stop.Token);
        try
        {
            if (token != playbackToken || guest is null || !guest.IsRunning)
                throw new InvalidOperationException("Playback session is no longer active.");
            if (session is not null && (!owners.TryGetValue(session, out var owner) || owner != playbackToken))
                throw new InvalidOperationException("Playback license is no longer active.");
            return await guest.RequestAsync(command, data, stop.Token, response =>
            {
                if (command == "SESSION") owners[response.GetProperty("session").GetString()!] = playbackToken;
            });
        }
        finally { lifecycle.Release(); }
    }

    internal async Task CloseAsync(string playbackToken)
    {
        await lifecycle.WaitAsync(stop.Token);
        try
        {
            if (token != playbackToken) return;
            await EndPlaybackAsync();

        }
        finally { lifecycle.Release(); }
    }

    private async Task EndPlaybackAsync()
    {
        token = null;
        var sessions = owners.Keys.ToArray(); owners.Clear();
        if (guest is null) return;
        try
        {
            foreach (string session in sessions) await guest.RequestAsync("CLOSE", session, stop.Token);
        }
        catch { await DisposeGuestAsync(); }
    }

    private async Task DisposeGuestAsync()
    {
        var previous = guest; guest = null; token = null; owners.Clear(); runtime = null; certificate = null; licenceWarm = false;
        if (previous is not null) await previous.DisposeAsync();
    }

    internal async Task StopAsync()
    {
        await lifecycle.WaitAsync();
        try { await DisposeGuestAsync(); }
        finally { lifecycle.Release(); }
    }

    public async ValueTask DisposeAsync() { stop.Cancel(); await StopAsync(); }
}
