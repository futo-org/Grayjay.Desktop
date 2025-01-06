using Google.Protobuf.WellKnownTypes;
using Grayjay.ClientServer.Store;
using Grayjay.ClientServer.Sync;
using Grayjay.ClientServer.Sync.Models;
using Grayjay.Engine.Models.Feed;
using Newtonsoft.Json.Linq;
using System;
using System.Runtime.CompilerServices;

namespace Grayjay.ClientServer.States;

public class OrderedPlatformVideo : PlatformVideo
{
    public int Index { get; set; }
}

public class StateWatchLater
{
    private readonly ManagedStore<PlatformVideo> _watchLater = new ManagedStore<PlatformVideo>("watchLater")
        .WithUnique(x => x.Url)
        .Load();
    private readonly StringArrayStore _watchLaterOrderStore = new StringArrayStore("watchLaterOrder", Array.Empty<string>())
        .Load();

    private readonly StringStore _watchLaterReorderTime = new StringStore("watchLaterReorderTime")
        .Load();
    private readonly DictionaryStore<string, long> _watchLaterAdds = new DictionaryStore<string, long>("watchLaterAdds", new Dictionary<string, long>())
        .Load();
    private readonly DictionaryStore<string, long> _watchLaterRemovals = new DictionaryStore<string, long>("watchLaterRemovals", new Dictionary<string, long>())
        .Load();

    public event Action<List<PlatformVideo>>? OnChanged;

    public StateWatchLater()
    {
        OnChanged += (videos) =>
        {
            StateWebsocket.WatchLaterChanged();
        };
    }

    public List<PlatformVideo> GetWatchLater() => _watchLater.GetObjects().OrderBy(v => { var index = _watchLaterOrderStore.IndexOf(v.Url); return (index >= 0) ? index : 9999; }).ToList();

    public DateTimeOffset GetWatchLaterAddTime(string url) => DateTimeOffset.FromUnixTimeSeconds(_watchLaterAdds.GetValue(url, 0));
    public Dictionary<string, long> GetWatchLaterAddTimes() => _watchLaterAdds.All();
    public void SetWatchLaterAddTime(string url, DateTimeOffset time)
    {
        _watchLaterAdds.SetAndSave(url, time.ToUnixTimeSeconds());
    }
    public DateTimeOffset GetWatchLaterRemovalTime(string url) => DateTimeOffset.FromUnixTimeSeconds(_watchLaterRemovals.GetValue(url, 0));
    public Dictionary<string, long> GetWatchLaterRemovalTimes() => _watchLaterRemovals.All();
    public DateTimeOffset GetWatchLaterLastReorderTime()
    {
        var value = _watchLaterReorderTime.Value;
        if (string.IsNullOrEmpty(value))
            return DateTimeOffset.MinValue;
        long tryParse;
        if(!long.TryParse(value, out tryParse))
            return DateTimeOffset.MinValue;
        return DateTimeOffset.FromUnixTimeSeconds(tryParse);
    }
    private void SetWatchLaterReorderTime(DateTimeOffset? timeChange = null)
    {
        timeChange = timeChange ?? DateTimeOffset.UtcNow;
        long now = timeChange.Value.ToUnixTimeSeconds();
        _watchLaterReorderTime.Save(now.ToString());
    }

    public List<string> GetWatchLaterOrdering() => _watchLaterOrderStore.GetCopy().ToList();

    public void UpdateWatchLater(List<PlatformVideo> videos, bool isUserInteraction = false)
    {
        //TODO: Know if it changed at all.
        _watchLater.ReplaceAll(videos);
        if (isUserInteraction)
        {
            SetWatchLaterReorderTime();
            BroadcastChanges();
        }
        OnChanged?.Invoke(GetWatchLater());
    }
    public void UpdateWatchLaterOrder(List<string> order, DateTimeOffset? timeChange = null, bool isUserInteraction = false)
    {
        _watchLaterOrderStore.Save(Utilities.SmartMerge(order, StateWatchLater.Instance.GetWatchLaterOrdering()).ToArray());
        SetWatchLaterReorderTime(timeChange);
        OnChanged?.Invoke(GetWatchLater());

        if(isUserInteraction)
        {
            BroadcastChanges(true);
        }
    }


    public void Add(PlatformVideo video, bool isUserInteraction = false)
    {
        var existing = GetWatchLater();
        if (!_watchLaterOrderStore.Contains(video.Url))
            _watchLaterOrderStore.Save(_watchLaterOrderStore.GetCopy().Where(x=>existing.Any(y=>y.Url == x))
                .Concat(new string[] { video.Url }).ToArray());
        _watchLater.Save(video);
        if (isUserInteraction)
        {
            var now = DateTimeOffset.Now.ToUnixTimeSeconds();
            _watchLaterAdds.SetAndSave(video.Url, now);
            BroadcastAddition(video, now);
        }
        OnChanged?.Invoke(GetWatchLater());
    }

    public void Remove(string url, bool isUserInteraction = false, DateTimeOffset? time = null)
    {
        bool didDelete = _watchLater.DeleteBy(v => v.Url, url) != null;
        if (time != null)
            _watchLaterRemovals.SetAndSave(url, time.Value.ToUnixTimeSeconds());
        if (isUserInteraction)
        {
            var now = DateTimeOffset.Now.ToUnixTimeSeconds();
            if (time == null)
            {
                _watchLaterRemovals.SetAndSave(url, now);
                BroadcastRemoval(url, now);
            }
            else
                BroadcastRemoval(url, time.Value.ToUnixTimeSeconds());
        }
        if (_watchLaterOrderStore.Contains(url))
            _watchLaterOrderStore.Save(_watchLaterOrderStore.GetCopy().Where(x => x != url).ToArray());
        OnChanged?.Invoke(GetWatchLater());
    }

    private void BroadcastChanges(bool orderOnly = false)
    {
        Task.Run(async () =>
        {
            var videos = GetWatchLater();
            await StateSync.Instance.BroadcastJsonAsync(GJSyncOpcodes.SyncWatchLater, new SyncWatchLaterPackage()
            {
                Videos = (orderOnly) ? new List<PlatformVideo>() : videos.Select(x=>(PlatformVideo)x).ToList(),
                VideoAdds = (orderOnly) ? new Dictionary<string, long>() : _watchLaterAdds.All(),
                VideoRemovals = (orderOnly) ? new Dictionary<string, long>() : _watchLaterRemovals.All(),
                ReorderTime = GetWatchLaterLastReorderTime().ToUnixTimeSeconds(),
                Ordering = GetWatchLaterOrdering()
            });
        });
    }
    private void BroadcastRemoval(string url, long time)
    {
        Task.Run(async () =>
        {
            await StateSync.Instance.BroadcastJsonAsync(GJSyncOpcodes.SyncWatchLater, new SyncWatchLaterPackage()
            {
                VideoRemovals = new Dictionary<string, long>()
                {
                    { url, time }
                }
            });
        });
    }
    private void BroadcastAddition(PlatformVideo video, long time)
    {
        Task.Run(async () =>
        {
            await StateSync.Instance.BroadcastJsonAsync(GJSyncOpcodes.SyncWatchLater, new SyncWatchLaterPackage()
            {
                Videos = new List<PlatformVideo>() { video },
                VideoAdds = new Dictionary<string, long>()
                {
                    { video.Url, time }
                },
                Ordering = GetWatchLaterOrdering(),
                ReorderTime = GetWatchLaterLastReorderTime().ToUnixTimeSeconds()
            });
        });
    }

    private static readonly object _instanceLock = new object();
    private static StateWatchLater? _instance = null;
    public static StateWatchLater Instance
    {
        get
        {
            lock (_instanceLock)
            {
                if (_instance == null)
                    _instance = new StateWatchLater();
                return _instance;
            }
        }
    }
}