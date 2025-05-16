using Grayjay.ClientServer.Controllers;
using Grayjay.ClientServer.Models;
using Grayjay.ClientServer.Models.History;
using Grayjay.ClientServer.Models.Subscriptions;
using Grayjay.ClientServer.States;
using Grayjay.ClientServer.Subscriptions;
using Grayjay.ClientServer.Sync.Internal;
using Grayjay.ClientServer.Sync.Models;
using Grayjay.Desktop.POC;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine.Models.Feed;
using SyncClient;
using static Grayjay.ClientServer.States.StateBackup;

namespace Grayjay.ClientServer.Sync
{
    public static class GJSyncOpcodes
    {
        public const byte SendToDevice = 101;


        public const byte SyncStateExchange = 150;

        public const byte SyncExport = 201;
        public const byte SyncSubscriptions = 202;
        public const byte SyncHistory = 203;
        public const byte SyncSubscriptionGroups = 204;
        public const byte SyncPlaylists = 205;
        public const byte SyncWatchLater = 206;
    }

    public partial class GrayjaySyncHandlers : SyncHandlers
    {

        [SyncHandler(GJSyncOpcodes.SendToDevice)]
        public void HandleSendToDevice(SendToDevicePackage package)
        {
            StateWebsocket.OpenUrl(package.Url, package.Position);
        }
        
        [SyncAsyncHandler(GJSyncOpcodes.SyncStateExchange)]
        public async Task HandleSyncExchange(SyncSession session, SyncSessionData data)
        {
            await session.SendJsonDataAsync(GJSyncOpcodes.SyncSubscriptions, new SyncSubscriptionsPackage()
            {
                Subscriptions = StateSubscriptions.GetSubscriptions(),
                SubscriptionRemovals = StateSubscriptions.GetSubscriptionRemovals()
            });
            await session.SendJsonDataAsync(GJSyncOpcodes.SyncSubscriptionGroups, new SyncSubscriptionGroupsPackage()
            {
                Groups = StateSubscriptions.GetGroups(),
                GroupRemovals = StateSubscriptions.GetSubscriptionGroupRemovals()
            });
            await session.SendJsonDataAsync(GJSyncOpcodes.SyncPlaylists, new SyncPlaylistsPackage()
            {
                Playlists = StatePlaylists.All,
                PlaylistRemovals = StatePlaylists.GetPlaylistRemovals()
            });
            await session.SendJsonDataAsync(GJSyncOpcodes.SyncPlaylists, new SyncPlaylistsPackage()
            {
                Playlists = StatePlaylists.All,
                PlaylistRemovals = StatePlaylists.GetPlaylistRemovals()
            });
            await session.SendJsonDataAsync(GJSyncOpcodes.SyncWatchLater, new SyncWatchLaterPackage()
            {
                Videos = StateWatchLater.Instance.GetWatchLater(),
                VideoAdds = StateWatchLater.Instance.GetWatchLaterAddTimes(),
                VideoRemovals = StateWatchLater.Instance.GetWatchLaterRemovalTimes(),
                ReorderTime = StateWatchLater.Instance.GetWatchLaterLastReorderTime().ToUnixTimeSeconds(),
                Ordering = StateWatchLater.Instance.GetWatchLaterOrdering()
            });

            await StateWatchLater.Instance.BroadcastChangesAsync();


            var newHistory = StateHistory.GetRecentHistory(data.LastHistory);
            if (newHistory.Count > 0)
                await session.SendJsonDataAsync(GJSyncOpcodes.SyncHistory, newHistory);
        }

        [SyncHandler(GJSyncOpcodes.SyncExport)]
        public async Task HandleSyncExport(SyncSession session, byte[] data)
        {
            var export = ExportStructure.FromZipBytes(data);

            //Subscriptions
            var subsRecons = export.Stores.FirstOrDefault(x => x.Key.Equals("subscriptions", StringComparison.OrdinalIgnoreCase));
            if(!string.IsNullOrEmpty(subsRecons.Key))
            {
                var subsStore = StateSubscriptions.GetUnderlyingSubscriptionsStore();
                SyncSubscriptionsPackage package = new SyncSubscriptionsPackage();
                foreach(var subRecon in subsRecons.Value)
                {
                    var sub = await subsStore.FromReconstruction(subRecon, export.Cache);
                    package.Subscriptions.Add(sub);
                }
                HandleSyncSubscriptions(session, package);
            }
        }

        [SyncHandler(GJSyncOpcodes.SyncSubscriptions)]
        public async Task HandleSyncSubscriptions(SyncSession session, SyncSubscriptionsPackage subscriptions)
        {
            Logger.Info(nameof(GrayjaySyncHandlers), $"SyncSubscriptions received {subscriptions.Subscriptions.Count} subs");

            List<Subscription> added = new List<Subscription>();
            foreach(var sub in subscriptions.Subscriptions)
            {
                if (!StateSubscriptions.IsSubscribed(sub.Channel))
                {
                    var removalTime = StateSubscriptions.GetSubscriptionRemovalTime(sub.Channel.Url);
                    if (removalTime.Year < 2000 || sub.CreationTime.ToUniversalTime() > removalTime)
                    {
                        await StateSubscriptions.AddSubscription(sub.Channel, sub.CreationTime);
                        added.Add(sub);
                    }
                }
            }
            if (added.Count > 3)
                StateUI.Toast($"{added.Count} Subscriptions from {session.RemotePublicKey.Substring(0, Math.Min(8, session.RemotePublicKey.Length))}");
            else if(added.Count > 0)
                StateUI.Toast($"Subscriptions from {session.RemotePublicKey.Substring(0, Math.Min(8, session.RemotePublicKey.Length))}:\n" +
                    string.Join("\n", added.Select(x => x.Channel.Name)));

            if(subscriptions.SubscriptionRemovals != null && subscriptions.SubscriptionRemovals.Count > 0)
            {
                var removed = StateSubscriptions.ApplySubscriptionRemovals(subscriptions.SubscriptionRemovals);
                if(removed.Count > 3)
                    StateUI.Toast($"Removed {removed.Count} Subscriptions from {session.RemotePublicKey.Substring(0, Math.Min(8, session.RemotePublicKey.Length))}");
                else if(removed.Count > 0)
                    StateUI.Toast($"Subscriptions removed from {session.RemotePublicKey.Substring(0, Math.Min(8, session.RemotePublicKey.Length))}:\n" +
                        string.Join("\n", removed.Select(x => x.Channel.Name)));
            }
        }

        [SyncHandler(GJSyncOpcodes.SyncSubscriptionGroups)]
        public void HandleSyncSubscriptionGroups(SyncSession session, SyncSubscriptionGroupsPackage pack)
        {
            Logger.Info(nameof(GrayjaySyncHandlers), $"SyncSubscriptionGroups received {pack.Groups.Count} groups");

            foreach (SubscriptionGroup group in pack.Groups)
            {
                var existing = StateSubscriptions.GetGroup(group.ID);

                if (existing == null)
                    StateSubscriptions.SaveGroup(group, false);
                else if (existing.LastChange < group.LastChange)
                {
                    existing.Urls = group.Urls;
                    existing.Name = group.Name;
                    existing.LastChange = group.LastChange;
                    existing.CreationTime = group.CreationTime;
                    existing.Priority = group.Priority;
                    existing.Image = group.Image;
                    StateSubscriptions.SaveGroup(existing, false);
                }
            }
            foreach(var removal in pack.GroupRemovals)
            {
                var creation = StateSubscriptions.GetGroup(removal.Key);
                var removalTime = DateTimeOffset.FromUnixTimeSeconds(removal.Value);
                if (creation != null && creation.CreationTime < removalTime)
                    StateSubscriptions.DeleteGroup(removal.Key);
            }
        }

        [SyncHandler(GJSyncOpcodes.SyncPlaylists)]
        public void HandleSyncPlaylists(SyncSession session, SyncPlaylistsPackage pack)
        {
            Logger.Info(nameof(GrayjaySyncHandlers), $"SyncPlaylists received {pack.Playlists.Count} playlists");

            foreach (Playlist playlist in pack.Playlists)
            {
                var existing = StatePlaylists.Get(playlist.Id);

                if (existing == null)
                    StatePlaylists.CreateOrUpdate(playlist, false);
                else if (existing.DateUpdate < playlist.DateUpdate.ToLocalTime())
                    StatePlaylists.CreateOrUpdate(playlist, false);
            }
            foreach (var removal in pack.PlaylistRemovals)
            {
                var creation = StatePlaylists.Get(removal.Key);
                var removalTime = DateTimeOffset.FromUnixTimeSeconds(removal.Value);
                if (creation != null && creation.DateCreation < removalTime)
                    StatePlaylists.Remove(removal.Key, false);
            }
        }

        [SyncHandler(GJSyncOpcodes.SyncWatchLater)]
        public void HandleSyncWatchLater(SyncSession session, SyncWatchLaterPackage pack)
        {
            Logger.Info(nameof(GrayjaySyncHandlers), $"SyncWatchLater received {pack.Videos.Count} watchlater videos");

            var allExisting = StateWatchLater.Instance.GetWatchLater();
            List<string> originalOrder = allExisting.Select(x => x.Url).ToList();
            foreach (PlatformVideo video in pack.Videos)
            {
                var existing = allExisting.FirstOrDefault(x => x.Url == video.Url);

                var time = (pack.VideoAdds != null && pack.VideoAdds.ContainsKey(video.Url)) ? DateTimeOffset.FromUnixTimeSeconds(pack.VideoAdds[video.Url]) : DateTimeOffset.MinValue;
                var removalTime = StateWatchLater.Instance.GetWatchLaterRemovalTime(video.Url);
                if (existing == null && time > removalTime)
                {
                    StateWatchLater.Instance.Add(video);
                    if(time > DateTimeOffset.MinValue.AddDays(1))
                        StateWatchLater.Instance.SetWatchLaterAddTime(video.Url, time);
                }
                //else if (existing.DateUpdate < playlist.DateUpdate.ToLocalTime())
                //    StatePlaylists.CreateOrUpdate(playlist, false);
            }
            foreach (var removal in pack.VideoRemovals)
            {
                var watchLater = allExisting.FirstOrDefault(x => x.Url == removal.Key);
                if (watchLater == null)
                    continue;
                var creation = StateWatchLater.Instance.GetWatchLaterAddTime(watchLater.Url);
                var removalTime = DateTimeOffset.FromUnixTimeSeconds(removal.Value);
                if (watchLater != null && creation < removalTime)
                    StateWatchLater.Instance.Remove(watchLater.Url);
            }
            DateTimeOffset packReorderTime = pack.ReorderTime < 0 ? DateTimeOffset.MinValue : DateTimeOffset.FromUnixTimeSeconds(pack.ReorderTime);
            if (StateWatchLater.Instance.GetWatchLaterLastReorderTime() < packReorderTime && pack.Ordering != null)
                StateWatchLater.Instance.UpdateWatchLaterOrder(pack.Ordering, packReorderTime);
        }

        [SyncHandler(GJSyncOpcodes.SyncHistory)]
        public void HandleSyncHistory(SyncSession session, List<HistoryVideo> history)
        {
            Logger.Info(nameof(GrayjaySyncHandlers), $"SyncHistory received {history.Count} videos");

            var lastHistory = DateTime.MinValue;
            foreach(var video in history)
            {
                var hist = StateHistory.GetHistoryByVideo(video.Video, true, video.Date);
                if(hist != null)
                    StateHistory.UpdateHistoryPosition(video.Video, hist, true, video.Position, video.Date);
                if (video.Date > lastHistory)
                    lastHistory = video.Date;
            }

            if(lastHistory != DateTime.MinValue && history.Count > 1)
            {
                var ses = StateSync.Instance.GetSyncSessionData(session.RemotePublicKey);
                if (ses.LastHistory < lastHistory)
                {
                    ses.LastHistory = lastHistory;
                    StateSync.Instance.SaveSyncSessionData(ses);
                }
            }
        }
        
    }
}
