using Grayjay.ClientServer.Exceptions;
using Grayjay.ClientServer.Models;
using Grayjay.ClientServer.Models.Subscriptions;
using Grayjay.ClientServer.Store;
using Grayjay.ClientServer.Sync.Models;
using Grayjay.ClientServer.Sync;
using Grayjay.Desktop.POC;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine.Exceptions;
using Grayjay.Engine.Models.Detail;
using Grayjay.Engine.Models.Feed;
using Grayjay.ClientServer.Subscriptions;

namespace Grayjay.ClientServer.States;

public class StatePlaylists
{
    private static readonly ManagedStore<Playlist> _playlists = new ManagedStore<Playlist>("playlists")
        .WithUnique(x => x.Id)
        .WithRestore<PlaylistReconstructionStore>()
        .Load();

    private static DictionaryStore<string, long> _playlistRemoved = new DictionaryStore<string, long>("playlist_removed", new Dictionary<string, long>())
        .Load();

    public static Playlist? LastPlayed => _playlists.MaxByNotNull(p => p.DatePlayed);
    public static Playlist? LastUpdated => _playlists.MaxByNotNull(p => p.DateUpdate);
    public static List<Playlist> All => _playlists.GetObjects();

    public static event Action OnPlaylistsChanged;

    static StatePlaylists()
    {
        OnPlaylistsChanged += () =>
        {
            StateWebsocket.PlaylistsChanged();
        };
    }



    public static List<IManagedStore> ToMigrateCheck()
    {
        return new List<IManagedStore>()
        {
            _playlists
        };
    }


    public static Playlist? Get(string id) => _playlists.FindObject(p => p.Id == id);
    public static DateTimeOffset GetPlaylistRemoval(string id)
    {
        return DateTimeOffset.FromUnixTimeSeconds(Math.Max(_playlistRemoved.GetValue(id, 0), 0));
    }
    public static Dictionary<string, long> GetPlaylistRemovals()
    {
        return _playlistRemoved.All();
    }
    public static void AddContentToPlaylists(string[] playlistIds, PlatformVideo content)
    {
        List<Playlist> changed = new List<Playlist>();
        foreach (var playlistId in playlistIds)
        {
            changed.Add(_playlists.Update(v => v.Id, playlistId, (playlist) =>
            {
                playlist.DatePlayed = DateTime.Now;
                playlist.DateUpdate = DateTime.Now;
                playlist.Videos = new List<PlatformVideo>(playlist.Videos)
                {
                    content
                };
            }));
        }
        BroadcastSyncPlaylists(changed);
        foreach(var playlistId in playlistIds)
        {
            var downloaded = StateDownloads.GetDownloadingPlaylist(playlistId);
            if(downloaded != null)
            {
                _ =StateDownloads.StartDownloadCycle();
                return;
            }
        }
    }

    public static void RemoveContentFromPlaylist(string playlistId, int index)
    {
        var playlist = _playlists.Update(v => v.Id, playlistId, (playlist) => 
        {
            playlist.DatePlayed = DateTime.Now;
            playlist.DateUpdate = DateTime.Now;
            var newList = playlist.Videos.ToList();
            newList.RemoveAt(index);
            playlist.Videos = newList;

            var dlPlaylist = StateDownloads.GetDownloadingPlaylist(playlistId);
            if (dlPlaylist != null)
                _ = StateDownloads.CheckOutdatedPlaylistVideos(playlist, dlPlaylist);
        });
        BroadcastSyncPlaylists(new List<Playlist>()
        {
            playlist
        });
    }

    public static void SetPlayed(string id)
    {
        var playlist = _playlists.Update(v => v.Id, id, playlist =>
        {
            playlist.DatePlayed = DateTime.Now;
            playlist.DateUpdate = DateTime.Now;
        });
        BroadcastSyncPlaylists(new List<Playlist>()
        {
            playlist
        });
    }

    public static void CreateOrUpdate(Playlist playlist, bool isUserInteraction = true)
    {
        _playlists.CreateOrUpdate(v => v.Id, playlist.Id, () => {
            playlist.DateCreation = DateTime.Now;
            playlist.DatePlayed = DateTime.Now;
            playlist.DateUpdate = DateTime.Now;
            return playlist;
        }, p =>
        {
            p.DatePlayed = DateTime.Now;
            p.DateUpdate = DateTime.Now;
            p.Name = playlist.Name;
            p.Videos = playlist.Videos;
        });

        OnPlaylistsChanged?.Invoke();

        if (isUserInteraction)
        {
            BroadcastSyncPlaylists(new List<Playlist>()
            {
                playlist
            });
        }
    }

    public static void Remove(string id, bool isUserInteraction = true)
    {
        _playlists.DeleteBy(v => v.Id, id);
        OnPlaylistsChanged?.Invoke();
        if (isUserInteraction)
        {
            _playlistRemoved.SetAndSave(id.ToString(), DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            BroadcastSyncPlaylists(null, _playlistRemoved.All());
        }
    }

    private static void BroadcastSyncPlaylists(List<Playlist> playlists, Dictionary<string, long> removals = null)
    {
        Task.Run(async () =>
        {
            try
            {
                await StateSync.Instance.BroadcastJsonAsync(GJSyncOpcodes.SyncPlaylists, new SyncPlaylistsPackage()
                {
                    Playlists = playlists,
                    PlaylistRemovals = removals ?? new Dictionary<string, long>()
                });
            }
            catch (Exception ex)
            {
                Logger.w(nameof(StateSubscriptions), "Failed to send subs changes to sync clients", ex);
            }
        });
    }

    private class PlaylistReconstructionStore : ReconstructStore<Playlist>
    {
        public override string ToReconstruction(Playlist obj)
        {
            var items = new List<string>();
            items.Add(obj.Name + ":::" + obj.Id);
            items.AddRange(obj.Videos.Select(x => x.Url));
            return string.Join("\n", items.Select(x => x.Replace("\n", "")));
        }
        public override Playlist ToObject(string id, string backup, Builder builder, StateBackup.ImportCache cache = null)
        {
            var items = backup.Split("\n");
            if (items.Length <= 0)
                throw new InvalidDataException($"Cannot reconstruct playlist {id}");

            var name = items[0];
            if(name.Contains(":::"))
            {
                int splitIndex = name.IndexOf(":::");
                string foundId = name.Substring(splitIndex + 3);
                if (!string.IsNullOrEmpty(foundId))
                    id = foundId;
                name = name.Substring(0, splitIndex);
            }
            var videos = items.Skip(1).Where(x => !string.IsNullOrEmpty(x)).Select(videoUrl =>
            {
                try
                {
                    PlatformContent video = cache?.Videos.FirstOrDefault(x => x.Url == videoUrl) ?? (PlatformContent)StatePlatform.GetContentDetails(videoUrl);
                    if (video is PlatformVideo)
                        return (PlatformVideo)video;
                    else
                        return null;
                }
                catch (ScriptUnavailableException ex)
                {
                    throw new ReconstructionException(name, $"{name}:[{videoUrl}] is no longer available", ex);
                }
                catch (NoPlatformClientException ex)
                {
                    throw new ReconstructionException(name, $"No source enabled for [{videoUrl}]", ex);
                }
                catch (Exception ex)
                {
                    throw new ReconstructionException(name, $"{name}:[{videoUrl}] {ex.Message}", ex);
                }
            }).Where(x => x != null).ToList();
            return new Playlist()
            {
                Id = id,
                Name = name,
                Videos = videos
            };
        }
    }
}