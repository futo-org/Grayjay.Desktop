using System.Text.Json;
using Grayjay.Desktop.CEF.LinuxCdm;

internal static class PlaybackTests
{
    internal static async Task RunAsync()
    {
        var guests = new List<FakeGuest>();
        var events = new List<(string Token, string Event)>();
        var runtime = new PlayerRuntime("runtime", "qemu", "kernel", "image");
        var warmedGuests = new List<FakeGuest>();
        var warmPreparation = new TaskCompletionSource<PlayerRuntime>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var warmed = new PlaybackGuestSession(_ => warmPreparation.Task,
            (_, callback) => { var guest = new FakeGuest(callback); warmedGuests.Add(guest); return guest; },
            (_, _) => throw new Exception("Warmup must not send playback events"), default, "tcg"))
        {
            var warmup = warmed.WarmupAsync(false);
            var playback = warmed.StartAsync("warm", "Y2VydA==");
            warmPreparation.SetResult(runtime);
            await Task.WhenAll(warmup, playback);
            Check(warmedGuests.Count == 1 && warmedGuests[0].Certificate == "Y2VydA==",
                "playback joins background warmup and applies its certificate without rebooting");
            await warmed.WarmupAsync(true);
            Check(warmedGuests.Count == 1 && warmedGuests[0].Warmups == 0, "repeat warmup preserves active playback without a licence warm-up");
            await warmed.StopAsync();
            await warmed.WarmupAsync(false);
            await warmed.StartAsync("warm-again", "");
            Check(warmedGuests.Count == 2 && warmedGuests[0].Disposed && warmedGuests[1].Certificate == "",
                "reload discards the warmed helper and a fresh helper selects the uncertified instance");
        }
        var licenceGuests = new List<FakeGuest>();
        await using (var licensed = new PlaybackGuestSession(_ => Task.FromResult(runtime),
            (_, callback) => { var guest = new FakeGuest(callback) { HoldWarmup = new(TaskCreationOptions.RunContinuationsAsynchronously) }; licenceGuests.Add(guest); return guest; },
            (_, _) => Task.CompletedTask, default, "tcg"))
        {
            var warming = licensed.WarmupAsync(true);
            await licenceGuests[0].WarmupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(!warming.IsCompleted && licenceGuests[0].Background.SequenceEqual(new[] { true }),
                "licence warm-up runs in the background at low priority");
            await licensed.StartAsync("during", "").WaitAsync(TimeSpan.FromSeconds(5));
            Check(licenceGuests.Count == 1 && licenceGuests[0].Background.SequenceEqual(new[] { true, false }) && licenceGuests[0].Certificate == "",
                "playback during the licence warm-up proceeds on the same guest at normal priority");
            licenceGuests[0].HoldWarmup!.SetResult(true);
            await warming.WaitAsync(TimeSpan.FromSeconds(5));
            await licensed.CloseAsync("during");
            await licensed.WarmupAsync(true);
            Check(licenceGuests[0].Warmups == 1, "the licence warm-up runs once per guest");
            await licensed.StopAsync();
            licenceGuests.Clear();
            var rewarm = licensed.WarmupAsync(true);
            await licenceGuests[0].WarmupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            licenceGuests[0].HoldWarmup!.SetException(new InvalidOperationException("rejected"));
            await rewarm.WaitAsync(TimeSpan.FromSeconds(5));
            Check(licenceGuests.Count == 1 && licenceGuests[0].Warmups == 1 && licenceGuests[0].IsRunning,
                "a reload warms the new guest again and a failed warm-up keeps it");
        }
        var fallbackGuests = new List<FakeGuest>();
        await using (var fallback = new PlaybackGuestSession(_ => Task.FromResult(runtime),
            (_, callback) => { var guest = new FakeGuest(callback) { FailStart = fallbackGuests.Count == 0 }; fallbackGuests.Add(guest); return guest; },
            (_, _) => Task.CompletedTask, default, "hvf"))
        {
            await fallback.StartAsync("fallback", "");
            Check(fallbackGuests.Count == 2 && fallbackGuests[0].Disposed &&
                fallbackGuests[0].Accelerator == "hvf" && fallbackGuests[1].Accelerator == "tcg" && fallbackGuests[1].IsRunning,
                "unavailable WHPX is disposed and retried with software emulation");
        }
        var preparation = new TaskCompletionSource<PlayerRuntime>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = new PlaybackGuestSession(_ => preparation.Task,
            (_, callback) => { var guest = new FakeGuest(callback); guests.Add(guest); return guest; },
            (token, message) => { events.Add((token, message.GetProperty("event").GetString()!)); return Task.CompletedTask; },
            default);
        Check(guests.Count == 0, "creating a window session does not launch a protected playback helper");
        var start = session.StartAsync("one", "");
        Check(!start.IsCompleted, "playback waits for component preparation");
        preparation.SetResult(runtime); await start;
        var created = await session.RequestAsync("one", "SESSION", "pssh");
        string id = created.GetProperty("session").GetString()!;
        Check(events.SequenceEqual(new[] { ("one", "message") }), "license events belong to their session before creation returns");
        await session.CloseAsync("one");
        Check(guests[0].Closed.Contains(id) && !guests[0].Disposed, "closing playback closes licenses and retains the guest");
        await session.StartAsync("two", "");
        await session.CloseAsync("one");
        Check(guests.Count == 1 && !guests[0].Disposed, "new playback reuses the guest and ignores stale close requests");
        await Reject(() => session.RequestAsync("one", "SESSION", "pssh"), "stale playback cannot create licenses");
        await Reject(() => session.RequestAsync("two", "UPDATE", id + " response", id), "new playback cannot update an old license");
        await guests[0].EmitAsync("message", id);
        Check(events.Count == 1, "late events from a closed license are discarded");
        var second = await session.RequestAsync("two", "SESSION", "pssh");
        string secondId = second.GetProperty("session").GetString()!;
        await session.StartAsync("three", "Y2VydA==");
        Check(guests.Count == 1 && !guests[0].Disposed && guests[0].Closed.Contains(secondId) && guests[0].Certificate == "Y2VydA==",
            "certificate changes close licenses and select an instance in the same guest");
        await session.StartAsync("four", "");
        Check(guests.Count == 1 && guests[0].Certificate == "",
            "a source without a certificate selects the uncertified instance");
        int certificates = guests[0].Certificates;
        await session.StartAsync("four-again", "");
        Check(guests[0].Certificates == certificates, "the same certificate is not sent again");
        guests[0].InstanceLimit = true;
        await session.StartAsync("limit", "bGltaXQ=");
        Check(guests.Count == 2 && guests[0].Disposed && guests[1].Certificate == "bGltaXQ=",
            "a guest at its instance limit is replaced");
        runtime = runtime with { Initramfs = "updated-image" };
        preparation = new(TaskCreationOptions.RunContinuationsAsynchronously); preparation.SetResult(runtime);
        await session.StartAsync("five", "");
        Check(guests.Count == 3 && guests[1].Disposed, "updated boot images replace cached guests");
        guests[2].Running = false;
        await session.StartAsync("six", "");
        Check(guests.Count == 4 && guests[2].Disposed, "failed guests are replaced on the next playback");
        guests[3].FailClose = true;
        await session.RequestAsync("six", "SESSION", "pssh");
        await session.CloseAsync("six");
        Check(guests[3].Disposed, "a failed license close discards the guest");
        await session.StartAsync("seven", "");
        await session.CloseAsync("seven");
        Check(!guests[4].Disposed, "idle guests stay loaded until the window closes");
        await session.StartAsync("eight", "");
        await session.StopAsync();
        Check(guests[4].Disposed, "window reload destroys the guest immediately");
        await session.StartAsync("nine", "");
        await session.DisposeAsync();
        Check(guests[5].Disposed, "window disposal destroys the guest immediately");
    }
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
    private static async Task Reject(Func<Task> action, string name)
    {
        try { await action(); }
        catch (InvalidOperationException) { Console.WriteLine("PASS " + name); return; }
        throw new Exception(name);
    }
    private sealed class FakeGuest(Func<JsonElement, Task> callback) : IPlaybackGuest
    {
        internal bool Running, Disposed, FailClose, FailStart, InstanceLimit;
        internal string? Certificate, Accelerator;
        internal int Certificates, Warmups;
        internal TaskCompletionSource<bool>? HoldWarmup;
        internal readonly TaskCompletionSource<bool> WarmupStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<bool> Background = [];
        internal readonly List<string> Closed = [];
        internal readonly TaskCompletionSource<bool> Disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int sequence;
        public bool IsRunning => Running && !Disposed;
        public Task StartAsync(string accelerator, CancellationToken cancellation)
        {
            Accelerator = accelerator;
            if (FailStart) throw new IOException("HVF: No accelerator found");
            Running = true; return Task.CompletedTask;
        }
        public async Task<JsonElement> RequestAsync(string command, string data, CancellationToken cancellation, Action<JsonElement>? onResponse = null)
        {
            if (command == "CLOSE")
            {
                if (FailClose) throw new IOException("Guest stopped");
                Closed.Add(data); await EmitAsync("closed", data);
            }
            if (command == "CERT")
            {
                if (InstanceLimit && data != Certificate) throw new InvalidOperationException("instance limit");
                Certificate = data; Certificates++;
            }
            if (command == "WARMUP")
            {
                Warmups++; WarmupStarted.TrySetResult(true);
                if (HoldWarmup is not null) await HoldWarmup.Task;
            }
            var response = JsonSerializer.SerializeToElement(new { @event = command == "SESSION" ? "created" : "resolved", session = "session-" + ++sequence });
            onResponse?.Invoke(response);
            if (command == "SESSION") await EmitAsync("message", response.GetProperty("session").GetString()!);
            return response;
        }
        public void SetBackground(bool background) => Background.Add(background);
        internal Task EmitAsync(string kind, string session) => callback(JsonSerializer.SerializeToElement(new { @event = kind, session }));
        public ValueTask DisposeAsync() { Disposed = true; Disposal.TrySetResult(true); return ValueTask.CompletedTask; }
    }
}
