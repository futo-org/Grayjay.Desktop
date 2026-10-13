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

using Logger = Grayjay.Desktop.POC.Logger;
using System.Collections.Generic;

namespace Grayjay.ClientServer.States
{
    public static class StateDownloads
    {
        private static object _downloadLock = new object();
        private static bool _downloadCycleRequested = false;
        public static bool IsDownloading { get; private set; }

        private static DirectoryInfo _downloadsDirectory = new DirectoryInfo("downloads");

        public static ManagedStore<PlaylistDownload> _downloadingPlaylists = new ManagedStore<PlaylistDownload>("downloads_playlists")
            .WithUnique(x=>x.PlaylistID)
            .WithKeepUnloadable()
            .Load();

        public static ManagedStore<VideoDownload> _downloading = new ManagedStore<VideoDownload>("downloads_ongoing")
            .WithUnique(x=>x.Video.ID)
            .WithKeepUnloadable()
            .Load();
        public static ManagedStore<VideoLocal> _downloaded = new ManagedStore<VideoLocal>("downloaded")
            .WithUnique(x=>x.VideoDetails.ID)
            .WithKeepUnloadable()
            .Load();

        public static StorageInfo GetDownloadStorageInfo() => StorageInfo.GetInfo(_downloadsDirectory.FullName);

        public static event Action<VideoDownload, VideoLocal> OnDownloadCompleted;
        public static event Action OnDownloadsChanged;

        static StateDownloads()
        {
            string path = Path.Combine(StateApp.GetAppDirectory().FullName, "downloads");
            Directory.CreateDirectory(path);
            _downloadsDirectory = new DirectoryInfo(path);
            try
            {
                ResolveMovedFiles(path, _downloaded);
            }
            catch (Exception ex)
            {
                Logger.e(nameof(StateDownloads), "Failed to resolve moved downloaded files", ex);
            }
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
        /// <summary>Points records whose files no longer exist at their recorded path at the file of the same name in the downloads folder.</summary>
        /// <returns>The number of records that were updated and saved.</returns>
        internal static int ResolveMovedFiles(string downloadsDirectory, ManagedStore<VideoLocal> downloaded)
        {
            int updatedRecords = 0;
            foreach (var local in downloaded.GetObjects())
            {
                bool changed = false;
                foreach (var file in DownloadedFiles(local))
                {
                    if (string.IsNullOrEmpty(file.FilePath) || File.Exists(file.FilePath))
                        continue;
                    string candidate = Path.Combine(downloadsDirectory, FileNameOf(file.FilePath));
                    if (!File.Exists(candidate))
                        continue;
                    Logger.i(nameof(StateDownloads), $"Downloaded file [{file.FilePath}] moved to [{candidate}]");
                    //The DASH cache is keyed by the full file path, so the one next to the file is stale
                    DeleteDashCache(candidate);
                    file.SetFilePath(candidate);
                    changed = true;
                }
                if (!changed)
                    continue;
                //A record that cannot be saved still works this session and must not stop the others
                try
                {
                    downloaded.Save(local, onlyExisting: true);
                    updatedRecords++;
                }
                catch (Exception ex)
                {
                    Logger.e(nameof(StateDownloads), $"Failed to save the moved files of [{local.VideoDetails?.ID?.Value}]", ex);
                }
            }
            return updatedRecords;
        }

        private record DownloadedFile(string FilePath, Action<string> SetFilePath);
        private static IEnumerable<DownloadedFile> DownloadedFiles(VideoLocal local)
            => local.VideoSources.Select(source => new DownloadedFile(source.FilePath, path => source.FilePath = path))
                .Concat(local.AudioSources.Select(source => new DownloadedFile(source.FilePath, path => source.FilePath = path)))
                .Concat(local.SubtitleSources.Select(source => new DownloadedFile(source.FilePath, path => source.FilePath = path)));

        /// <summary>The file name of a path written on any OS, so a Windows path also resolves on Linux.</summary>
        internal static string FileNameOf(string filePath) => filePath.Split('\\', '/').Last();

        private static void DeleteDashCache(string videoPath)
        {
            try
            {
                Transcoding.LocalDash.DeleteDownloadCache(videoPath);
            }
            catch (Exception ex)
            {
                Logger.w(nameof(StateDownloads), $"Failed to delete DASH cache of [{videoPath}]", ex);
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
            VideoDownload download = new VideoDownload(video, targetPixelCount, targetBitrate, null, "playlist", playlistId);
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
            var vid = GetDownloadingVideo(id);
            RemoveDownloading(vid);
        }
        public static void RemoveDownloading(VideoDownload download)
        {
            if (_downloading.Delete(download))
            {
                download.Cancel();
                OnDownloadsChanged?.Invoke();
            }
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
            foreach (var source in local.VideoSources)
            {
                try { Transcoding.LocalDash.DeleteDownloadCache(source.FilePath); }
                catch (Exception ex) { Logger.w(nameof(StateDownloads), "Failed to delete downloaded DASH cache", ex); }
            }
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
        public static Task StartDownloadCycle() => RunExclusiveDownloadCycle(RunDownloadCycle);

        private static async Task RunExclusiveDownloadCycle(Func<Task> runCycle)
        {
            lock(_downloadLock)
            {
                if (IsDownloading)
                {
                    // Picked up by the running cycle so a download queued while it finishes is not lost.
                    _downloadCycleRequested = true;
                    return;
                }
                IsDownloading = true;
            }

            while (true)
            {
                try
                {
                    await runCycle();
                }
                catch (Exception ex)
                {
                    Logger.e(nameof(StateDownloads), "Download cycle failed: " + ex.Message, ex);
                }

                lock (_downloadLock)
                {
                    if (!_downloadCycleRequested)
                    {
                        IsDownloading = false;
                        return;
                    }
                    _downloadCycleRequested = false;
                }
            }
        }
        private static async Task RunDownloadCycle()
        {
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
                        continue;
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
                    Logger.e(nameof(StateDownloads), $"Failed download [{currentVideo.Video?.Name ?? currentVideo.VideoDetails?.Name}]: {ex.Message}", ex);

                if(ex is AggregateException ex2)
                {
                    currentVideo.Error = ex.Message + "\n\n" + string.Join("\n\n", ex2.InnerExceptions.Select(x => x.Message));
                }
                else
                    currentVideo.Error = ex.Message;
                currentVideo.PrepareTime = null;
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
            using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(StateApp.AppCancellationToken.Token, download.CancellationToken);
            var cancel = cancellationSource.Token;
            //TODO: Should download?

            if((download.PrepareTime?.DifferenceNowMinutes() ?? 99) > 15)
            {
                Logger.w(nameof(StateDownloads), $"Video Download [{download.Video?.Name ?? download.VideoDetails?.Name}] expired, re-preparing");
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
                download.Prepare(new ManagedHttpClient(), cancel);
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

        public static void CleanupFiles() => CleanupFiles(GetDownloadsDirectory(), _downloaded);
        private static void CleanupFiles(string dirPath, ManagedStore<VideoLocal> downloaded)
        {
            var unloadableIds = downloaded.UnloadableIds;
            if (unloadableIds.Count > 0)
            {
                Logger.w(nameof(StateDownloads), $"Skipping downloads cleanup, {unloadableIds.Count} downloaded records could not be loaded [{string.Join(", ", unloadableIds)}]");
                return;
            }
            DirectoryInfo dir = new DirectoryInfo(dirPath);
            var knownFiles = new HashSet<string>(downloaded.GetObjects()
                .Concat(downloaded.DuplicateObjects)
                .SelectMany(x => x.VideoSources.Select(y => Path.GetFileName(y.FilePath))
                .Concat(x.AudioSources.Select(z => Path.GetFileName(z.FilePath)))
                .Concat(x.SubtitleSources.Select(z => Path.GetFileName(z.FilePath)))
            ));
            List<FileInfo> toDelete = new List<FileInfo>();
            foreach (var cache in dir.GetDirectories("*.dash"))
            {
                var videoName = cache.Name[..^5];
                if (!knownFiles.Contains(videoName))
                {
                    try { Transcoding.LocalDash.DeleteDownloadCache(Path.Combine(dirPath, videoName)); }
                    catch (Exception ex) { Logger.w(nameof(StateDownloads), "Failed to delete unused DASH cache", ex); }
                }
            }
            foreach(var file in dir.GetFiles())
            {
                if (!knownFiles.Contains(file.Name))
                    toDelete.Add(file);
            }
            Logger.w("StateDownloads", $"Found {toDelete.Count} redundant downloaded files, deleting");
            foreach (var del in toDelete)
            {
                try
                {
                    Logger.w("StateDownloads", $"Deleting redundant download file [{del.Name}]");
                    del.Delete();
                }
                catch (Exception ex)
                {
                    Logger.w("StateDownloads", $"Failed to delete unknown downloads file [{del.Name}]");
                }
            }
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
