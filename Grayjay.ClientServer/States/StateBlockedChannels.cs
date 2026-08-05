using Grayjay.ClientServer.Models;
using Grayjay.ClientServer.Store;
using Grayjay.ClientServer.Sync;
using Grayjay.ClientServer.Sync.Models;
using Grayjay.Desktop.POC;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine.Models.Channel;
using Grayjay.Engine.Models.General;
using System.Text.RegularExpressions;
using static Grayjay.ClientServer.States.StateBackup;

namespace Grayjay.ClientServer.States
{
    public class StateBlockedChannels
    {
        private class BlockedChannelReconstructionStore : ReconstructStore<BlockedChannel>
        {
            public override string ToReconstruction(BlockedChannel obj) => obj.Url;

            public override BlockedChannel ToObject(string id, string backup, Builder builder, ImportCache cache = null)
            {
                try
                {
                    var channel = cache?.Channels?.FirstOrDefault(x => x.IsSameUrl(backup)) ?? StatePlatform.GetChannel(backup);
                    if (channel != null)
                    {
                        var blocked = new BlockedChannel()
                        {
                            Url = channel.Url,
                            Name = channel.Name,
                            Thumbnail = channel.Thumbnail,
                            PluginId = StatePlatform.GetChannelClientOrNull(channel.Url)?.Config.ID,
                            ChannelId = channel.ID?.Value,
                            UrlAlternatives = channel.UrlAlternatives?.ToList() ?? new List<string>(),
                            BlockedTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                        };
                        Normalize(blocked);
                        return blocked;
                    }
                }
                catch (Exception ex)
                {
                    Logger.w(nameof(StateBlockedChannels), $"Failed to resolve blocked channel [{backup}], keeping url only", ex);
                }
                var fallback = new BlockedChannel()
                {
                    Url = backup,
                    Name = backup,
                    BlockedTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
                Normalize(fallback);
                return fallback;
            }
        }

        private readonly ManagedStore<BlockedChannel> _blocked = new ManagedStore<BlockedChannel>("blockedChannels")
            .WithUnique(x => x.Url)
            .WithRestore<BlockedChannelReconstructionStore>()
            .Load();
        private readonly DictionaryStore<string, long> _blockedRemoved = new DictionaryStore<string, long>("blockedChannelsRemoved", new Dictionary<string, long>())
            .Load();

        private static readonly Regex _channelIdRegex = new Regex(@"(?:/channel/)([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase);
        private static bool _didBackfill = false;

        public event Action? OnChanged;

        public StateBlockedChannels()
        {
            OnChanged += () =>
            {
                StateWebsocket.BlockedChannelsChanged();
            };
        }

        private static void Normalize(BlockedChannel channel)
        {
            if (channel == null)
                throw new ArgumentNullException(nameof(channel));
            channel.Url = channel.Url?.ToLower();
            if (string.IsNullOrEmpty(channel.Url))
                throw new ArgumentException("Blocked channel requires a url");
            channel.ChannelId = string.IsNullOrWhiteSpace(channel.ChannelId) ? null : channel.ChannelId.Trim();
            channel.UrlAlternatives = (channel.UrlAlternatives ?? new List<string>())
                .Where(x => !string.IsNullOrEmpty(x))
                .Select(x => x.ToLower())
                .Distinct()
                .ToList();
        }

        public bool IsBlocked(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;
            var norm = url.ToLower();
            return _blocked.HasObject(x => x.IsSameUrl(norm));
        }

        public bool IsBlocked(IEnumerable<string> urls)
        {
            if (urls == null)
                return false;
            var list = urls.Where(x => !string.IsNullOrEmpty(x)).Select(x => x.ToLower()).ToList();
            if (list.Count == 0)
                return false;
            return _blocked.HasObject(x => x.IsSameUrl(list));
        }

        public bool IsBlocked(PlatformAuthorLink author)
        {
            if (author == null)
                return false;
            if (!string.IsNullOrEmpty(author.ID?.Value))
            {
                var id = author.ID.Value;
                if (_blocked.HasObject(x => !string.IsNullOrEmpty(x.ChannelId) && string.Equals(x.ChannelId, id, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            if (IsBlocked(author.Url))
                return true;
            var authorChannelId = GetChannelIdFromUrl(author.Url);
            if (authorChannelId != null)
            {
                return _blocked.HasObject(x =>
                {
                    if (GetChannelIdFromUrl(x.Url) is string blockedId && string.Equals(blockedId, authorChannelId, StringComparison.OrdinalIgnoreCase))
                        return true;
                    return x.UrlAlternatives.Any(alt => GetChannelIdFromUrl(alt) is string altId && string.Equals(altId, authorChannelId, StringComparison.OrdinalIgnoreCase));
                });
            }
            return false;
        }

        private static string? GetChannelIdFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;
            var match = _channelIdRegex.Match(url);
            return match.Success ? match.Groups[1].Value : null;
        }

        public List<BlockedChannel> GetBlocked()
        {
            if (!_didBackfill)
            {
                _didBackfill = true;
                _ = BackfillChannelIds();
            }
            return _blocked.GetObjects().OrderByDescending(x => x.BlockedTime).ToList();
        }

        public List<string> GetBlockedUrls()
        {
            return GetBlocked().Select(x => x.Url).ToList();
        }

        public Task BackfillChannelIds()
        {
            return Task.Run(() =>
            {
                try
                {
                    foreach (var entry in _blocked.GetObjects())
                    {
                        if (!string.IsNullOrEmpty(entry.ChannelId))
                            continue;
                        try
                        {
                            var channel = StatePlatform.GetChannel(entry.Url);
                            if (channel?.ID?.Value != null)
                            {
                                entry.ChannelId = channel.ID.Value;
                                _blocked.Save(entry);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.w(nameof(StateBlockedChannels), $"Failed to resolve channel id for blocked channel [{entry.Url}]", ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.w(nameof(StateBlockedChannels), "Failed to backfill blocked channel ids", ex);
                }
            });
        }

        public DateTimeOffset GetBlockedTime(string url)
        {
            if (string.IsNullOrEmpty(url))
                return DateTimeOffset.MinValue;
            var blocked = _blocked.FindObject(x => x.IsSameUrl(url.ToLower()));
            return (blocked != null) ? DateTimeOffset.FromUnixTimeSeconds(blocked.BlockedTime) : DateTimeOffset.MinValue;
        }

        public DateTimeOffset GetBlockedRemovalTime(string url)
        {
            if (string.IsNullOrEmpty(url))
                return DateTimeOffset.MinValue;
            return DateTimeOffset.FromUnixTimeSeconds(_blockedRemoved.GetValue(url.ToLower(), 0));
        }

        public void SetBlockedRemovalTime(string url, long unixTime)
        {
            if (string.IsNullOrEmpty(url))
                return;
            _blockedRemoved.SetAndSave(url.ToLower(), unixTime);
        }

        public Dictionary<string, long> GetBlockedRemovals()
        {
            return new Dictionary<string, long>(_blockedRemoved.All());
        }

        public void Add(BlockedChannel channel, bool isUserInteraction = false, DateTimeOffset? time = null)
        {
            if (channel == null || string.IsNullOrEmpty(channel.Url))
                return;
            Normalize(channel);
            var now = time?.ToUnixTimeSeconds() ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var existing = _blocked.FindObject(x => x.IsSameUrl(channel.Url));
            if (existing != null)
            {
                existing.Name = channel.Name;
                existing.Thumbnail = channel.Thumbnail;
                existing.PluginId = channel.PluginId;
                existing.ChannelId = channel.ChannelId;
                existing.UrlAlternatives = channel.UrlAlternatives;
                existing.BlockedTime = now;
                _blocked.Save(existing);
                channel = existing;
            }
            else
            {
                if (channel.BlockedTime <= 0)
                    channel.BlockedTime = now;
                _blocked.Save(channel);
            }

            ClearRemovalTombstone(channel.Url);
            OnChanged?.Invoke();

            if (isUserInteraction)
                BroadcastChange(new SyncBlockedChannelsPackage()
                {
                    Channels = new List<BlockedChannel>() { channel }
                });
        }

        public void Remove(string url, bool isUserInteraction = false, DateTimeOffset? time = null)
        {
            if (string.IsNullOrEmpty(url))
                return;
            var blocked = _blocked.FindObject(x => x.IsSameUrl(url.ToLower()));
            if (blocked == null)
                return;

            _blocked.Delete(blocked);
            var unblockTime = time?.ToUnixTimeSeconds() ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _blockedRemoved.SetAndSave(blocked.Url.ToLower(), unblockTime);
            OnChanged?.Invoke();

            if (isUserInteraction)
                BroadcastChange(new SyncBlockedChannelsPackage()
                {
                    ChannelRemovals = new Dictionary<string, long>()
                    {
                        { blocked.Url, unblockTime }
                    }
                });
        }

        public void ReplaceAll(List<BlockedChannel> channels)
        {
            foreach (var channel in channels ?? new List<BlockedChannel>())
            {
                if (channel != null && !string.IsNullOrEmpty(channel.Url))
                    Normalize(channel);
            }
            _blocked.ReplaceAll(channels ?? new List<BlockedChannel>());
            OnChanged?.Invoke();
        }

        private void ClearRemovalTombstone(string url)
        {
            if (string.IsNullOrEmpty(url))
                return;
            var norm = url.ToLower();
            var existing = _blockedRemoved.All();
            if (existing.ContainsKey(norm))
            {
                existing.Remove(norm);
                _blockedRemoved.Save(existing);
            }
        }

        private void BroadcastChange(SyncBlockedChannelsPackage package)
        {
            Task.Run(async () =>
            {
                try
                {
                    await StateSync.Instance.BroadcastJsonAsync(GJSyncOpcodes.SyncBlockedChannels, package);
                }
                catch (Exception ex)
                {
                    Logger.w(nameof(StateBlockedChannels), "Failed to send blocked channels changes to sync clients", ex);
                }
            });
        }

        public List<IManagedStore> ToMigrateCheck()
        {
            return new List<IManagedStore>()
            {
                _blocked
            };
        }

        private static readonly object _instanceLock = new object();
        private static StateBlockedChannels? _instance = null;
        public static StateBlockedChannels Instance
        {
            get
            {
                lock (_instanceLock)
                {
                    if (_instance == null)
                        _instance = new StateBlockedChannels();
                    return _instance;
                }
            }
        }
    }
}
