using Grayjay.ClientServer.Controllers;
using Grayjay.ClientServer.Exceptions;
using Grayjay.ClientServer.Models;
using Grayjay.ClientServer.Models.Downloads;
using Grayjay.ClientServer.Settings;
using Grayjay.ClientServer.Store;
using Grayjay.Desktop.POC;
using Grayjay.Engine;
using Grayjay.Engine.Models.Detail;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Models.General;
using Grayjay.Engine.Models.Video.Sources;
using Grayjay.Engine.Web;
using System.Runtime.CompilerServices;
using PlatformID = Grayjay.Engine.Models.General.PlatformID;

namespace Grayjay.ClientServer.States
{
    public static class StateDownloads
    {
        private static object _downloadLock = new object();
        public static bool IsDownloading { get; private set; }

        private static DirectoryInfo _downloadsDirectory = new DirectoryInfo("downloads");

        public static ManagedStore<PlaylistDownload> _downloadingPlaylists = new ManagedStore<PlaylistDownload>("downloads_playlists")
            .WithUnique(x=>x.PlaylistID)
            .Load();

        public static ManagedStore<VideoDownload> _downloading = new ManagedStore<VideoDownload>("downloads_ongoing")
            .WithUnique(x=>x.Video.ID)
            .Load();
        public static ManagedStore<VideoLocal> _downloaded = new ManagedStore<VideoLocal>("downloaded")
            .WithUnique(x=>x.VideoDetails.ID)
            .Load();

        public static StorageInfo GetDownloadStorageInfo() => StorageInfo.GetInfo(_downloadsDirectory.FullName);

        public static event Action<VideoDownload, VideoLocal> OnDownloadCompleted;
        public static event Action OnDownloadsChanged;

        static StateDownloads()
        {
            string path = Path.Combine(StateApp.GetAppDirectory().FullName, "downloads");
            Directory.CreateDirectory(path);
            _downloadsDirectory = new DirectoryInfo(path);
            OnDownloadCompleted += (download, local) =>
            {
                if (local != null)
                    GrayjayServer.Instance.WebSocket.Broadcast(local, "DownloadCompleted", local.VideoDetails.ID.Value);
            };
            OnDownloadsChanged += () =>
            {
                GrayjayServer.Instance.WebSocket.Broadcast(null, "DownloadsChanged", null);
            };
        }

        public static void ChangeDownloadDirectory(string dir)
        {
            if (!Directory.Exists(dir))
                return;

            StateUI.Toast("Moving directory to\n[" + dir + "]");
            Logger.i(nameof(DownloadController), "Change download directory to [" + dir + "]");

            DirectoryInfo newDirectory = new DirectoryInfo(dir);
            DirectoryInfo oldDirectory = new DirectoryInfo(GetDownloadsDirectory());

            if (!newDirectory.Exists)
                return;
            if (!oldDirectory.Exists)
                return;

            //TODO: Catch issues, change dir
            foreach(FileInfo info in oldDirectory.GetFiles())
            {
                File.Move(info.FullName, Path.Combine(newDirectory.FullName, info.Name));
            }
        }
        public static string GetDownloadsDirectory()
        {
            if(!_downloadsDirectory.Exists)
                _downloadsDirectory.Create();
            return _downloadsDirectory.FullName;
        }

        public static List<VideoDownload> GetDownloading() => _downloading.GetObjects();
        public static List<VideoLocal> GetDownloaded() => _downloaded.GetObjects();
        public static List<VideoLocal> GetDownloaded(string groupId) => _downloaded.FindObjects(x => x.GroupID == groupId);

        public static VideoDownload GetDownloadingVideo(PlatformID id)
            => _downloading.FindObject(x => x.Video.ID.Equals(id));
        public static VideoLocal? GetDownloadedVideo(PlatformID id)
            => _downloaded.FindObject(x => x.ID.Equals(id));
        public static VideoLocal? GetDownloadedVideo(string url)
            => _downloaded.FindObject(x => x.Url?.Equals(url, StringComparison.OrdinalIgnoreCase) ?? false);

        public static PlaylistDownload GetDownloadingPlaylist(string id)
            => _downloadingPlaylists.FindObject(x => x.PlaylistID == id);
        public static List<PlaylistDownload> GetDownloadingPlaylists()
            => _downloadingPlaylists.GetObjects();

        public static VideoDownload StartDownload(PlatformVideoDetails video, IVideoSource? vsource = null, IAudioSource? asource = null, ISubtitleSource? ssource = null)
        {
            VideoDownload download = new VideoDownload(video, vsource, asource, ssource);
            UpdateDownloading(download);
            OnDownloadsChanged?.Invoke();
            return download;
        }
        public static VideoDownload StartDownload(PlatformVideo video, long? targetPixelCount = null, long? targetBitrate = null, string playlistId = null)
        {
            VideoDownload download = new VideoDownload(video, targetPixelCount, targetBitrate, "playlist", playlistId);
            UpdateDownloading(download);
            OnDownloadsChanged?.Invoke();
            return download;
        }

        public static PlaylistDownload StartDownload(string playlistId, int targetPixelCount = -1, int targetBitrate = -1)
        {
            PlaylistDownload download = new PlaylistDownload(playlistId, targetPixelCount, targetBitrate);
            UpdateDownloading(download);
            return download;
        }

        public static void UpdateDownloaded(VideoLocal local)
        {
            _downloaded.Save(local);
        }
        public static void UpdateDownloading(VideoDownload download)
        {
            _downloading.Save(download);
        }
        public static void UpdateDownloading(PlaylistDownload download)
        {
            _downloadingPlaylists.Save(download);
        }

        public static void RemoveDownloading(PlatformID id)
        {
            if (_downloading.Delete(GetDownloadingVideo(id)))
                OnDownloadsChanged?.Invoke();
        }
        public static void RemoveDownloading(VideoDownload download)
        {
            if (_downloading.Delete(download))
                OnDownloadsChanged?.Invoke();
        }

        public static void RemoveDownloadingPlaylist(string playlistId)
        {
            var playlist = GetDownloadingPlaylist(playlistId);
            _downloadingPlaylists.Delete(playlist);

            foreach (var download in GetDownloading().Where(x => x.GroupID == playlistId))
                RemoveDownloading(download.Video?.ID);
            foreach (var download in GetDownloaded().Where(x => x.GroupID == playlistId))
                RemoveDownloaded(download.ID);
        }

        public static void RemoveDownloaded(PlatformID id)
            => RemoveDownloaded(GetDownloadedVideo(id));
        public static void RemoveDownloaded(VideoLocal local)
        {
            if(local == null)
                return;
            local.DeleteFiles();
            if (_downloaded.Delete(local))
                OnDownloadsChanged?.Invoke();
        }

        public static async Task CheckOutdatedPlaylistVideos(Playlist playlist, PlaylistDownload download)
        {
            var outdatedVideos = GetDownloaded(download.PlaylistID)
                .Where(x => !playlist.Videos.Any(y => y.ID.Equals(x.ID)))
                .ToArray();
            foreach (var outdated in outdatedVideos)
                RemoveDownloaded(outdated);
        }
        public static async Task StartDownloadCycle()
        {
            lock(_downloadLock)
            {
                if (IsDownloading)
                    return;
                IsDownloading = true;
            }
            Logger.i(nameof(StateDownloads), "Started downloading cycle");


            List<PlaylistDownload> playlistsToDownload = StateDownloads.GetDownloadingPlaylists();
            foreach (var playlistDownload in playlistsToDownload)
            {
                try
                {
                    var playlist = StatePlaylists.Get(playlistDownload.PlaylistID);
                    if(playlist == null)
                    {
                        StateDownloads.RemoveDownloadingPlaylist(playlistDownload.PlaylistID);
                        return;
                    }

                    await CheckOutdatedPlaylistVideos(playlist, playlistDownload);

                    foreach (var video in playlist.Videos)
                    {
                        var existingDownloading = StateDownloads.GetDownloadingVideo(video.ID);
                        if (existingDownloading != null)
                            continue;
                        var existingDownloaded = StateDownloads.GetDownloadedVideo(video.ID);
                        if (existingDownloaded != null)
                            continue;
                        StartDownload(video, 
                            (playlistDownload.TargetPixelCount != null && playlistDownload.TargetPixelCount >= 0) ? playlistDownload.TargetPixelCount : null, 
                            (playlistDownload.TargetBitrate != null && playlistDownload.TargetBitrate >= 0) ? playlistDownload.TargetBitrate : null,
                            playlist.Id.ToString()); ;
                    }
                }
                catch (Exception ex)
                {
                    Logger.e(nameof(StateDownloads), "Failed to download playlist " + playlistDownload.PlaylistID + " due to " + ex.Message, ex);
                }
            }


            List<VideoDownload> ignore = new List<VideoDownload>();
            var currentVideo = StateDownloads.GetDownloading().FirstOrDefault();
            while (currentVideo != null)
            {
                await TryDownloadVideo(currentVideo, ignore);

                Thread.Sleep(500);
                currentVideo = StateDownloads.GetDownloading().Where(x => !ignore.Contains(x)).FirstOrDefault();
            }

            Logger.i(nameof(StateDownloads), "Ended downloading cycle");
            IsDownloading = false;
        }
        private static async Task TryDownloadVideo(VideoDownload currentVideo, List<VideoDownload> ignore)
        {
            try
            {
                await DownloadVideo(currentVideo);
                if (currentVideo.State == DownloadState.COMPLETED)
                {
                    OnDownloadCompleted?.Invoke(currentVideo, GetDownloadedVideo(currentVideo.Video.ID));
                }
            }
            catch (Exception ex)
            {
                Logger.e(nameof(StateDownloads), "Download failed", ex);

                if (currentVideo.Video == null && currentVideo.VideoDetails == null)
                {
                    //Corrupt
                    Logger.w(nameof(StateDownloads), "Video had no video or videodetail, removing download");
                    //StateDownloads.RemoveDownload(currentVideo);
                }
                else if (ex is DownloadException dex && !dex.IsRetryable)
                {
                    Logger.w(nameof(StateDownloads), "Video had exception that should not be retried");
                    //StateDownloads.RemoveDownload(currentVideo);
                    //StateDownloads.PreventPlaylistDownload(currentVideo);
                }
                else
                    Logger.e(nameof(StateDownloads), $"Failed download [{currentVideo.Video.Name}]: {ex.Message}", ex);

                if(ex is AggregateException ex2)
                {
                    currentVideo.Error = ex.Message + "\n\n" + string.Join("\n\n", ex2.InnerExceptions.Select(x => x.Message));
                }
                else
                    currentVideo.Error = ex.Message;
                currentVideo.ChangeState(DownloadState.ERROR);
                ignore.Add(currentVideo);

                //TODO: Handle cancel
            }
            finally
            {
            }
        }
        private static async Task DownloadVideo(VideoDownload download)
        {
            var cancel = StateApp.AppCancellationToken.Token;
            //TODO: Should download?

            if((download.PrepareTime?.DifferenceNowMinutes() ?? 99) > 15)
            {
                Logger.w(nameof(StateDownloads), $"Video Download [{download.Video.Name}] expired, re-preparing");
                download.VideoDetails = null;

                if (download.TargetPixelCount == null && download.VideoSource != null)
                    download.TargetPixelCount = (download.VideoSource.Width * download.VideoSource.Height);
                download.VideoSource = null;
                if (download.TargetBitrate == null && download.AudioSource != null)
                    download.TargetBitrate = download.AudioSource.Bitrate;
                download.AudioSource = null;
            }
            if (download.VideoDetails == null || (download.VideoSource == null && download.AudioSource == null))
                download.ChangeState(DownloadState.PREPARING);
            NotifyDownload(download);

            Logger.i(nameof(StateDownloads), $"Preparing [{download.AudioFileName}] started");
            if (download.State == DownloadState.PREPARING)
                download.Prepare(new ManagedHttpClient());
            download.ChangeState(DownloadState.DOWNLOADING);
            NotifyDownload(download);

            var lastNotifyTime = DateTime.MinValue;
            Logger.i(nameof(StateDownloads), $"Downloading [{download.Video.Name}] started");
            //TODO: use plugin client
            await download.Download(new ManagedHttpClient(), (progress) =>
            {
                download.Progress = progress;

                var currentTime = DateTime.Now;
                if(currentTime.Subtract(lastNotifyTime).TotalMilliseconds > 500)
                {
                    NotifyDownload(download);
                    lastNotifyTime = DateTime.Now;
                }
            }, cancel);
            Logger.i(nameof(StateDownloads), $"Download [{download.Video.Name}] finished");
            StateDownloads.UpdateDownloading(download);

            download.ChangeState(DownloadState.VALIDATING);
            NotifyDownload(download);

            Logger.i(nameof(StateDownloads), $"Validating [{download.Video.Name}]");
            download.Validate();
            download.ChangeState(DownloadState.FINALIZING);
            NotifyDownload(download);

            Logger.i(nameof(StateDownloads), $"Completing [{download.Video.Name}]");
            download.Complete();
            download.ChangeState(DownloadState.COMPLETED);

            StateDownloads.RemoveDownloading(download);
            NotifyDownload(download);
        }

        public static void NotifyDownload(VideoDownload download)
        {
            try
            {
                GrayjayServer.Instance?.WebSocket?.Broadcast(download, "DownloadChanged", download.Video.ID.Value);
            }
            catch { }
        }
    }
}
