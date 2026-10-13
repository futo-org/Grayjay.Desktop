using Grayjay.ClientServer.Exceptions;
using Grayjay.ClientServer.Helpers;
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
using System.Text.RegularExpressions;
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
                var videoSources = VideoHelper.ReorderVideoSources(VideoHelper.ExpandUMPVideoSources(details.Video.VideoSources).Where(x=>x.IsDownloadable()).ToList(), (details.Video is UnMuxedVideoDescriptor unmux && (unmux.AudioSources?.Any(x=>x.IsDownloadable()) ?? false)) || details.Video.VideoSources.Any(x => x is UMPSource));
                var manifestPlaylists = new Dictionary<int, Parsers.HLS.MasterPlaylist>();
                _sources = new DownloadSources()
                {
                    VideoSources = videoSources,
                    AudioSources = VideoHelper.ReorderAudioSources(((details.Video is UnMuxedVideoDescriptor unmux2) ? unmux2.AudioSources.Where(x=>x.IsDownloadable()).ToList() : new List<IAudioSource>())
                        .Concat(VideoHelper.GetUMPAudioSources(details.Video.VideoSources).Where(x => x.IsDownloadable())).ToList()),
                    SubtitleSources = details.Subtitles.ToList(),
                    ManifestSources = videoSources.Where(x => x is HLSManifestSource)
                        .ToDictionary(x => videoSources.IndexOf(x), y =>
                        {
                            var hlsSource = y as HLSManifestSource;
                            try
                            {
                                var modifier = hlsSource?.GetRequestModifier();

                                var manifest = Parsers.HLS.DownloadAndParsePlaylist(hlsSource.Url, modifier).Result;
                                if (manifest is not Parsers.HLS.MasterPlaylist masterPlaylist)
                                    return new List<HLSVariantVideoUrlSource>();
                                manifestPlaylists[videoSources.IndexOf(y)] = masterPlaylist;
                                return masterPlaylist.GetVideoSources();
                            }
                            catch (Exception ex)
                            {
                                Logger.w(nameof(DownloadController), "Failed to extract HLS manifest: " + ex.Message);
                                return new List<HLSVariantVideoUrlSource>();
                            }

                        })
                };
                _sources.ManifestPlaylists = manifestPlaylists;
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
                (sourceVideo, sourceAudio) = SelectManifestVariant(sources, videoIndex, manifestIndex, sourceAudio);
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

        /// <summary>Maps a chosen HLS variant to a downloadable source, adding its separate audio rendition when no audio was chosen.</summary>
        internal static (IVideoSource Video, IAudioSource? Audio) SelectManifestVariant(DownloadSources sources, int videoIndex, int manifestIndex, IAudioSource? sourceAudio)
        {
            var modifier = (sources.VideoSources[videoIndex] as HLSManifestSource)?.GetRequestModifier();
            var variant = sources.ManifestSources[videoIndex][manifestIndex];
            variant.Modifier = modifier;
            if (sourceAudio == null && sources.ManifestPlaylists.TryGetValue(videoIndex, out var masterPlaylist))
            {
                var renditions = masterPlaylist.GetAudioSourcesForVariant(variant.Url);
                foreach (var rendition in renditions)
                    rendition.Modifier = modifier;
                sourceAudio = VideoDownload.SelectHLSAudioSource(renditions, null);
            }
            return (variant, sourceAudio);
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
                .Where(x=>x?.Video != null)
                .ToArray();
            if(downloads.Length > 0)
            {
                string outputFolder = await GrayjayServer.Instance.GetWindowProviderOrThrow().ShowDirectoryDialogAsync(CancellationToken.None);
                if(!string.IsNullOrEmpty(outputFolder))
                {
                    return ExportsFinished(downloads, outputFolder, ExportAllToFolder(downloads, outputFolder));
                }
                return Ok();
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
                string? missingFile = MissingExportFile(videoSource?.FilePath, audioSource?.FilePath);
                if (missingFile != null)
                    throw DialogException.FromException("Downloaded file is missing", new FileNotFoundException($"Grayjay could not find [{missingFile}]. It may have been moved or deleted. Delete this download and download it again to export it.", missingFile));

                string defaultName = ExportFileName(download.Name, videoSource, audioSource);
                string? chosenFile = await GrayjayServer.Instance.GetWindowProviderOrThrow().ShowSaveFileDialogAsync(defaultName, new (string, string)[0]);

                if (string.IsNullOrEmpty(chosenFile))
                    throw DialogException.FromException("Export cancelled", new Exception("No valid export path provided"));
                string outputFile = ExportFilePath(chosenFile, defaultName);

                if ((videoSource != null || audioSource != null) && (videoSource == null || audioSource == null))
                {
                    try
                    {
                        if (videoSource != null)
                            System.IO.File.Copy(videoSource.FilePath, outputFile, true);
                        else
                            System.IO.File.Copy(audioSource.FilePath, outputFile, true);
                    }
                    catch(Exception ex)
                    {
                        throw DialogException.FromException("Failed to copy files", ex);
                    }
                    return ExportFinished(download, outputFile, CheckExportAudio(download));
                }
                else
                {
                    var subtitle = download.SubtitleSources?.FirstOrDefault();
                    string? subtitlePath = ExportSubtitlePath(subtitle?.FilePath);
                    if (subtitle != null && subtitlePath == null)
                    {
                        Logger.w(nameof(DownloadController), $"Subtitle file [{subtitle.FilePath}] of [{download.Name}] is missing, exporting without subtitles");
                        StateUI.Toast("The subtitle file is missing, exporting without subtitles");
                    }
                    string[] args = ExportMergeArguments(videoSource.FilePath, audioSource.FilePath, subtitlePath, outputFile);
                    Logger.i(nameof(DownloadController), "Exporting with FFMPEG:\n" + string.Join(" ", args));
                    (var exitCode, var errors) = ExecuteExport(args);
                    if (exitCode == 0)
                        return ExportFinished(download, outputFile, ExportAudio.HasAudio);

                    var message = $"FFmpeg exited with code {exitCode}.";
                    if (errors.Count > 0) message += "\n" + string.Join("\n", errors);
                    throw DialogException.FromException("Failed to export files", new InvalidOperationException(message));
                }
            }
            else
                return NotFound();
        }
        private IActionResult ExportFinished(VideoLocal video, string outputPath, ExportAudio audio)
        {
            if (audio == ExportAudio.NoAudio)
                Logger.w(nameof(DownloadController), $"Exported [{video.Name}] without audio, no audio was downloaded");
            else if (audio == ExportAudio.Unknown)
                Logger.w(nameof(DownloadController), $"Exported [{video.Name}], its audio could not be verified");
            StateUI.Dialog("", "Export Completed",
                ExportFinishedText(video.Name, audio),
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
        private IActionResult ExportsFinished(VideoLocal[] video, string outputPath, ExportResults results)
        {
            if (results.WithoutAudio.Count > 0)
                Logger.w(nameof(DownloadController), "Exported without audio, no audio was downloaded: " + string.Join(", ", results.WithoutAudio));
            if (results.AudioUnverified.Count > 0)
                Logger.w(nameof(DownloadController), "Exported, audio could not be verified: " + string.Join(", ", results.AudioUnverified));
            StateUI.Dialog("", results.Failures.Count == 0 ? "Export Completed" : "Export Completed With Errors",
                ExportsFinishedText(video.Length, results),
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

        /// <summary>Exports one download into a folder, replacing an existing file.</summary>
        /// <returns>The reason it failed, or null when it succeeded.</returns>
        internal static string? ExportToFolder(VideoLocal download, string outputFolder)
        {
            var videoSource = (LocalVideoSource?)download.VideoSources?.FirstOrDefault();
            var audioSource = (download.Video is UnMuxedVideoDescriptor mvideo && mvideo.AudioSources != null) ? (LocalAudioSource?)mvideo.AudioSources.FirstOrDefault() : null;

            if (videoSource == null && audioSource == null)
                return "No sources found";
            string? missingFile = MissingExportFile(videoSource?.FilePath, audioSource?.FilePath);
            if (missingFile != null)
                return $"Downloaded file is missing [{missingFile}]";

            string outputFile = Path.Combine(outputFolder, ExportFileName(download.Name, videoSource, audioSource));

            try
            {
                if (videoSource == null || audioSource == null)
                {
                    if (videoSource != null)
                        System.IO.File.Copy(videoSource.FilePath, outputFile.SanitizeFileNameWithPath(), true);
                    else
                        System.IO.File.Copy(audioSource!.FilePath, outputFile.SanitizeFileNameWithPath(), true);
                    return null;
                }

                string[] args = ExportMergeArguments(videoSource.FilePath, audioSource.FilePath, null, outputFile.SanitizeFileNameWithPath());
                Logger.i(nameof(DownloadController), "Exporting with FFMPEG:\n" + string.Join(" ", args));
                (var exitCode, var errors) = ExecuteExport(args);
                if (exitCode == 0)
                    return null;
                Logger.e(nameof(DownloadController), $"Failed to export [{download.Name}], FFmpeg exited with code {exitCode}:\n{string.Join("\n", errors)}");
                return $"FFmpeg exited with code {exitCode}" + (errors.Count > 0 ? $" ({errors.Last()})" : "");
            }
            catch (Exception ex)
            {
                Logger.e(nameof(DownloadController), $"Failed to export [{download.Name}]", ex);
                return ex.Message;
            }
        }

        private static readonly string[] Mp4VideoCodecs = { "avc1", "avc3", "h264", "hvc1", "hev1", "hevc", "av01", "av1", "mp4v" };
        private static readonly string[] Mp4AudioCodecs = { "mp4a", "aac", "mp3", "opus", "ac-3", "ec-3" };
        private static readonly string[] WebmVideoCodecs = { "vp8", "vp9", "vp09", "av01", "av1" };
        private static readonly string[] WebmAudioCodecs = { "opus", "vorbis" };
        private static readonly string[] Mp4Containers = { "mp4", "mpegurl", "audio/mpeg" };
        private static readonly string[] WebmContainers = { "webm" };

        /// <summary>Picks the extension for merging a video and an audio stream: webm or mp4 when both streams fit it, otherwise mkv.</summary>
        internal static string ExportContainerFor(string videoContainer, string? videoCodec, string audioContainer, string? audioCodec)
        {
            bool bothFitMp4 = FitsExportContainer(videoContainer, videoCodec, Mp4Containers, Mp4VideoCodecs) && FitsExportContainer(audioContainer, audioCodec, Mp4Containers, Mp4AudioCodecs);
            bool bothFitWebm = FitsExportContainer(videoContainer, videoCodec, WebmContainers, WebmVideoCodecs) && FitsExportContainer(audioContainer, audioCodec, WebmContainers, WebmAudioCodecs);
            if (bothFitMp4 && bothFitWebm)
                return videoContainer.Contains("webm", StringComparison.OrdinalIgnoreCase) ? "webm" : "mp4";
            if (bothFitWebm)
                return "webm";
            if (bothFitMp4)
                return "mp4";
            return "mkv";
        }

        private static bool FitsExportContainer(string container, string? codec, string[] containers, string[] codecs)
        {
            if (string.IsNullOrWhiteSpace(codec))
                return containers.Any(family => container?.Contains(family, StringComparison.OrdinalIgnoreCase) ?? false);
            return codecs.Any(prefix => codec.Trim().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The sanitized default file name for exporting a download with the given sources.</summary>
        internal static string ExportFileName(string name, LocalVideoSource? videoSource, LocalAudioSource? audioSource)
        {
            string extension;
            if (videoSource != null && audioSource != null)
                extension = ExportContainerFor(videoSource.Container, videoSource.Codec, audioSource.Container, audioSource.Codec);
            else if (videoSource != null)
                extension = videoSource.Container.VideoContainerToExtension();
            else
                extension = audioSource!.Container.AudioContainerToExtension();
            return name.SanitizeFileName() + "." + extension;
        }

        /// <summary>The downloaded subtitle file to add to an export, or null when there is none or it is missing.</summary>
        internal static string? ExportSubtitlePath(string? subtitlePath)
            => !string.IsNullOrEmpty(subtitlePath) && System.IO.File.Exists(subtitlePath) ? subtitlePath : null;

        /// <summary>FFmpeg arguments that merge a video and an audio file, and a subtitle file when given, without re-encoding.</summary>
        internal static string[] ExportMergeArguments(string videoPath, string audioPath, string? subtitlePath, string outputFile)
        {
            var args = new List<string> { "-i", videoPath, "-i", audioPath };
            if (subtitlePath != null)
            {
                args.AddRange(new[] { "-i", subtitlePath });
            }
            args.AddRange(new[] { "-map", "0:v", "-map", "1:a" });
            if (subtitlePath != null)
            {
                args.AddRange(new[] { "-map", "2:s" });
            }
            args.AddRange(new[] { "-c:v", "copy", "-c:a", "copy" });
            if (subtitlePath != null)
            {
                string subtitleCodec = Path.GetExtension(outputFile).ToLowerInvariant() switch
                {
                    ".webm" => "webvtt",
                    ".mkv" => "srt",
                    _ => "mov_text"
                };
                args.AddRange(new[] { "-c:s", subtitleCodec });
            }
            args.Add("-y");
            args.Add(outputFile);
            return args.ToArray();
        }

        /// <summary>The first given downloaded file that does not exist, or null when all exist.</summary>
        internal static string? MissingExportFile(params string?[] filePaths)
            => filePaths.FirstOrDefault(filePath => !string.IsNullOrEmpty(filePath) && !System.IO.File.Exists(filePath));

        /// <summary>The chosen export path, with the default file name's extension added when it has none.</summary>
        internal static string ExportFilePath(string chosenPath, string defaultName)
        {
            if (_fileExtension.IsMatch(Path.GetFileName(chosenPath)))
                return chosenPath;
            return chosenPath.TrimEnd('.') + Path.GetExtension(defaultName);
        }
        private static readonly Regex _fileExtension = new Regex(@"\.(?=[0-9]*[A-Za-z])[A-Za-z0-9]{1,5}$", RegexOptions.Compiled);

        internal enum ExportAudio
        {
            HasAudio,
            NoAudio,
            Unknown
        }

        internal record ExportResults(List<string> Failures, List<string> WithoutAudio, List<string> AudioUnverified);

        /// <summary>Exports each download to the folder and sorts the names into failures, exports without audio and exports whose audio could not be verified.</summary>
        internal static ExportResults ExportAllToFolder(VideoLocal[] downloads, string outputFolder)
        {
            var results = new ExportResults(new List<string>(), new List<string>(), new List<string>());
            foreach (var download in downloads)
            {
                var failure = ExportToFolder(download, outputFolder);
                if (failure != null)
                {
                    results.Failures.Add($"{download.Name}: {failure}");
                    continue;
                }
                var audio = CheckExportAudio(download);
                if (audio == ExportAudio.NoAudio)
                    results.WithoutAudio.Add(download.Name);
                else if (audio == ExportAudio.Unknown)
                    results.AudioUnverified.Add(download.Name);
            }
            return results;
        }

        internal static string ExportFinishedText(string name, ExportAudio audio)
        {
            var text = "Video [" + name + "] is ready at the location below";
            if (audio == ExportAudio.NoAudio)
                text += "\nIt was exported without audio, as no audio was downloaded.";
            else if (audio == ExportAudio.Unknown)
                text += "\nIts audio could not be verified, as the video file could not be inspected.";
            return text;
        }

        internal static string ExportsFinishedText(int total, ExportResults results)
        {
            var details = new StringBuilder(results.Failures.Count == 0
                ? "Videos are ready at the location below"
                : $"{total - results.Failures.Count} of {total} videos are ready at the location below");
            if (results.Failures.Count > 0)
                details.Append("\n\nFailed:\n" + string.Join("\n", results.Failures));
            if (results.WithoutAudio.Count > 0)
                details.Append("\n\nExported without audio, as no audio was downloaded:\n" + string.Join("\n", results.WithoutAudio));
            if (results.AudioUnverified.Count > 0)
                details.Append("\n\nExported, but audio could not be verified:\n" + string.Join("\n", results.AudioUnverified));
            return details.ToString();
        }

        /// <summary>Whether a download's export carries audio. A video exported alone is judged by its file, as the saved descriptor does not keep its type.</summary>
        internal static ExportAudio CheckExportAudio(VideoLocal download)
        {
            var videoSource = download.VideoSources.FirstOrDefault();
            if (videoSource == null || download.AudioSources.Count > 0)
                return ExportAudio.HasAudio;
            return ProbeFileAudio(videoSource.FilePath);
        }

        private static ExportAudio ProbeFileAudio(string path)
        {
            bool noAudioStream = false;
            int exitCode;
            try
            {
                //FFmpeg selects the audio stream itself; "-v error" keeps file metadata out of the output
                exitCode = FFMPEG.ExecuteSafe(new[] { "-hide_banner", "-nostdin", "-v", "error", "-i", path, "-map", "0:a:0", "-c", "copy", "-frames:a", "1", "-f", "null", "-" }, print: false, onLog: (line, isError) =>
                {
                    if (line != null && line.StartsWith("Stream map '") && line.Contains("matches no streams"))
                        noAudioStream = true;
                });
            }
            catch (Exception ex)
            {
                Logger.w(nameof(DownloadController), $"Could not probe [{path}] for audio", ex);
                return ExportAudio.Unknown;
            }
            if (exitCode == 0)
                return ExportAudio.HasAudio;
            return noAudioStream ? ExportAudio.NoAudio : ExportAudio.Unknown;
        }

        private static (int ExitCode, List<string> Errors) ExecuteExport(string[] args)
        {
            var errors = new Queue<string>();
            var exitCode = FFMPEG.ExecuteSafe(args, onLog: (line, isError) =>
            {
                if (!isError || string.IsNullOrWhiteSpace(line)) return;
                lock (errors)
                {
                    errors.Enqueue(line);
                    while (errors.Count > 12) errors.Dequeue();
                }
            });
            lock (errors)
                return (exitCode, errors.ToList());
        }

        public class DownloadSources
        {
            public string ID { get; set; } = Guid.NewGuid().ToString();
            public List<IVideoSource> VideoSources { get; set; }
            public List<IAudioSource> AudioSources { get; set; }
            public List<SubtitleSource> SubtitleSources { get; set; }

            public Dictionary<int, List<HLSVariantVideoUrlSource>> ManifestSources { get; set; }
            [System.Text.Json.Serialization.JsonIgnore]
            public Dictionary<int, Parsers.HLS.MasterPlaylist> ManifestPlaylists { get; set; } = new Dictionary<int, Parsers.HLS.MasterPlaylist>();
        }


    }
}
