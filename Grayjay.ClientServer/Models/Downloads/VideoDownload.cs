using Grayjay.ClientServer.Constants;
using Grayjay.ClientServer.Exceptions;
using Grayjay.ClientServer.Helpers;
using Grayjay.ClientServer.Parsers;
using Grayjay.ClientServer.Sabr;
using Grayjay.ClientServer.Settings;
using Grayjay.ClientServer.States;
using Grayjay.ClientServer.Transcoding;
using Grayjay.Desktop.POC;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine.Dash;
using Grayjay.Engine.Models.Detail;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Models.Subtitles;
using Grayjay.Engine.Models.Video;
using Grayjay.Engine.Models.Video.Additions;
using Grayjay.Engine.Models.Video.Sources;
using Grayjay.Engine.Web;
using Microsoft.ClearScript;
using SQLitePCL;
using System;
using System.Diagnostics.Eventing.Reader;
using System.Net;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static Grayjay.ClientServer.ModifierHttp;
using static Grayjay.ClientServer.Parsers.HLS;

namespace Grayjay.ClientServer.Models.Downloads
{
    public class VideoDownload
    {
        public DownloadState State { get; set; }
        public PlatformVideo Video { get; set; }
        public PlatformVideoDetails VideoDetails { get; set; }


        public long? TargetPixelCount { get; set; }
        public long? TargetBitrate { get; set; }
        public string? TargetLanguage { get; set; }
        public string? SubtitleName { get; set; }

        public IVideoSource? VideoSource { get; set; }
        public bool VideoSourceRequiresLive { get; set; }
        [JsonIgnore]
        private IVideoSource? VideoSourceLive { get; set; }
        [JsonIgnore]
        private IVideoSource? VideoSourceToUse => VideoSourceLive ?? VideoSource;
        public StreamMetaData? VideoSourceMetaDataOverride { get; set; }
        public string VideoSourceMimeTypeOverride { get; set; }

        public IAudioSource? AudioSource { get; set; }
        public bool AudioSourceRequiresLive { get; set; }
        [JsonIgnore]
        public IAudioSource? AudioSourceLive { get; set; }
        [JsonIgnore]
        private IAudioSource? AudioSourceToUse => AudioSourceLive ?? AudioSource;
        [JsonIgnore]
        public IAudioSource? AudioSourceOverride { get; set; }
        public StreamMetaData? AudioSourceMetaDataOverride { get; set; }
        public string AudioSourceMimeTypeOverride { get; set; }

        public SubtitleSource.Serializable? SubtitleSource { get; set; }
        public bool SubtitleSourceRequiresLive { get; set; }
        [JsonIgnore]
        public ISubtitleSource? SubtitleSourceLive { get; set; }
        public ISubtitleSource? SubtitleSourcetoUse => SubtitleSourceLive ?? SubtitleSource;

        public DateTime? PrepareTime { get; set; }
        public double Progress { get; set; } = 0.0;
        public bool IsCancelled { get; set; } = false;

        public long DownloadSpeedVideo { get; set; }
        public long DownloadSpeedAudio { get; set; }
        public long DownloadSpeed => DownloadSpeedVideo + DownloadSpeedAudio;

        public string? Error { get; set; }


        public string? VideoFilePath { get; set; }
        public string? VideoFileName { get; set; }
        public long? VideoFileSize { get; set; }

        public string? AudioFilePath { get; set; }
        public string? AudioFileName { get; set; }
        public long? AudioFileSize { get; set; }

        public string? SubtitleFilePath { get; set; }
        public string? SubtitleFileName { get; set; }

        public string? GroupType { get; set; }
        public string? GroupID { get; set; }

        public event Action<VideoDownload, DownloadState> OnStateChanged;
        public event Action<VideoDownload, double> OnProgressChanged;


        public bool RequireVideoSource { get; set; }
        public bool RequireAudioSource { get; set; }


        public VideoDownload() { }
        public VideoDownload(PlatformVideo video, long? targetPixelCount = null, long? targetBitRate = null, string targetLanguage = null, string groupType = null, string groupId = null)
        {
            Video = video;
            VideoSource = null;
            AudioSource = null;
            GroupType = groupType;
            GroupID = groupId;
            SubtitleSource = null;
            TargetPixelCount = targetPixelCount;
            TargetBitrate = targetBitRate;
            RequireVideoSource = targetPixelCount != null;
            RequireAudioSource = TargetBitrate != null;
        }
        public VideoDownload(PlatformVideoDetails video, IVideoSource? videoSource = null, IAudioSource? audioSource = null, ISubtitleSource? subtitleSource = null)
        {
            Video = video;
            VideoDetails = video;
            VideoSource = videoSource;
            AudioSource = audioSource;
            if (subtitleSource?.HasFetch ?? true)
                SubtitleSource = null;
            else
                SubtitleSource = new SubtitleSource.Serializable(subtitleSource);
            SubtitleSourceLive = subtitleSource;
            TargetPixelCount = (videoSource != null) ? videoSource.Width * videoSource.Height : null;
            TargetBitrate = audioSource?.Bitrate;
            if (audioSource != null)
                TargetLanguage = (audioSource?.Language != Language.UNKNOWN) ? audioSource?.Language : null;
            else
                TargetLanguage = (videoSource?.Language != Language.UNKNOWN) ? videoSource?.Language : null;

            SubtitleName = subtitleSource?.Name;

            VideoSourceRequiresLive = videoSource is UMPVideoFormatSource || videoSource is UMPSource ||
                (videoSource is DashManifestRawSource dashManifestRawSource && dashManifestRawSource.HasGenerate) ||
                (videoSource is JSSource videoJSSource && (videoJSSource.HasRequestExecutor || videoJSSource.HasRequestModifier));
            AudioSourceRequiresLive = audioSource is UMPAudioFormatSource ||
                (audioSource is DashManifestRawAudioSource dashManifestRawAudioSource && dashManifestRawAudioSource.HasGenerate) ||
                (audioSource is JSSource audioJSSource && (audioJSSource.HasRequestExecutor || audioJSSource.HasRequestModifier));
            SubtitleSourceRequiresLive = subtitleSource?.HasFetch ?? false;

            RequireVideoSource = videoSource != null;
            RequireAudioSource = audioSource != null;
        }

        public VideoDownload WithGroup(string groupType, string groupId)
        {
            GroupType = groupType;
            GroupID = groupId;
            return this;
        }

        public string GetDownloadInfo()
        {
            string videoInfo = null;
            if (VideoSource != null)
            {
                videoInfo = $"{VideoSource.Width}x{VideoSource.Height} ({VideoSource.Container})";
            }
            else if (TargetPixelCount != null && TargetPixelCount > 0)
            {
                int guessWidth = (int)((4 * Math.Sqrt((double)TargetPixelCount)) / 3);
                int guessHeight = (int)((3 * Math.Sqrt((double)TargetPixelCount)) / 4);
                videoInfo = $"{guessWidth}x{guessHeight}";
            }

            string audioInfo = null;
            if (AudioSource != null)
            {
                audioInfo = AudioSource.Bitrate.ToHumanBitrate();
            }
            else if (TargetBitrate != null && TargetBitrate > 0)
            {
                audioInfo = TargetBitrate?.ToHumanBitrate();
            }

            var items = new string?[] { videoInfo, audioInfo };
            var filteredItems = Array.FindAll(items, item => item != null);

            return string.Join(" • ", filteredItems);
        }


        public void ChangeState(DownloadState state)
        {
            State = state;
            OnStateChanged?.Invoke(this, state);
        }

        public void Prepare(ManagedHttpClient client)
        {
            if (Video == null && VideoDetails == null)
                throw new InvalidOperationException("Missing information for download to complete");
            if (TargetPixelCount == null && TargetBitrate == null && VideoSource == null && AudioSource == null)
                throw new InvalidOperationException("No sources or query values set");

            //If live source are required, ensure a live object is present
            if (VideoSourceRequiresLive)
            {
                VideoDetails = null;
                VideoSource = null;
                VideoSourceLive = null;
            }
            if (AudioSourceRequiresLive)
            {
                VideoDetails = null;
                AudioSource = null;
                AudioSourceLive = null;
            }
            if (SubtitleSourceRequiresLive)
            {
                SubtitleSource = null;
                SubtitleSourceLive = null;
            }

            //The following exceptions seem contradictory for certain cases.
            //if (Video == null && VideoDetails == null) //Include query options?
            //    throw new InvalidDataException("Missing information for download to complete");
            //if (TargetPixelCount == null && TargetBitrate == null && VideoSourceToUse == null && AudioSourceToUse == null && VideoFileName == null && AudioFileName == null)
            //    throw new InvalidOperationException("No sources or query values set");

            if (Video != null && (VideoDetails == null || VideoSourceRequiresLive || AudioSourceRequiresLive || SubtitleSourceRequiresLive))
            {
                var originalContent = StatePlatform.GetContentDetails(Video.Url);
                if (!(originalContent is PlatformVideoDetails))
                    throw new InvalidOperationException("Original content is not media");
                var original = (PlatformVideoDetails)originalContent;

                if (original.Video.HasAnySource() && !original.IsDownloadable())
                {
                    Logger.i(nameof(VideoDownload), $"Attempted to download unsupported video [{original.Name}]:{original.Url}");
                    throw new DownloadException("Unsupported video for downloading", false);
                }

                VideoSourceMetaDataOverride = null;
                VideoSourceMimeTypeOverride = null;
                AudioSourceMetaDataOverride = null;
                AudioSourceMimeTypeOverride = null;

                VideoDetails = original;
                if (VideoSource == null && TargetPixelCount != null)
                {
                    var videoSources = new List<IVideoSource>();
                    foreach (var source in VideoHelper.ExpandUMPVideoSources(original.Video.VideoSources))
                    {
                        if (source is HLSManifestSource hlsManifestSource)
                        {
                            try
                            {
                                var modifier = hlsManifestSource!.GetRequestModifier();
                                var res = ModifierHttp.GetBytes(client, hlsManifestSource!.Url, modifier);
                                if (res.IsOk)
                                {
                                    var sources = HLS.ParseToVideoSources(source, Encoding.UTF8.GetString(res.Bytes), res.FinalUrl);
                                    foreach (var subSource in sources)
                                        if (subSource != null)
                                            subSource.Modifier = modifier;
                                    videoSources.AddRange(sources);
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.i(nameof(VideoDownload), $"Failed to fetch hls manifest: {ex.Message}");
                            }
                        }
                        else if (source.IsDownloadable())
                            videoSources.Add(source);
                    }

                    var vsource = VideoHelper.SelectBestVideoSource(videoSources, (int)TargetPixelCount, new List<string>(), TargetLanguage, TargetLanguage != null);
                    if (vsource != null)
                    {
                        if (vsource is VideoUrlSource || vsource is DashManifestRawSource || vsource is UMPVideoFormatSource)
                            VideoSource = vsource;
                        else
                            throw new DownloadException("Video source is not supported for downloading (yet)", false);
                    }
                }

                if (AudioSource == null && TargetBitrate != null)
                {
                    var audioSources = new List<IAudioSource>();
                    audioSources.AddRange(VideoHelper.GetUMPAudioSources(original.Video.VideoSources).Where(x => x.IsDownloadable()));
                    if (original.Video is UnMuxedVideoDescriptor unmuxed)
                    {
                        foreach (var source in unmuxed.AudioSources)
                        {
                            if (source is HLSManifestAudioSource hLSManifestAudioSource)
                            {
                                continue;
                            }
                            else if (source.IsDownloadable())
                                audioSources.Add(source);
                        }
                    }

                    var asource = VideoHelper.SelectBestAudioSource(audioSources, new List<string>(), TargetLanguage, TargetBitrate, TargetLanguage != null);
                    if (asource == null && VideoSource == null)
                        throw new DownloadException("Could not find a valid video or audio source for download", false);

                    if (asource == null)
                        AudioSource = null;
                    else if (asource is AudioUrlSource || asource is DashManifestRawAudioSource || asource is UMPAudioFormatSource)
                        AudioSource = asource;
                    else
                        throw new DownloadException("Audio source is not supported for downloading (yet)", false);
                }

                if (SubtitleSource == null && SubtitleName != null)
                {
                    SubtitleSourceLive = original.Subtitles.FirstOrDefault(s => s.Name == SubtitleName);
                    SubtitleSource = (!SubtitleSourceLive.HasFetch) ? new SubtitleSource.Serializable(SubtitleSourceLive) : null;
                }
                if (VideoSource == null && AudioSource == null)
                    throw new DownloadException("No valid sources found for video/audio", false);
            }
        }
        public async Task Download(ManagedHttpClient client, Action<double> onProgress, CancellationToken cancel = default)
        {
            Logger.i(nameof(VideoDownload), $"VideoDownload Download [{Video.Name}]");
            if (VideoDetails == null || (VideoSource == null && AudioSource == null))
                throw new InvalidOperationException("Missing information for download to complete");
            var downloadDir = StateDownloads.GetDownloadsDirectory();

            Error = null;

            if (VideoDetails.ID.Value == null)
                throw new InvalidOperationException("Video has no id");

            if (IsCancelled)
                throw new OperationCanceledException("Download got cancelled");

            string videoDash = (VideoSource is DashManifestRawSource dVideoSource) ? dVideoSource.Generate() : null;
            List<DashRepresentation> videoRepresentations = (!string.IsNullOrEmpty(videoDash)) ? DashHelper.GetRepresentations(videoDash) : null;
            if (VideoSource != null)
            {
                var representation = videoRepresentations?.FirstOrDefault(x => x.MimeType.StartsWith("video/"));
                var mimeType = representation?.MimeType ?? VideoSource.Container;

                VideoFileName = $"{VideoDetails.ID.Value} [{VideoSource.Width}x{VideoSource.Height}].{VideoHelper.VideoContainerToExtension(mimeType)}".SanitizeFileName();
                VideoFilePath = Path.Combine(downloadDir, VideoFileName);

                if(AudioSource == null && VideoSource is DashManifestRawSource)
                {
                    var representationAudio = videoRepresentations?.FirstOrDefault(x => x.MimeType.StartsWith("audio/"));
                    var mimeTypeAudio = representation?.MimeType;
                    if(representationAudio != null && mimeTypeAudio != null)
                    {
                        AudioFileName = $"{VideoDetails.ID.Value} [unknown].{VideoHelper.AudioContainerToExtension(mimeTypeAudio)}".SanitizeFileName();
                        AudioFilePath = Path.Combine(downloadDir, AudioFileName);
                    }
                }
            }
            string audioDash = (AudioSource is DashManifestRawAudioSource dAudioSource) ? dAudioSource.Generate() : null;
            List<DashRepresentation> audioRepresentations = (!string.IsNullOrEmpty(audioDash)) ? DashHelper.GetRepresentations(audioDash) : null;
            if (AudioSource != null)
            {
                var representation = audioRepresentations?.FirstOrDefault();
                var mimeType = representation?.MimeType ?? AudioSource.Container;

                AudioFileName = $"{VideoDetails.ID.Value} [{AudioSource.Language}-{AudioSource.Bitrate}].{VideoHelper.AudioContainerToExtension(mimeType)}".SanitizeFileName();
                AudioFilePath = Path.Combine(downloadDir, AudioFileName);
            }
            if (SubtitleSourcetoUse != null)
            {
                SubtitleFileName = $"{VideoDetails.ID.Value} [{SubtitleSourcetoUse.Name}].{VideoHelper.SubtitleContainerToExtension(SubtitleSourcetoUse.Format)}".SanitizeFileName();
                SubtitleFilePath = Path.Combine(downloadDir, SubtitleFileName);
            }
            var progressLock = new Object();

            long lastVideoLength = 0;
            long lastVideoRead = 0;
            long lastAudioLength = 0;
            long lastAudioRead = 0;

            List<Task> downloadTasks = new List<Task>();

            if (VideoSourceToUse != null)
            {
                Logger.i(nameof(VideoDownload), "Started downloading video");
                downloadTasks.Add(StateApp.ThreadPoolDownload.Run(async () =>
                {
                    var progressCallback = (long length, long totalRead, long speed) =>
                    {
                        lock (progressLock)
                        {
                            lastVideoLength = length;
                            lastVideoRead = totalRead;
                            DownloadSpeedVideo = speed;
                            VideoFileSize = lastVideoLength;

                            var totalLength = lastVideoLength + lastAudioLength;
                            var total = lastVideoRead + lastAudioRead;
                            if (totalLength > 0)
                            {
                                var percentage = (total / (double)totalLength);
                                onProgress?.Invoke(percentage);
                                Progress = percentage;
                                OnProgressChanged?.Invoke(this, percentage);

                                StateDownloads.NotifyDownload(this);
                            }
                        }
                    };

                    if (VideoSourceToUse is UMPVideoFormatSource umpVideo)
                    {
                        var result = await UmpDownloader.DownloadTrackAsync(SabrStreamSpec.FromSource(umpVideo.Parent), SabrSession.ROLE_VIDEO, umpVideo.Format,
                            umpVideo.Parent.Duration, VideoFilePath, GrayjaySettings.Instance.Downloads.GetByteRangeThreadCount(), progressCallback, () => IsCancelled, cancel);
                        VideoFileSize = result.Length;
                        VideoSourceMetaDataOverride = result.ToStreamMetaData();
                        VideoSourceMimeTypeOverride = umpVideo.Format.ContainerMimeType;
                    }
                    else if (VideoSourceToUse is DashManifestRawSource dashManifestRawSource)
                    {
                        var rep = videoRepresentations.FirstOrDefault(x => x.MimeType.StartsWith("video/"));
                        var repAudio = AudioSourceToUse == null ? videoRepresentations.FirstOrDefault(x => x.MimeType.StartsWith("audio/")) : null;

                        (var length, var metaData) = await DownloadDashRawSource("Video", client, dashManifestRawSource, rep, VideoFilePath, progressCallback, default, repAudio, (repAudio != null) ? AudioFilePath : null);
                        VideoFileSize = length;
                        if (metaData != null)
                            VideoSourceMetaDataOverride = metaData;
                        if (rep != null)
                            VideoSourceMimeTypeOverride = rep.MimeType;
                    }
                    else
                    {
                        IRequestModifier modifier = null;
                        if (VideoSourceToUse is JSSource jss)
                            modifier = jss.GetRequestModifier();

                        switch (VideoSource.Container)
                        {
                            case "application/vnd.apple.mpegurl":
                                await DownloadHLSSource("Video", client, modifier, ((VideoUrlSource)VideoSource).Url, VideoFilePath, progressCallback);
                                break;
                            default:
                                if (!(VideoSource is VideoUrlSource))
                                    throw new NotImplementedException("Only support video urls for download");
                                await DownloadSourceFile("Video", client, modifier, ((VideoUrlSource)VideoSource).Url, VideoFilePath, progressCallback);
                                break;
                        }
                    }
                    DownloadSpeedVideo = 0;
                }));
            }
            if (AudioSourceToUse != null)
            {
                Logger.i(nameof(VideoDownload), "Started downloading audio");
                downloadTasks.Add(StateApp.ThreadPoolDownload.Run(async () =>
                {
                    var progressCallback = (long length, long totalRead, long speed) =>
                    {
                        lock (progressLock)
                        {
                            lastAudioLength = length;
                            lastAudioRead = totalRead;
                            DownloadSpeedAudio = speed;
                            AudioFileSize = lastAudioLength;

                            var totalLength = lastAudioLength + lastVideoLength;
                            var total = lastVideoRead + lastAudioRead;
                            if (totalLength > 0)
                            {
                                var percentage = (total / (double)totalLength);
                                onProgress?.Invoke(percentage);
                                Progress = percentage;
                                OnProgressChanged?.Invoke(this, percentage);

                                StateDownloads.NotifyDownload(this);
                            }
                        }
                    };

                    if (AudioSourceToUse is UMPAudioFormatSource umpAudio)
                    {
                        var result = await UmpDownloader.DownloadTrackAsync(SabrStreamSpec.FromSource(umpAudio.Parent), SabrSession.ROLE_AUDIO, umpAudio.Format,
                            umpAudio.Parent.Duration, AudioFilePath, GrayjaySettings.Instance.Downloads.GetByteRangeThreadCount(), progressCallback, () => IsCancelled, cancel);
                        AudioFileSize = result.Length;
                        AudioSourceMetaDataOverride = result.ToStreamMetaData();
                        AudioSourceMimeTypeOverride = umpAudio.Format.ContainerMimeType;
                    }
                    else if (AudioSourceToUse is DashManifestRawAudioSource dashManifestRawSource)
                    {
                        var rep = audioRepresentations.FirstOrDefault();

                        (var length, var metaData) = await DownloadDashRawSource("Audio", client, dashManifestRawSource, rep, AudioFilePath, progressCallback);
                        AudioFileSize = length;
                        if (metaData != null)
                            AudioSourceMetaDataOverride = metaData;
                        if (rep != null)
                            AudioSourceMimeTypeOverride = rep.MimeType;
                    }
                    else
                    {
                        IRequestModifier modifier = null;
                        if (VideoSourceToUse is JSSource jss)
                            modifier = jss.GetRequestModifier();

                        switch (AudioSourceToUse.Container)
                        {
                            case "application/vnd.apple.mpegurl":
                                if (AudioSourceToUse is HLSVariantAudioUrlSource)
                                    await DownloadHLSSource("Audio", client, modifier, ((AudioUrlSource)AudioSourceToUse).Url, AudioFilePath, progressCallback);
                                else
                                    throw new NotImplementedException();
                                break;
                            default:
                                if (!(AudioSourceToUse is AudioUrlSource))
                                    throw new NotImplementedException("Only support audio urls for download");
                                await DownloadSourceFile("Audio", client, modifier, ((AudioUrlSource)AudioSourceToUse).Url, AudioFilePath, progressCallback);
                                break;
                        }
                    }
                    DownloadSpeedAudio = 0;
                }));
            }
            if (SubtitleSourcetoUse != null)
            {
                downloadTasks.Add(StateApp.ThreadPoolDownload.RunAsync(() =>
                {
                    if (SubtitleSourcetoUse is SubtitleRawSource sbrs)
                        File.WriteAllText(SubtitleFilePath, sbrs.GetSubtitles());
                    else
                    {
                        var uri = SubtitleSourcetoUse.GetSubtitlesUri()!;
                        if (uri.Scheme == "file")
                            File.WriteAllText(SubtitleFilePath, File.ReadAllText(uri.AbsolutePath));
                        else
                        {
                            var resp = client.GET(SubtitleSource.Url, new Engine.Models.HttpHeaders());
                            if (!resp.IsOk)
                                throw new Exception("Failed to download subtitles.");
                            File.WriteAllText(SubtitleFilePath, resp.Body.AsString());
                        }
                    }
                }));
            }

            bool wasSuccesful = false;
            try
            {
                foreach (var task in downloadTasks)
                    await task;
                wasSuccesful = true;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                ChangeState(DownloadState.ERROR);
                StateDownloads.NotifyDownload(this);
                throw;
            }
            finally
            {
                if (!wasSuccesful)
                {
                    try
                    {
                        if (VideoFilePath != null)
                        {
                            if (File.Exists(VideoFilePath) && new FileInfo(VideoFilePath).Length > 0)
                            {
                                Logger.i(nameof(VideoDownload), "Deleting remaining video file");
                                File.Delete(VideoFilePath);
                            }
                        }
                        if (AudioFilePath != null)
                        {
                            if (File.Exists(AudioFilePath) && new FileInfo(AudioFilePath).Length > 0)
                            {
                                Logger.i(nameof(VideoDownload), "Deleting remaining audio file");
                                File.Delete(AudioFilePath);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.e(nameof(VideoDownload), $"Failed to delete files after failure:\n{ex.Message}", ex);
                    }
                }
            }

        }
        #region Download Deps

        private async Task DownloadSourceFile(string name, ManagedHttpClient client, IRequestModifier? modifier, string url, string targetFile, Action<long, long, long> onProgress, bool allowByteRangeDownload = true, CancellationToken cancel = default)
        {
            if (File.Exists(targetFile))
                File.Delete(targetFile);

            long sourceLength = 0;
            try
            {
                using (FileStream stream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                {
                    var head = client.TryHead(url);
                    //GrayjaySettings.Instance.Downloads.ByteRangeDownload
                    if (allowByteRangeDownload && true && head?.Headers?.Contains("accept-ranges") == true && head?.Headers?.Contains("content-length") == true)
                    {
                        var concurrency = Math.Min(2, GrayjaySettings.Instance.Downloads.GetByteRangeThreadCount()); //TODO: Temporary limit to 2 to prevent ratelimits
                        Logger.i(nameof(VideoDownload), $"Download {Video.Name} ByteRange Parallel ({concurrency})");
                        sourceLength = long.Parse(head.Headers["content-length"]);
                        onProgress?.Invoke(sourceLength, 0, 0);
                        await DownloadSourceRanges(client, modifier, stream, url, sourceLength, 1024 * 512, concurrency, onProgress);
                    }
                    else
                    {
                        Logger.i(nameof(VideoDownload), $"Download {Video.Name} Sequentially");
                        sourceLength = DownloadSourceSequential(client, modifier, stream, url, onProgress);
                    }
                }
            }
            catch (Exception ex)
            {
                if (File.Exists(targetFile))
                    File.Delete(targetFile);
                throw;
            }

        }

        private long DownloadSourceSequential(ManagedHttpClient client, IRequestModifier? modifier, Stream fileStream, string url, Action<long, long, long> onProgress)
        {
            DateTime lastProgressNotify = DateTime.Now;
            int progressNotifyInterval = 500;

            SpeedMonitor speedMonitor = new SpeedMonitor(TimeSpan.FromSeconds(5));

            long lastSpeed = 0;
            var res = ModifierHttp.GetStream(client, url, modifier);
            if (!res.IsOk)
                throw new InvalidDataException($"Failed to download source. Web[{res.Code}] Error");
            if (res.Stream == Stream.Null)
                throw new InvalidDataException($"Failed to download source. Web[{res.Code}] No response");

            var sourceLength = res.ContentLength;
            using (var sourceStream = res.Stream)
            {
                long totalRead = 0;
                int read = 0;
                byte[] buffer = new byte[4096];

                do
                {
                    read = sourceStream.Read(buffer);
                    if (read <= 0)
                        break;
                    fileStream.Write(buffer, 0, read);


                    totalRead += read;
                    speedMonitor.Activity(read);
                    if (DateTime.Now.Subtract(lastProgressNotify).TotalMilliseconds > progressNotifyInterval)
                    {
                        lastProgressNotify = DateTime.Now;
                        lastSpeed = speedMonitor.GetCurrentSpeed();
                        onProgress?.Invoke(sourceLength, totalRead, lastSpeed);
                    }

                    if (IsCancelled)
                        throw new OperationCanceledException("Cancelled");
                }
                while (read > 0);

                lastSpeed = 0;
                onProgress?.Invoke(sourceLength, totalRead, speedMonitor.GetCurrentSpeed());
            }
            return sourceLength;
        }
        private async Task DownloadSourceRanges(ManagedHttpClient client, IRequestModifier? modifier, Stream fileStream, string url, long sourceLength, int rangeSize, int concurrency, Action<long, long, long> onProgress, CancellationToken cancel = default)
        {
            DateTime lastProgressNotify = DateTime.Now;
            int progressNotifyInterval = 500;
            int progressRate = 4096 * 128;
            int lastProgressCount = 0;
            int speedRate = 4096 * 10;
            long readSinceLastSpeedTest = 0;
            DateTime timeSinceLastSpeedTest = DateTime.Now;

            long lastSpeed = 0;

            int reqCount = -1;
            long totalRead = 0;

            //TODO: Full thread pool
            while (totalRead < sourceLength)
            {
                reqCount++;

                Logger.i(nameof(VideoDownload), $"Download {Video.Name} Batch #{reqCount} [{concurrency}] ({lastSpeed.ToHumanBytesSpeed()})");
                var byteRangeResults = await RequestByteRangeParallel(client, modifier, url, sourceLength, concurrency, totalRead, rangeSize, 1024 * 512, cancel);
                foreach (var byteRange in byteRangeResults)
                {
                    var read = ((byteRange.end - byteRange.start) + 1);

                    fileStream.Write(byteRange.data, 0, (int)read);
                    totalRead += read;
                    readSinceLastSpeedTest += read;
                }

                if (DateTime.Now.Subtract(lastProgressNotify).TotalMilliseconds > progressNotifyInterval)
                {
                    var lastSpeedTime = timeSinceLastSpeedTest;
                    timeSinceLastSpeedTest = DateTime.Now;
                    var timeSince = timeSinceLastSpeedTest - lastSpeedTime;
                    if (timeSince.TotalMilliseconds > 1)
                        lastSpeed = (int)(((double)readSinceLastSpeedTest / timeSince.TotalMilliseconds) * 1000);
                    readSinceLastSpeedTest = 0;

                    lastProgressNotify = DateTime.Now;
                    onProgress?.Invoke(sourceLength, totalRead, lastSpeed);
                    lastProgressCount++;
                }

                if (IsCancelled)
                    throw new OperationCanceledException("Cancelled", null);
            }
            onProgress?.Invoke(sourceLength, totalRead, 0);
        }
        private async Task<(byte[] data, long start, long end)[]> RequestByteRangeParallel(ManagedHttpClient client, IRequestModifier? modifier, string url, long totalLength, int concurrency, long rangePosition, int rangeSize, int rangeVariance, CancellationToken cancel = default)
        {
            List<Task<(byte[] data, long start, long end)>> tasks = new List<Task<(byte[] data, long start, long end)>>();
            var readPosition = rangePosition;
            for (int i = 0; i < concurrency; i++)
            {
                if (readPosition >= totalLength - 1)
                    continue;

                var toRead = rangeSize + ((rangeVariance >= 1) ? _random.Next(rangeVariance * -1, rangeVariance) : 0);
                var rangeStart = readPosition;
                var rangeEnd = (rangeStart + toRead > totalLength) ? totalLength - 1 : readPosition + toRead;

                tasks.Add(StateApp.ThreadPoolDownload.Run(() =>
                {
                    return RequestByteRange(client, modifier, url, rangeStart, rangeEnd);
                }, cancel));
                readPosition = rangeEnd + 1;
            }
            (byte[] data, long start, long end)[] items = new (byte[] data, long start, long end)[tasks.Count];
            for (int i = 0; i < tasks.Count; i++)
                items[i] = await tasks[i];
            return items;
        }
        private (byte[] data, long start, long end) RequestByteRange(ManagedHttpClient client, IRequestModifier? modifier, string url, long rangeStart, long rangeEnd)
        {
            var toRead = rangeEnd - rangeStart;
            var headers = new Engine.Models.HttpHeaders() { { "range", $"bytes={rangeStart}-{rangeEnd}" } };

            BytesResult r = default;
            for (int i = 0; i <= 5; i++)
            {
                r = ModifierHttp.GetBytes(client, url, modifier, headers);

                if (!r.IsOk)
                {
                    if (i < 4)
                    {
                        Logger.w(nameof(VideoDownload), $"Range request failed code [{r.Code}] retrying");
                        switch (i)
                        {
                            case 2: Thread.Sleep(2000 + (int)(Random.Shared.NextDouble() * 300)); break;
                            case 3: Thread.Sleep(3000 + (int)(Random.Shared.NextDouble() * 300)); break;
                            default: Thread.Sleep(1000 + (int)(Random.Shared.NextDouble() * 300)); break;
                        }
                        continue;
                    }
                    throw new InvalidDataException($"Range request failed Code [{r.Code}]");
                }

                break;
            }

            var data = r.Bytes;
            if (data == null || data.Length == 0)
                throw new InvalidDataException("Range request failed, no body");

            if (data.LongLength < toRead)
                throw new InvalidDataException($"Byte-Range request attempted to provide less ({data.LongLength} < {toRead})");

            Thread.Sleep(300);
            return (data, rangeStart, rangeEnd);
        }


        private async Task<(long, StreamMetaData)> DownloadDashRawSource(string name, ManagedHttpClient client, IDashManifestRawSource source, DashRepresentation rep, string targetFile, Action<long, long, long> onProgress, CancellationToken cancel = default, DashRepresentation repAudio = null, string targetFileAudio = null)
        {
            if (File.Exists(targetFile))
                File.Delete(targetFile);
            string dash = null;
            if (rep == null)
            {
                var reps = DashHelper.GetRepresentations(dash);
                dash = source.Generate();
                rep = reps.First(x=>x.MimeType.StartsWith("video/"));

                if(repAudio == null && targetFileAudio != null)
                {
                    repAudio = reps.First(x => x.MimeType.StartsWith("audio/"));
                }
            }

            var executor = source.GetRequestExecutor();
            var modifier = source?.GetRequestModifier();
            StreamMetaData metaData = null;
            StreamMetaData metaDataAudio = null;

            if (source.HasStreamMetadata)
                metaData = new StreamMetaData()
                {
                    FileInitStart = source.InitStart,
                    FileInitEnd = source.InitEnd,
                    FileIndexStart = source.IndexStart,
                    FileIndexEnd = source.IndexEnd
                };
            if (source is DashManifestRawSource sourceDash && sourceDash.HasAudioStreamMetadata)
                metaDataAudio = new StreamMetaData()
                {
                    FileInitStart = sourceDash.AudioInitStart,
                    FileInitEnd = sourceDash.AudioInitEnd,
                    FileIndexStart = sourceDash.AudioIndexStart,
                    FileIndexEnd = sourceDash.AudioIndexEnd
                };

            long sourceLength = 0;
            try
            {
                using (FileStream stream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                using (FileStream streamAudio = ((targetFileAudio != null) ? new FileStream(targetFileAudio, FileMode.Create, FileAccess.Write, FileShare.ReadWrite) : null))
                {
                    Logger.i(nameof(VideoDownload), $"Download {Video.Name} segments (" + rep.Segments.Count.ToString() + ")");
                    long read = 0;
                    long readAudio = 0;
                    var speedmeter = new SpeedMonitor(TimeSpan.FromSeconds(5));
                    int preRead = 0;
                    int preReadAudio = 0;
                    int segmentsTotal = (rep?.Segments?.Count ?? 0) + (repAudio?.Segments?.Count ?? 0) + 1;
                    if (repAudio != null)
                        segmentsTotal++;
                    int segmentsDownloadedCount = 0;
                    var segmentsDownloadedAudio = new HashSet<DashSegment>();
                    long totalEstimateSize = 0;
                    if (rep?.InitializationUrl != null)
                    {
                        int segRead = 0;
                        if (executor != null)
                        {
                            var data = executor.ExecuteRequest(rep.InitializationUrl, new Dictionary<string, string>());
                            stream.Write(data, 0, data.Length);
                            segRead = data.Length;
                        }
                        else
                            segRead = (int)DownloadSourceSequential(client, modifier, stream, rep.InitializationUrl, onProgress);
                        read += segRead;
                        segmentsDownloadedCount++;
                        speedmeter.Activity(read);
                        onProgress?.Invoke(rep.Segments.Count * read, read, speedmeter.GetCurrentSpeed());
                        preRead += segRead;
                    }
                    if(repAudio?.InitializationUrl != null)
                    {
                        int segRead = 0;
                        if (executor != null)
                        {
                            var data = executor.ExecuteRequest(repAudio.InitializationUrl, new Dictionary<string, string>());
                            streamAudio.Write(data, 0, data.Length);
                            segRead = data.Length;
                        }
                        else
                            segRead = (int)DownloadSourceSequential(client, modifier, streamAudio, repAudio.InitializationUrl, onProgress);
                        readAudio += segRead;
                        segmentsDownloadedCount++;
                        speedmeter.Activity(readAudio);
                        onProgress?.Invoke(segmentsTotal, segmentsDownloadedCount, speedmeter.GetCurrentSpeed());
                        preReadAudio += segRead;
                    }
                    for (int i = 0; i < rep.Segments.Count; i++)
                    {
                        bool isLast = i == rep.Segments.Count - 1;
                        var segment = rep.Segments[i];
                        int segRead = 0;
                        if (executor != null)
                        {
                            var data = executor.ExecuteRequest(segment.Url, new Dictionary<string, string>());
                            stream.Write(data, 0, data.Length);
                            segRead = data.Length;
                        }
                        else
                            segRead = (int)DownloadSourceSequential(client, modifier, stream, segment.Url, onProgress);
                        read += segRead;
                        speedmeter.Activity(segRead);

                        var avgSegmentSize = (long)(read / (i + 1));
                        var estimatedSize = (rep.Segments.Count * (long)avgSegmentSize + preRead);
                        segmentsDownloadedCount++;
                        onProgress?.Invoke(totalEstimateSize, read + readAudio, speedmeter.GetCurrentSpeed());

                        var audioToDownload = (repAudio != null) ?
                            (isLast ? 
                                repAudio.Segments.Where(x=>!segmentsDownloadedAudio.Contains(x)).ToList() :
                                repAudio.Segments.Where(x=>x.StartTime < segment.StartTime && !segmentsDownloadedAudio.Contains(x)).ToList()
                            ) : new List<DashSegment>();

                        foreach(var segmentAudio in audioToDownload)
                        {
                            int segReadAudio = 0;
                            if (executor != null)
                            {
                                var data = executor.ExecuteRequest(segmentAudio.Url, new Dictionary<string, string>());
                                streamAudio.Write(data, 0, data.Length);
                                segReadAudio = data.Length;
                            }
                            else
                                segReadAudio = (int)DownloadSourceSequential(client, modifier, stream, segment.Url, onProgress);
                            readAudio += segReadAudio;
                            speedmeter.Activity(segReadAudio);

                            var avgSegmentSizeAudio = (long)(readAudio / (i + 1));
                            var estimatedSizeAudio = (repAudio.Segments.Count * (long)avgSegmentSizeAudio + preReadAudio);
                            segmentsDownloadedCount++;
                            segmentsDownloadedAudio.Add(segmentAudio);
                            totalEstimateSize = estimatedSize + estimatedSizeAudio;
                            onProgress?.Invoke(totalEstimateSize, read + readAudio, speedmeter.GetCurrentSpeed());
                        }
                    }
                    onProgress?.Invoke(read, read, speedmeter.GetCurrentSpeed());


                    if(repAudio != null)
                    {

                        AudioFileSize = readAudio;
                        AudioSourceOverride = new LocalAudioSource()
                        {
                            Name = (VideoSourceToUse?.Name != null) ? VideoSourceToUse.Name + " [audio]" : "audio",
                            FilePath = "",
                            FileSize = readAudio,
                            Container = repAudio.MimeType,
                            Codec = repAudio.Codec,
                            Language = Language.UNKNOWN
                        };
                        AudioSourceMetaDataOverride = metaDataAudio;
                        //if (metaData != null)
                        //    AudioSourceMetaDataOverride = metaData;
                        if (rep != null)
                            AudioSourceMimeTypeOverride = repAudio.MimeType;
                    }

                    return (read, metaData);
                }
            }
            catch (Exception ex)
            {
                if (File.Exists(targetFile))
                    File.Delete(targetFile);
                throw;
            }

        }

        private static void CombineHLSSegments(List<FileInfo> segmentFiles, string targetPath)
        {
            if (segmentFiles == null || segmentFiles.Count == 0)
                throw new ArgumentException("segmentFiles must not be empty", nameof(segmentFiles));

            var concatInput = "concat:" + string.Join("|", segmentFiles.Select(x => x.FullName));

            var args = new string[]
            {
                "-i", concatInput,
                "-c", "copy",
                targetPath
            };

            if (FFMPEG.ExecuteSafe(args, true) != 0)
                throw new InvalidDataException("Transcoding failed");
        }

        private async Task<long> DownloadHLSSource(string name, ManagedHttpClient client, IRequestModifier? modifier, string hlsUrl, string targetFile, Action<long, long, long> onProgress, CancellationToken cancel = default)
        {
            byte[] DownloadBytes(ManagedHttpClient httpClient, string url, long? rangeStart, long? rangeLength)
            {
                var h = new Engine.Models.HttpHeaders();

                if (rangeStart.HasValue)
                {
                    if (rangeLength.HasValue && rangeLength.Value > 0)
                    {
                        long end = rangeStart.Value + rangeLength.Value - 1;
                        h["Range"] = $"bytes={rangeStart.Value}-{end}";
                    }
                    else
                    {
                        h["Range"] = $"bytes={rangeStart.Value}-";
                    }
                }

                var res = ModifierHttp.GetBytes(httpClient, url, modifier, h);
                if (!res.IsOk)
                    throw new InvalidDataException($"Failed to download HLS resource ({url}): HTTP {res.Code}");

                return res.Bytes;
            }

            static byte[] HexToBytes(string hex)
            {
                if (string.IsNullOrWhiteSpace(hex))
                    throw new ArgumentException("Hex string is null or empty.", nameof(hex));

                if ((hex.Length & 1) != 0)
                    throw new ArgumentException("Hex string must have an even length.", nameof(hex));

                var bytes = new byte[hex.Length / 2];
                for (int i = 0; i < bytes.Length; i++)
                    bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

                return bytes;
            }

            static byte[] BuildSequenceIv(long sequenceNumber)
            {
                var iv = new byte[16];
                for (int i = 0; i < 8; i++)
                {
                    iv[15 - i] = (byte)(sequenceNumber & 0xFF);
                    sequenceNumber >>= 8;
                }
                return iv;
            }

            static byte[] DecryptAes128Cbc(byte[] data, byte[] key, byte[] iv)
            {
                using (var aes = Aes.Create())
                {
                    if (aes == null)
                        throw new CryptographicException("Unable to create AES algorithm instance.");

                    aes.KeySize = 128;
                    aes.BlockSize = 128;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = key;
                    aes.IV = iv;

                    using (var decryptor = aes.CreateDecryptor())
                    {
                        return decryptor.TransformFinalBlock(data, 0, data.Length);
                    }
                }
            }

            long downloadedTotalLength = 0;
            var vp = ModifierHttp.GetBytes(client, hlsUrl, modifier);
            if (!vp.IsOk)
                throw new InvalidDataException("Failed to get variant playlist: " + vp.Code);

            string vpContent = Encoding.UTF8.GetString(vp.Bytes);
            var variantPlaylist = HLS.ParseVariantPlaylist(vpContent, vp.FinalUrl);
            var decryption = variantPlaylist.Decryption;
            bool useDecryption = decryption != null && decryption.IsEncrypted;
            byte[]? keyBytes = null;
            byte[]? staticIvBytes = null;

            if (useDecryption)
            {
                if (!string.Equals(decryption!.Method, "AES-128", StringComparison.OrdinalIgnoreCase))
                    throw new NotSupportedException($"HLS decryption method '{decryption.Method}' is not supported.");

                if (string.IsNullOrEmpty(decryption.KeyUrl))
                    throw new InvalidDataException("Encrypted HLS playlist without key URI is not supported.");

                var key = ModifierHttp.GetBytes(client, decryption.KeyUrl, modifier);
                if (!key.IsOk)
                    throw new InvalidDataException("Failed to download AES-128 key: " + key.Code);

                keyBytes = key.Bytes;
                if (!string.IsNullOrEmpty(decryption.IV))
                    staticIvBytes = HexToBytes(decryption.IV);
            }

            long mediaSequence = variantPlaylist.MediaSequence ?? 0;

            SpeedMonitor speedMeter = new SpeedMonitor();
            List<FileInfo> segmentFiles = new List<FileInfo>(variantPlaylist.Segments.Count);
            try
            {
                var rangeOffsets = new Dictionary<string, long>(StringComparer.Ordinal);
                if (!string.IsNullOrEmpty(variantPlaylist.MapUrl))
                {
                    cancel.ThrowIfCancellationRequested();

                    Logger.i(nameof(VideoDownload), "Downloading HLS initialization map");

                    long? mapRangeStart = null;
                    long? mapRangeLength = null;

                    if (variantPlaylist.MapBytesLength > 0)
                    {
                        mapRangeLength = variantPlaylist.MapBytesLength;

                        if (variantPlaylist.MapBytesStart >= 0)
                        {
                            mapRangeStart = variantPlaylist.MapBytesStart;
                            rangeOffsets[variantPlaylist.MapUrl] =
                                variantPlaylist.MapBytesStart + variantPlaylist.MapBytesLength;
                        }
                        else
                        {
                            long offset = 0;
                            rangeOffsets.TryGetValue(variantPlaylist.MapUrl, out offset);
                            mapRangeStart = offset;
                            rangeOffsets[variantPlaylist.MapUrl] = offset + variantPlaylist.MapBytesLength;
                        }
                    }

                    byte[] mapBytes = DownloadBytes(client, variantPlaylist.MapUrl, mapRangeStart, mapRangeLength);

                    if (useDecryption)
                    {
                        if (keyBytes == null)
                            throw new InvalidDataException("Decryption key bytes are missing.");

                        if (staticIvBytes == null)
                            throw new NotSupportedException("Encrypted EXT-X-MAP without explicit IV is not supported.");

                        mapBytes = DecryptAes128Cbc(mapBytes, keyBytes, staticIvBytes);
                    }

                    if (mapBytes.LongLength > int.MaxValue)
                        throw new InvalidDataException("HLS MAP segment too large to handle.");

                    FileInfo segmentFile = StateApp.GetTemporaryFile(null, "segment-");
                    using (FileStream str = new FileStream(segmentFile.FullName, FileMode.Create, FileAccess.Write, FileShare.Delete))
                        await str.WriteAsync(mapBytes, 0, mapBytes.Length);
                    segmentFiles.Add(segmentFile);

                    downloadedTotalLength += mapBytes.Length;
                }

                int totalSegments = variantPlaylist.Segments.Count;
                int mediaSegmentIndex = 0;
                for (int i = 0; i < variantPlaylist.Segments.Count; i++)
                {
                    cancel.ThrowIfCancellationRequested();

                    if (!(variantPlaylist.Segments[i] is HLS.MediaSegment seg))
                        continue;

                    Logger.i(nameof(VideoDownload), $"Download {Video.Name} segment {i} sequential");

                    long? rangeStart = null;
                    long? rangeLength = null;

                    if (seg.BytesLength > 0)
                    {
                        rangeLength = seg.BytesLength;

                        if (seg.BytesStart >= 0)
                        {
                            rangeStart = seg.BytesStart;
                            rangeOffsets[seg.Uri] = seg.BytesStart + seg.BytesLength;
                        }
                        else
                        {
                            long offset = 0;
                            rangeOffsets.TryGetValue(seg.Uri, out offset);
                            rangeStart = offset;
                            rangeOffsets[seg.Uri] = offset + seg.BytesLength;
                        }
                    }

                    byte[] segmentBytes = DownloadBytes(client, seg.Uri, rangeStart, rangeLength);
                    if (useDecryption)
                    {
                        if (keyBytes == null)
                            throw new InvalidDataException("Decryption key bytes are missing.");

                        byte[] ivBytes;
                        if (staticIvBytes != null)
                        {
                            ivBytes = staticIvBytes;
                        }
                        else
                        {
                            long sequenceNumber = mediaSequence + mediaSegmentIndex;
                            ivBytes = BuildSequenceIv(sequenceNumber);
                        }

                        segmentBytes = DecryptAes128Cbc(segmentBytes, keyBytes, ivBytes);
                    }

                    var segmentLength = segmentBytes.LongLength;

                    if (segmentLength > int.MaxValue)
                        throw new InvalidDataException("HLS media segment too large to handle.");

                    var avgLen = (i == 0) ? segmentLength : (i > 0 ? downloadedTotalLength / i : segmentLength);
                    var expectedTotal = avgLen * (totalSegments - 1) + segmentLength;

                    Logger.i(nameof(VideoDownload), $"Download {Video.Name} segment {i} sequential");
                    FileInfo segmentFile = StateApp.GetTemporaryFile(null, "segment-");
                    using (FileStream str = new FileStream(segmentFile.FullName, FileMode.Create, FileAccess.Write, FileShare.Delete))
                        await str.WriteAsync(segmentBytes, 0, (int)segmentLength);
                    segmentFiles.Add(segmentFile);

                    downloadedTotalLength += segmentLength;

                    speedMeter.Activity(downloadedTotalLength);
                    onProgress(expectedTotal, downloadedTotalLength, speedMeter.GetCurrentSpeed());
                    mediaSegmentIndex++;
                }

                Logger.i(nameof(VideoDownload), "Combining segments");
                CombineHLSSegments(segmentFiles, targetFile);
                if (!File.Exists(targetFile))
                    throw new InvalidDataException("No file found after ffmpeg");

                Logger.i(nameof(VideoDownload), $"Finished HLS Source for {Video.Name}");
            }
            finally
            {
                foreach (var seg in segmentFiles)
                    seg.Delete();
            }

            return downloadedTotalLength;
        }
        #endregion

        public void Validate()
        {
            Logger.i(nameof(VideoDownload), $"VideoDownload Validate [{Video.Name}]");
            if (VideoSource != null)
            {
                if (VideoFilePath == null)
                    throw new InvalidDataException("Missing video file name after download");
                var expectedFile = new FileInfo(VideoFilePath);
                if (!expectedFile.Exists)
                    throw new InvalidDataException("Video file missing after download");
                if (VideoSource?.Container != "application/vnd.apple.mpegurl")
                    if (expectedFile.Length != VideoFileSize)
                        throw new InvalidDataException($"Expected size [{VideoFileSize}], but found {expectedFile.Length}");
            }
            if (AudioSource != null)
            {
                if (AudioFilePath == null)
                    throw new InvalidDataException("Missing audio file name after download");
                var expectedFile = new FileInfo(AudioFilePath);
                if (!expectedFile.Exists)
                    throw new InvalidDataException("Audio file missing after download");
                if (AudioSource.Container != "application/vnd.apple.mpegurl")
                    if (expectedFile.Length != AudioFileSize)
                        throw new InvalidDataException($"Expected size [{AudioFileSize}], but found {expectedFile.Length}");
            }
            if (SubtitleSource != null)
            {
                if (SubtitleFilePath == null)
                    throw new InvalidDataException("Missing subtitle file name after download");
                var expectedFile = new FileInfo(SubtitleFilePath);
                if (!expectedFile.Exists)
                    throw new InvalidDataException("Subtitle file missing after download");
            }
        }

        public void Complete()
        {
            var existing = StateDownloads.GetDownloadedVideo(Video.ID);
            var localVideoSource = (VideoFilePath != null ? LocalVideoSource.FromSource(VideoSourceToUse, VideoFilePath, VideoFileSize.Value, VideoSourceMetaDataOverride, VideoSourceMimeTypeOverride) : null);
            var localAudioSource = (AudioFilePath != null ? LocalAudioSource.FromSource(AudioSourceOverride ?? AudioSourceToUse, AudioFilePath, AudioFileSize.Value, AudioSourceMetaDataOverride, AudioSourceMimeTypeOverride) : null);
            var localSubtitleSource = (SubtitleFilePath != null ? LocalSubtitleSource.FromSource(SubtitleSourcetoUse, SubtitleFilePath) : null);

            if (localVideoSource != null && VideoSourceToUse != null && VideoSourceToUse is IStreamMetaDataSource smsv)
                localVideoSource.MetaData = smsv.MetaData;
            if (localAudioSource != null && AudioSourceToUse != null && AudioSourceToUse is IStreamMetaDataSource smsa)
                localAudioSource.MetaData = smsa.MetaData;

            //TODO: Save stream metadata video?
            //TODO: Save stream metadata audio?

            if (existing != null)
            {
                existing.VideoDetails = VideoDetails;
                if (localVideoSource != null)
                {
                    var newVideos = new List<LocalVideoSource>(existing.VideoSources);
                    newVideos.Add(localVideoSource);
                    existing.VideoSources = newVideos;
                }
                if (localAudioSource != null)
                {
                    var newAudios = new List<LocalAudioSource>(existing.AudioSources);
                    newAudios.Add(localAudioSource);
                    existing.AudioSources = newAudios;
                }
                if (localSubtitleSource != null)
                {
                    var newSubtitles = new List<LocalSubtitleSource>(existing.SubtitleSources);
                    newSubtitles.Add(localSubtitleSource);
                    existing.SubtitleSources = newSubtitles;
                }
                StateDownloads.UpdateDownloaded(existing);
            }
            else
            {
                var newVideo = new VideoLocal(VideoDetails);
                if (localVideoSource != null)
                    newVideo.VideoSources.Add(localVideoSource);
                if (localAudioSource != null)
                    newVideo.AudioSources.Add(localAudioSource);
                if (localSubtitleSource != null)
                    newVideo.SubtitleSources.Add(localSubtitleSource);
                newVideo.GroupID = GroupID;
                newVideo.GroupType = GroupType;

                StateDownloads.UpdateDownloaded(newVideo);
            }
        }




        private static Random _random = new Random();
    }



    public enum DownloadState
    {
        QUEUE = 1,
        PREPARING = 2,
        DOWNLOADING = 3,
        VALIDATING = 4,
        FINALIZING = 5,
        COMPLETED = 6,
        ERROR = 7
    }
}
