using Grayjay.ClientServer.Database.Indexes;
using Grayjay.ClientServer.Models;
using Grayjay.ClientServer.Models.History;
using Grayjay.ClientServer.Store;
using Grayjay.ClientServer.Sync;
using Grayjay.ClientServer.Sync.Models;
using Grayjay.Desktop.POC;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Pagers;
using System;
using System.Collections.Concurrent;

namespace Grayjay.ClientServer.States
{
    public static class StateHistory
    {

        private static readonly ConcurrentDictionary<object, DBHistoryIndex> _historyIndex = new ConcurrentDictionary<object, DBHistoryIndex>();
        private static readonly ManagedDBStore<DBHistoryIndex, HistoryVideo> _history =
            new ManagedDBStore<DBHistoryIndex, HistoryVideo>(DBHistoryIndex.TABLE_NAME)
            .WithIndex(x=>x.Url, _historyIndex, false, true)
            .Load();

        public static event Action<PlatformVideo, long> OnHistoricVideoChanged;


        public static DBHistoryIndex GetHistoryIndex(string url)
        {
            DBHistoryIndex index;
            if (_historyIndex.TryGetValue(url, out index))
                return index;
            return null;
        }
        public static long GetHistoryPosition(string url)
        {
            return GetHistoryIndex(url)?.Position ?? 0;
        }

        public static bool IsHistoryWatched(string url, long duration)
        {
            return IsHistoryWatchedPercentage(GetHistoryPosition(url), (long)(duration * (decimal)0.7));
        }
        public static bool IsHistoryWatchedPercentage(long watched, long duration)
        {
            return watched > duration * 0.7;
        }

        public static void Clear()
        {
            _history.DeleteAll();
        }

        public static void ClearToday()
        {
            var today = _history.QueryGreater(nameof(DBHistoryIndex.DateTime), DateTime.Now.Subtract(DateTime.Now.TimeOfDay));
        }


        public static long UpdateHistoryPosition(PlatformVideo video, DBHistoryIndex index, bool updateExisting, long position = -1, DateTime? date = null)
        {
            position = (position < 0) ? 0 : position;
            var historyVideo = index.Object;

            var positionBefore = index.Position;
            if (updateExisting)
            {
                bool shouldUpdate = false;
                if (positionBefore < 30 * 1000)
                    shouldUpdate = true;
                else if (position > 30 * 1000)
                    shouldUpdate = true;

                if(shouldUpdate)
                {
                    //Restores broken imports
                    if(historyVideo.Video.Author.ID.Value == null && historyVideo.Video.Duration == 0)
                        historyVideo.Video = video;

                    index.Position = position;
                    historyVideo.Position = position;
                    historyVideo.Date = (date != null) ? date.Value : DateTime.Now;
                    try
                    {
                        _history.Update(index.ID, historyVideo);
                    }
                    catch(Exception ex)
                    {
                        Logger.e(nameof(StateHistory), $"Failed to update history for video [{historyVideo.Video.Name}]: {ex.Message}", ex);
                    }
                    OnHistoricVideoChanged?.Invoke(video, position);
                }
                return positionBefore;
            }
            return positionBefore;
        }
        private static DateTime _lastHistoryBroadcast = DateTime.MinValue;
        private static string _lastHistoryBroadcastUrl = string.Empty;
        public static async void UpdateHistory(PlatformVideo video, DBHistoryIndex index, long position, long delta)
        {
            UpdateHistoryPosition(video, index, true, position);

            Desktop.POC.Logger.Info(nameof(StateHistory), $"SyncHistory sent update video '{video.Name}' (url: {video.Url}) at timestamp {position}");
            await StateSync.Instance.BroadcastJsonAsync(GJSyncOpcodes.SyncHistory, new List<HistoryVideo>()
            {
                index.Object
            });
        }


        public static List<HistoryVideo> GetRecentHistory(DateTime minDate, int max = 1000)
        {
            var pager = GetHistoryPager();
            List<HistoryVideo> videos = pager.GetResults().Where(x => x.Date > minDate).ToList();
            while (pager.HasMorePages() && videos.Count < max)
            {
                pager.NextPage();
                var newResults = pager.GetResults();
                bool foundEnd = false;
                foreach(var item in newResults)
                {
                    if (item.Date < minDate)
                    {
                        foundEnd = true;
                        break;
                    }
                    else
                        videos.Add(item);
                }
                if (foundEnd)
                    break;
            }
            return videos;
        }

        public static IPager<HistoryVideo> GetHistoryPager()
        {
            return _history.Pager(10, x=>x.Object);
        }
        public static IPager<HistoryVideo> GetHistorySearchPager(string query)
        {
            return _history.QueryLikePager(nameof(DBHistoryIndex.Name), $"%{query}%", 10, x => x.Object);
        }

        public static DBHistoryIndex GetHistoryByVideo(PlatformVideo video, bool create = false, DateTime watchDate = default(DateTime))
        {
            var existing = GetHistoryIndex(video.Url);
            DBHistoryIndex result = null;
            if(existing != null)
            {
                result = _history.Get(existing.ID);
                if(result == null)
                {
                    //History null, no tracking
                }
            }
            else if(create)
            {
                var newHistoryItem = HistoryVideo.FromVideo(video, 0, (watchDate == default(DateTime)) ? DateTime.Now : watchDate);
                var index = _history.Insert(newHistoryItem);
                result = _history.Get(index.ID);
                if(result == null)
                {
                    //History null, no tracking
                }
            }
            return result;
        }


        public static void RemoveHistory(string url)
        {
            var index = GetHistoryIndex(url);
            if (index != null)
                _history.Delete(index);
        }

        public static void RemoveHistoryRange(long minutesToDelete)
        {
            var now = DateTime.Now;
            var toDeleteTime = TimeSpan.FromMinutes(minutesToDelete);
            var toDelete = _history.GetAllIndexes().Where(x => minutesToDelete == -1 || (now.Subtract(x.DateTime) < toDeleteTime)).ToList();
            foreach (var item in toDelete)
                _history.Delete(item);
        }


        public static PlatformVideo AddVideoMetadata(PlatformVideo video)
        {
            long watched = StateHistory.GetHistoryPosition(video.Url);
            return video
                .AddMetadata("position", watched)
                .AddMetadata("watched", StateHistory.IsHistoryWatchedPercentage(watched, video.Duration));
        }
    }
}
