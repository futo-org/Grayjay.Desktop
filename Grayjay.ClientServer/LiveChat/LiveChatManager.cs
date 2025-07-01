using Grayjay.Engine.Models.Live;
using Grayjay.Engine.Pagers;

using Logger = Grayjay.Desktop.POC.Logger;
using LogLevel = Grayjay.Desktop.POC.LogLevel;

namespace Grayjay.ClientServer.LiveChat;

public class LiveChatManager
{
    private readonly IPager<PlatformLiveEvent> _pager;
    private readonly List<PlatformLiveEvent> _history = new List<PlatformLiveEvent>();
    private readonly Dictionary<object, Action<List<PlatformLiveEvent>>> _followers = new Dictionary<object, Action<List<PlatformLiveEvent>>>();

    private CancellationTokenSource _cts;
    private int _startCounter = 0;

    public long ViewCount { get; private set; }

    public LiveChatManager(IPager<PlatformLiveEvent> pager, long initialViewCount = 0)
    {
        _pager = pager ?? throw new ArgumentNullException(nameof(pager));
        ViewCount = initialViewCount;

        // Initial notice + seed history
        HandleEvents(new List<PlatformLiveEvent>
        {
            new LiveEventComment
            {
                Name = "SYSTEM",
                Message = "Live chat is still under construction. While it is mostly functional, the experience still needs to be improved.\n"
            }
        });

        var initial = _pager.GetResults();
        if (initial?.Length > 0)
            HandleEvents(initial.ToList());
    }

    public void Start()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        var counter = Interlocked.Increment(ref _startCounter);
        _ = RunLoopAsync(counter, token);
    }

    public void Stop()
    {
        // Bump counter to stop loop and cancel token
        Interlocked.Increment(ref _startCounter);
        _cts?.Cancel();
    }

    public List<PlatformLiveEvent> GetHistory()
    {
        lock (_history)
        {
            return new List<PlatformLiveEvent>(_history);
        }
    }

    public void Follow(object tag, Action<List<PlatformLiveEvent>> handler)
    {
        if (tag == null) throw new ArgumentNullException(nameof(tag));
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        List<PlatformLiveEvent> snapshot;
        lock (_history)
        {
            snapshot = new List<PlatformLiveEvent>(_history);
        }

        lock (_followers)
        {
            _followers[tag] = handler;
        }

        // Fire initial batch
        handler(snapshot);
    }

    public void Unfollow(object tag)
    {
        if (tag == null) throw new ArgumentNullException(nameof(tag));
        lock (_followers)
        {
            _followers.Remove(tag);
        }
    }

    private async Task RunLoopAsync(int counter, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && counter == _startCounter)
            {
                if (_pager == null || !_pager.HasMorePages())
                    break;

                try
                {
                    _pager.NextPage();
                    var newEvents = _pager.GetResults() ?? Array.Empty<PlatformLiveEvent>();

                    long nextInterval = 1_000;
                    if (_pager is IPager<PlatformLiveEvent> jsPager)
                        nextInterval = 800;//Math.Max(jsPager.NextRequest, 800); TODO fix

                    if (newEvents.Length > 0)
                    {
                        if (Logger.WillLog(LogLevel.Verbose))
                            Logger.Verbose<LiveChatManager>($"New Live Events ({newEvents.Length}) [{string.Join(", ", newEvents.Select(e => e.Type.ToString()))}]");


                        HandleEvents(newEvents.ToList());
                    }
                    else
                        Logger.Info<LiveChatManager>("No new Live Events");

                    await Task.Delay(TimeSpan.FromMilliseconds(nextInterval), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Error<LiveChatManager>("Failed to load live events", ex);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error<LiveChatManager>("Live events loop crashed.", ex);
        }
    }

    private void HandleEvents(List<PlatformLiveEvent>? events)
    {
        if (events == null) return;

        lock (_history)
        {
            _history.AddRange(events);
        }

        List<Action<List<PlatformLiveEvent>>> handlers;
        lock (_followers)
        {
            handlers = _followers.Values.ToList();
        }

        foreach (var handler in handlers)
        {
            try
            {
                handler(events);
            }
            catch (Exception ex)
            {
                Logger.Error<LiveChatManager>("Failed to invoke follower handler", ex);
            }
        }
    }
}