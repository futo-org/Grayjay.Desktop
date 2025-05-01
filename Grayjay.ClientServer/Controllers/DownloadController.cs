using Grayjay.ClientServer.Exceptions;
using Grayjay.ClientServer.Models.Downloads;
using Grayjay.ClientServer.States;
using Grayjay.ClientServer.Transcoding;
using Grayjay.Desktop.POC;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine.Models.Detail;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Models.General;
using Grayjay.Engine.Models.Subtitles;
using Grayjay.Engine.Models.Video;
using Grayjay.Engine.Models.Video.Sources;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Text;
using PlatformID = Grayjay.Engine.Models.General.PlatformID;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class DownloadController : ControllerBase
    {
        private static object _lock = new object();
        private static PlatformVideoDetails _details = new PlatformVideoDetails();
        private static DownloadSources _sources = new DownloadSources();

        public static (PlatformVideoDetails, DownloadSources) GetLoadedSources(string id)
        {
            lock(_lock)
            {
                if (_sources?.ID != id)
                    throw new BadHttpRequestException("Expired download id");

                return (_details, _sources);
            }
        }

        public static PlatformVideoDetails LoadDownload(PlatformVideoDetails details)
        {
            lock (_lock)
            {
                _details = details;
                _sources = new DownloadSources()
                {
                    VideoSources = details.Video.VideoSources.Where(x=>x.IsDownloadable()).ToList(),
                    AudioSources = (details.Video is UnMuxedVideoDescriptor unmux) ? unmux.AudioSources.Where(x=>x.IsDownloadable()).ToList() : new List<IAudioSource>(),
                    SubtitleSources = details.Subtitles.ToList(),
                    ManifestSources = details.Video.VideoSources.Where(x => x is HLSManifestSource)
                        .ToDictionary(x => Array.IndexOf(details.Video.VideoSources, x), y =>
                        {
                            var hlsSource = y as HLSManifestSource;
                            try
                            {
                                var manifest = Parsers.HLS.DownloadAndParsePlaylist(hlsSource.Url).Result;
                                return manifest.GetVideoSources();
                            }
                            catch (Exception ex)
                            {
                                Logger.w(nameof(DownloadController), "Failed to extract HLS manifest: " + ex.Message);
                                return new List<HLSVariantVideoUrlSource>();
                            }

                        })
                };
                return details;
            }
        }
        public static PlatformVideoDetails LoadDownload(string url)
        {
            var details = StatePlatform.GetContentDetails(url);
            if (!(details is PlatformVideoDetails))
                throw new InvalidDataException("Not a video: " + url);
            LoadDownload(details as PlatformVideoDetails);
            return details as PlatformVideoDetails;
        }

        [HttpGet]
        public DownloadSources LoadDownloadSources(string url)
        {
            LoadDownload(url);
            return _sources;
        }

        [HttpGet]
        public IActionResult DownloadCycle()
        {
            StateDownloads.StartDownloadCycle();

            return Ok(true);
        }

        [HttpGet]
        public IActionResult Download(string id, int videoIndex, int audioIndex, int subtitleIndex, int manifestIndex = -1)
        {
            (var details, var sources) = GetLoadedSources(id);
            
            var sourceVideo = (videoIndex >= 0) ? sources.VideoSources[videoIndex] : null;
            var sourceAudio = (audioIndex >= 0) ? sources.AudioSources[audioIndex] : null;
            var sourceSubtitle = (subtitleIndex >= 0) ? sources.SubtitleSources[subtitleIndex] : null;

            if(sourceVideo is HLSManifestSource)
            {
                if (manifestIndex < 0)
                    return BadRequest("Missing manifestIndex");
                sourceVideo = sources.ManifestSources[videoIndex][manifestIndex];
            }


            VideoDownload existing = StateDownloads.GetDownloadingVideo(details.ID);

            //TODO: Edgecases
            if (existing != null)
                return BadRequest("Already downloaded");

            if(sourceSubtitle?.HasFetch ?? false)
            {
                try
                {
                    Logger.i(nameof(DownloadController), "Pre-fetching subtitles");
                    SubtitleRawSource subtitle = sourceSubtitle.ToRaw();
                    sourceSubtitle = subtitle;
                }
                catch(Exception ex)
                {
                    Logger.w(nameof(DownloadController), "Failed to pre-convert subtitles, they may still be fetched", ex);
                }
            }

            var download = StateDownloads.StartDownload(details, sourceVideo, sourceAudio, sourceSubtitle);

            StateDownloads.StartDownloadCycle();

            return Ok(download);
        }

        [HttpGet]
        public IActionResult DownloadPlaylist(string playlistId, int pixelCount, int bitrate)
        {
            var playlist = StatePlaylists.Get(playlistId);

            PlaylistDownload existing = StateDownloads.GetDownloadingPlaylist(playlistId);

            if (existing != null)
                return BadRequest("Already downloading");

            var download = StateDownloads.StartDownload(playlistId, pixelCount, bitrate);

            StateDownloads.StartDownloadCycle();

            return Ok(download);
        }

        [HttpGet]
        public StorageInfo GetStorageInfo()
        {
            return StateDownloads.GetDownloadStorageInfo();
        }

        [HttpGet]
        public bool PromptDirectoryChange()
        {
            //TODO: Prompt cross-platform dialog
            return false;
        }


        [HttpGet]
        public List<VideoDownload> GetDownloading()
        {
            return StateDownloads.GetDownloading();
        }

        [HttpGet]
        public List<PlaylistDownload.WithPlaylistModel> GetDownloadingPlaylists()
        {
            return StateDownloads.GetDownloadingPlaylists()
                .Select(x => x.WithPlaylist())
                .ToList();
        }

        [HttpGet]
        public List<VideoLocal> GetDownloaded()
        {
            //TODO: Pager
            return StateDownloads.GetDownloaded();
        }

        [HttpPost]
        public bool DeleteDownload([FromBody]PlatformID id)
        {
            StateDownloads.RemoveDownloading(id);
            StateDownloads.RemoveDownloaded(id);
            return true;
        }

        [HttpGet]
        public bool DeleteDownloadPlaylist(string id)
        {
            StateDownloads.RemoveDownloadingPlaylist(id);
            return true;
        }

        [HttpGet]
        public async Task<bool> ChangeDownloadDirectory(string directory)
        {
            if (GrayjayServer.Instance.ServerMode)
                throw DialogException.FromException("Download directory change not supported in server-mode", new Exception("For changing download directory, run the application in ui mode, server support might be added at a later time"));

            string dir = await GrayjayServer.Instance.GetWindowProviderOrThrow().ShowDirectoryDialogAsync();
            if (!string.IsNullOrEmpty(dir))
            {
                StateDownloads.ChangeDownloadDirectory(dir);
            }

            return true;
        }

        [HttpPost]
        public async Task<IActionResult> ExportDownloads([FromBody] PlatformID[] ids)
        {
            if (GrayjayServer.Instance.ServerMode)
                throw DialogException.FromException("Export not supported in server-mode", new Exception("For export support, run the application in ui mode, server support might be added at a later time"));

            var downloads = ids.Select(x => StateDownloads.GetDownloadedVideo(x))
                .Where(x=>x.Video != null)
                .ToArray();
            if(downloads.Length > 0)
            {
                string outputFolder = await GrayjayServer.Instance.GetWindowProviderOrThrow().ShowDirectoryDialogAsync(CancellationToken.None);
                if(!string.IsNullOrEmpty(outputFolder))
                {

                    foreach(var download in downloads)
                    {
                        var videoSource = (LocalVideoSource?)download.VideoSources?.FirstOrDefault();
                        var audioSource = (download.Video is UnMuxedVideoDescriptor mvideo && mvideo.AudioSources != null) ? (LocalAudioSource?)mvideo.AudioSources.FirstOrDefault() : null;

                        string outputFile = (videoSource != null) ?
                            download.Name.SanitizeFileName() + "." + videoSource.Container.VideoContainerToExtension() :
                            download.Name.SanitizeFileName() + "." + audioSource.Container.AudioContainerToExtension();
                        outputFile = Path.Combine(outputFolder, outputFile);

                        if ((videoSource != null || audioSource != null) && (videoSource == null || audioSource == null))
                        {
                            try
                            {
                                if (videoSource != null)
                                    System.IO.File.Copy(videoSource.FilePath, outputFile.SanitizeFileNameWithPath(), true);
                                else
                                    System.IO.File.Copy(audioSource.FilePath, outputFile.SanitizeFileNameWithPath(), true);
                            }
                            catch (Exception ex)
                            {
                                throw DialogException.FromException("Failed to copy files", ex);
                            }
                        }
                        else
                        {
                            //throw DialogException.FromException("Merge not implemented yet", new NotImplementedException());

                            StringBuilder ffmpegQuery = new StringBuilder();
                            string[] args = new string[]
                            {
                                "-i", videoSource.FilePath,
                                "-i", audioSource.FilePath,
                                "-map", "0:v",
                                "-map", "1:a",
                                "-c:v", "copy",
                                "-c:a", "copy",
                                "-y",
                                outputFile.SanitizeFileNameWithPath()
                            };
                            /*
                            ffmpegQuery.Append($" -i \"{videoSource.FilePath}\"");
                            ffmpegQuery.Append($" -i \"{audioSource.FilePath}\"");
                            ffmpegQuery.Append(" -map 0:v");
                            ffmpegQuery.Append(" -map 1:a");
                            ffmpegQuery.Append(" -c:v copy");
                            ffmpegQuery.Append(" -c:a copy");
                            ffmpegQuery.Append(" -y");
                            ffmpegQuery.Append($" \"{outputFile.SanitizeFileNameWithPath()}\"");
                            */

                            string query = ffmpegQuery.ToString();
                            Logger.i(nameof(DownloadController), "Exporting with FFMPEG:\n" + query);
                            FFMPEG.ExecuteSafe(args);
                        }
                    }
                }
                return ExportsFinished(downloads, outputFolder);
            }
            return NotFound();
        }

        [HttpPost]
        public async Task<IActionResult> ExportDownload([FromBody] PlatformID id)
        {
            if (GrayjayServer.Instance.ServerMode)
                throw DialogException.FromException("Export not supported in server-mode", new Exception("For export support, run the application in ui mode, server support might be added at a later time"));

            var download = StateDownloads.GetDownloadedVideo(id);
            if (download != null && download.Video != null)
            {
                var videoSource = (LocalVideoSource?)download.VideoSources?.FirstOrDefault();
                var audioSource = (download.Video is UnMuxedVideoDescriptor mvideo && mvideo.AudioSources != null) ? (LocalAudioSource?)mvideo.AudioSources.FirstOrDefault() : null;

                if (videoSource == null && audioSource == null)
                    throw new Exception("No sources found");

                string outputFile = (videoSource != null) ?
                    await GrayjayServer.Instance.GetWindowProviderOrThrow().ShowSaveFileDialogAsync(download.Name + "." + videoSource.Container.VideoContainerToExtension(), new (string, string)[0]) :
                    await GrayjayServer.Instance.GetWindowProviderOrThrow().ShowSaveFileDialogAsync(download.Name + "." + audioSource.Container.AudioContainerToExtension(), new (string, string)[0]);

                if (string.IsNullOrEmpty(outputFile))
                    throw DialogException.FromException("Export cancelled", new Exception("No valid export path provided"));

                if ((videoSource != null || audioSource != null) && (videoSource == null || audioSource == null))
                {
                    try
                    {
                        if (videoSource != null)
                            System.IO.File.Copy(videoSource.FilePath, outputFile);
                        else
                            System.IO.File.Copy(audioSource.FilePath, outputFile);
                        return ExportFinished(download, outputFile);
                    }
                    catch(Exception ex)
                    {
                        throw DialogException.FromException("Failed to copy files", ex);
                    }
                }
                else
                {
                    //throw DialogException.FromException("Merge not implemented yet", new NotImplementedException());

                    FileInfo subtitleFile = null;
                    var subtitle = download.SubtitleSources?.FirstOrDefault();
                    if (subtitle != null)
                    {
                        if (false)//subtitle is SubtitleRawSource subRaw && !string.IsNullOrEmpty(subRaw._Subtitles))
                        {
                            switch (subtitle.Format)
                            {
                                case "text/vtt":
                                    subtitleFile = StateApp.GetTemporaryFile(".vtt", "sub");
                                    //System.IO.File.WriteAllText(subtitleFile.FullName, subRaw._Subtitles);
                                    break;
                                case "application/x-subrip":
                                    subtitleFile = StateApp.GetTemporaryFile(".srt", "sub");
                                    //System.IO.File.WriteAllText(subtitleFile.FullName, subRaw._Subtitles);
                                    break;
                            }
                        }
                        else if(string.IsNullOrEmpty(subtitle.FilePath))
                        {
                            subtitleFile = new FileInfo(subtitle.FilePath);
                        }
                    }



                    StringBuilder ffmpegQuery = new StringBuilder();
                    ffmpegQuery.Append($" -i \"{videoSource.FilePath}\"");
                    ffmpegQuery.Append($" -i \"{audioSource.FilePath}\"");
                    if(subtitle != null)
                        ffmpegQuery.Append($" -i \"{subtitle.FilePath}\"");
                    ffmpegQuery.Append(" -map 0:v");
                    ffmpegQuery.Append(" -map 1:a");
                    if (subtitle != null)
                        ffmpegQuery.Append($" -map 2:s");
                    ffmpegQuery.Append(" -c:v copy");
                    ffmpegQuery.Append(" -c:a copy");
                    if (subtitle != null)
                        ffmpegQuery.Append(" -c:s mov_text");
                    ffmpegQuery.Append(" -y");
                    ffmpegQuery.Append($" \"{outputFile}\"");

                    string query = ffmpegQuery.ToString();
                    List<string> args = new List<string>();
                    args.AddRange(new string[]
                    {
                        "-i", videoSource.FilePath,
                        "-i", audioSource.FilePath,
                    });
                    if (subtitle != null)
                        args.AddRange(new string[]
                        {
                            "-i", subtitle.FilePath
                        });
                    args.AddRange(new string[]
                    {
                        "-map", "0:v",
                        "-map", "1:a",
                    });
                    if (subtitle != null)
                        args.AddRange(new string[]
                        {
                            "-map", "2:s"
                        });
                    args.AddRange(new string[]
                    {
                        "-c:v", "copy",
                        "-c:a", "copy",
                    });
                    if(subtitle != null)
                        args.AddRange(new string[]
                        {
                            "-c:s", "mov_text"
                        });
                    args.Add("-y");
                    args.Add(outputFile);


                    Logger.i(nameof(DownloadController), "Exporting with FFMPEG:\n" + query);
                    if (FFMPEG.ExecuteSafe(args.ToArray()) == 0)
                        return ExportFinished(download, outputFile);
                    else
                        throw DialogException.FromException("Failed to transcode export files", new InvalidDataException());
                }
            }
            else
                return NotFound();
        }
        private IActionResult ExportFinished(VideoLocal video, string outputPath)
        {
            StateUI.Dialog("", "Export Completed",
                "Video [" + video.Name + "] is ready at the location below",
                outputPath,
                0,
                    new StateUI.DialogAction("Ok", () => { }, StateUI.ActionStyle.Primary),
                    new StateUI.DialogAction("Open File", () =>
                    {
                        OSHelper.OpenFile(outputPath);
                    }, StateUI.ActionStyle.Primary)
            );
            return Ok();
        }
        private IActionResult ExportsFinished(VideoLocal[] video, string outputPath)
        {
            StateUI.Dialog("", "Export Completed",
                "Videos are ready at the location below",
                outputPath,
                0,
                    new StateUI.DialogAction("Ok", () => { }, StateUI.ActionStyle.Primary),
                    new StateUI.DialogAction("Open Folder", () =>
                    {
                        OSHelper.OpenFolder(outputPath);
                    }, StateUI.ActionStyle.Primary)
            );
            return Ok();
        }

        public class DownloadSources
        {
            public string ID { get; set; } = Guid.NewGuid().ToString();
            public List<IVideoSource> VideoSources { get; set; }
            public List<IAudioSource> AudioSources { get; set; }
            public List<SubtitleSource> SubtitleSources { get; set; }

            public Dictionary<int, List<HLSVariantVideoUrlSource>> ManifestSources { get; set; }
        }


    }
}
