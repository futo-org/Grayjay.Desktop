using Grayjay.ClientServer.Settings;
using Grayjay.ClientServer.Store;
using Grayjay.ClientServer.Sync.Models;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Models.Live;

namespace Grayjay.ClientServer.States;


public class StateWebsocket
{
    public static void SubscriptionGroupsChanged()
    {
        Task.Run(async () =>
        {
            await GrayjayServer.Instance.WebSocket.Broadcast(null, "SubscriptionGroupsChanged");
        });
    }
    public static void SubscriptionsChanged()
    {
        Task.Run(async () =>
        {
            await GrayjayServer.Instance.WebSocket.Broadcast(null, "SubscriptionsChanged");
        });
    }
    public static void PlaylistsChanged()
    {
        Task.Run(async () =>
        {
            await GrayjayServer.Instance.WebSocket.Broadcast(null, "PlaylistsChanged");
        });
    }

    public static void PluginChanged(string id)
    {
        Task.Run(async () =>
        {
            await GrayjayServer.Instance.WebSocket.Broadcast(id, "PluginUpdated", id);
        });
    }

    public static void WatchLaterChanged()
    {
        Task.Run(async () =>
        {
            await GrayjayServer.Instance.WebSocket.Broadcast(null, "WatchLaterChanged");
        });
    }
    public static void EnabledClientsChanged()
    {
        var instance = GrayjayServer.Instance;
        if (instance == null)
            return;

        Task.Run(async () =>
        {
            await instance.WebSocket.Broadcast(null, "EnabledClientsChanged");
        });
    }

    public static void LiveEvents(List<PlatformLiveEvent> liveEvents)
    {
        Task.Run(async () =>
        {
            await GrayjayServer.Instance.WebSocket.Broadcast(liveEvents, "LiveEvents");
        });
    }

    public static void SyncDevicesChanged()
    {
        Task.Run(async () =>
        {
            await GrayjayServer.Instance.WebSocket.Broadcast(null, "SyncDevicesChanged");
        });
    }
    public static void SettingsChanged(GrayjaySettings settings)
    {
        Task.Run(async () =>
        {
            await GrayjayServer.Instance.WebSocket.Broadcast(settings, "SettingsChanged");
        });
    }
    public static void OpenUrl(string url, int positionSeconds)
    {
        Task.Run(async () =>
        {
            await GrayjayServer.Instance.WebSocket.Broadcast(new OpenUrlModel()
            {
                Url = url,
                PositionSeconds = positionSeconds
            }, "OpenUrl");
        });
    }

    public static void LicenseStatusChanged(bool val)
    {
        Task.Run(async () =>
        {
            await GrayjayServer.Instance.WebSocket.Broadcast(val, "LicenseStatusChanged");
        });
    }

    public class OpenUrlModel
    {
        public string Url { get; set; }
        public int PositionSeconds { get; set; }
    }
}