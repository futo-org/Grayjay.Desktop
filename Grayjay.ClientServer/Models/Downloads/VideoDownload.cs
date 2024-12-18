using Grayjay.ClientServer.Exceptions;
using Grayjay.ClientServer.Helpers;
using Grayjay.ClientServer.Parsers;
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
using Grayjay.Engine.Models.Video.Sources;
using Grayjay.Engine.Web;
using Microsoft.ClearScript;
using SQLitePCL;
using System;
using System.Diagnostics.Eventing.Reader;
using System.Net;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
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
        public VideoDownload(PlatformVideo video, long? targetPixelCount = null, long? targetBitRate = null, string groupType = null, string groupId = null)
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
            if(subtitleSource?.HasFetch ?? true)
                SubtitleSource = null;
            else
                SubtitleSource = new SubtitleSource.Serializable(subtitleSource);
            SubtitleSourceLive = subtitleSource;
            TargetPixelCount = (videoSource != null) ? videoSource.Width * videoSource.Height : null;
            TargetBitrate = audioSource?.Bitrate;
            SubtitleName = subtitleSource?.Name;

            VideoSourceRequiresLive = (videoSource is DashManifestRawSource dashManifestRawSource && dashManifestRawSource.HasGenerate) ||
                (videoSource is JSSource videoJSSource && videoJSSource.HasRequestExecutor);
            AudioSourceRequiresLive = (audioSource is DashManifestRawAudioSource dashManifestRawAudioSource && dashManifestRawAudioSource.HasGenerate) ||
                (audioSource is JSSource audioJSSource && audioJSSource.HasRequestExecutor);
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
            if(VideoSourceRequiresLive)
            {
                VideoDetails = null;
                VideoSource = null;
                VideoSourceLive = null;
            }
            if(AudioSourceRequiresLive)
            {
                VideoDetails = null;
                AudioSource = null;
                AudioSourceLive = null;
            }
            if(SubtitleSourceRequiresLive)
            {
                SubtitleSource = null;
                SubtitleSourceLive = null;
            }
            
            //The following exceptions seem contradictory for certain cases.
            //if (Video == null && VideoDetails == null) //Include query options?
            //    throw new InvalidDataException("Missing information for download to complete");
            //if (TargetPixelCount == null && TargetBitrate == null && VideoSourceToUse == null && AudioSourceToUse == null && VideoFileName == null && AudioFileName == null)
            //    throw new InvalidOperationException("No sources or query values set");

            if(Video != null && (VideoDetails == null || VideoSourceRequiresLive || AudioSourceRequiresLive || SubtitleSourceRequiresLive))
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
                if(VideoSource == null && TargetPixelCount != null)
                {
                    var videoSources = new List<IVideoSource>();
                    foreach(var source in original.Video.VideoSources)
                    {
                        if (source is HLSManifestSource hlsManifestSource)
                        {
                            try
                            {
                                var manifest = client.GET(hlsManifestSource.Url, new Dictionary<string, string>());
                                if(manifest.IsOk)
                                {
                                    var sources = HLS.ParseToVideoSources(source, manifest.Body.AsString(), hlsManifestSource.Url);
                                    videoSources.AddRange(sources);
                                }
                            }
                            catch(Exception ex)
                            {
                                Logger.i(nameof(VideoDownload), $"Failed to fetch hls manifest: {ex.Message}");
                            }
                        }
                        else if(source.IsDownloadable())
                            videoSources.Add(source);
                    }

                    var vsource = VideoHelper.SelectBestVideoSource(videoSources, (int)TargetPixelCount, new List<string>());
                    if(vsource != null)
                    {
                        if (vsource is VideoUrlSource || vsource is DashManifestRawSource)
                            VideoSource = vsource;
                        else
                            throw new DownloadException("Video source is not supported for downloading (yet)", false);
                    }
                }

                if(AudioSource == null && TargetBitrate != null)
                {
                    var audioSources = new List<IAudioSource>();
                    if(original.Video is UnMuxedVideoDescriptor unmuxed)
                    {
                        foreach(var source in unmuxed.AudioSources)
                        {
                            if (source is HLSManifestAudioSource hLSManifestAudioSource)
                            {
                                continue;
                            }
                            else if(source.IsDownloadable())
                                audioSources.Add(source);
                        }
                    }

                    var asource = VideoHelper.SelectBestAudioSource(audioSources, new List<string>(), null, TargetBitrate);
                    if (asource == null && VideoSource == null)
                        throw new DownloadException("Could not find a valid video or audio source for download", false);

                    if (asource == null)
                        AudioSource = null;
                    else if (asource is AudioUrlSource || asource is DashManifestRawAudioSource)
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
            if(VideoSource != null)
            {
                var representation = videoRepresentations?.FirstOrDefault();
                var mimeType = representation?.MimeType ?? VideoSource.Container;

                VideoFileName = $"{VideoDetails.ID.Value} [{VideoSource.Width}x{VideoSource.Height}].{VideoHelper.VideoContainerToExtension(mimeType)}".SanitizeFileName();
                VideoFilePath = Path.Combine(downloadDir, VideoFileName);
            }
            string audioDash = (AudioSource is DashManifestRawAudioSource dAudioSource) ? dAudioSource.Generate() : null;
            List<DashRepresentation> audioRepresentations = (!string.IsNullOrEmpty(audioDash)) ? DashHelper.GetRepresentations(audioDash) : null;
            if (AudioSource != null)
            {
                var representation = audioRepresentations?.FirstOrDefault();
                var mimeType = representation?.MimeType ?? AudioSource.Container;

                AudioFileName = $"{VideoDetails.ID.Value} [{AudioSource.Language}-{AudioSource.Bitrate}].{VideoHelper.AudioContainerToExtension(mimeType)}".SanitizeFileName();
                AudioFilePath = Path.Combine(downloadDir , AudioFileName);
            }
            if(SubtitleSourcetoUse != null)
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

            if(VideoSourceToUse != null)
            {
                Logger.i(nameof(VideoDownload), "Started downloading video");
                downloadTasks.Add(Task.Run(async () =>
                {
                    var progressCallback = (long length, long totalRead, long speed) =>
                    {
                        lock(progressLock)
                        {
                            lastVideoLength = length;
                            lastVideoRead = totalRead;
                            DownloadSpeedVideo = speed;
                            if (VideoFileSize == null)
                                VideoFileSize = lastVideoLength;

                            var totalLength = lastVideoLength + lastAudioLength;
                            var total = lastVideoRead + lastAudioRead;
                            if(totalLength > 0)
                            {
                                var percentage = (total / (double)totalLength);
                                onProgress?.Invoke(percentage);
                                Progress = percentage;
                                OnProgressChanged?.Invoke(this, percentage);

                                StateDownloads.NotifyDownload(this);
                            }
                        }
                    };

                    if (VideoSourceToUse is DashManifestRawSource dashManifestRawSource)
                    {
                        var rep = videoRepresentations.FirstOrDefault();

                        (var length, var metaData) = await DownloadDashRawSource("Video", client, dashManifestRawSource, rep, VideoFilePath, progressCallback);
                        VideoFileSize = length;
                        if (metaData != null)
                            VideoSourceMetaDataOverride = metaData;
                        if (rep != null)
                            VideoSourceMimeTypeOverride = rep.MimeType;
                    }
                    else
                    {
                        switch (VideoSource.Container)
                        {
                            case "application/vnd.apple.mpegurl":
                                DownloadHLSSource("Video", client, ((VideoUrlSource)VideoSource).Url, VideoFilePath, progressCallback);
                                break;
                            default:
                                if (!(VideoSource is VideoUrlSource))
                                    throw new NotImplementedException("Only support video urls for download");
                                await DownloadSourceFile("Video", client, ((VideoUrlSource)VideoSource).Url, VideoFilePath, progressCallback);
                                break;
                        }
                    }
                }, cancel));
            }
            if(AudioSourceToUse != null)
            {
                Logger.i(nameof(VideoDownload), "Started downloading audio");
                downloadTasks.Add(Task.Run(async () =>
                {
                    var progressCallback = (long length, long totalRead, long speed) =>
                    {
                        lock (progressLock)
                        {
                            lastAudioLength = length;
                            lastAudioRead = totalRead;
                            DownloadSpeedAudio = speed;
                            if (AudioFileSize == null)
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

                    if (AudioSourceToUse is DashManifestRawAudioSource dashManifestRawSource)
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
                        switch (AudioSourceToUse.Container)
                        {
                            case "application/vnd.apple.mpegurl":
                                if (VideoSource is HLSVariantAudioUrlSource)
                                    DownloadHLSSource("Audio", client, ((AudioUrlSource)VideoSource).Url, AudioFilePath, progressCallback);
                                else
                                    throw new NotImplementedException();
                                break;
                            default:
                                if (!(AudioSource is AudioUrlSource))
                                    throw new NotImplementedException("Only support audio urls for download");
                                await DownloadSourceFile("Audio", client, ((AudioUrlSource)AudioSource).Url, AudioFilePath, progressCallback);
                                break;
                        }
                    }
                }));
            }
            if(SubtitleSourcetoUse != null)
            {
                downloadTasks.Add(Task.Run(() =>
                {
                    var uri = SubtitleSourcetoUse.GetSubtitlesUri()!;
                    if (uri.Scheme == "file")
                        File.WriteAllText(SubtitleFilePath, File.ReadAllText(uri.AbsolutePath));
                    else
                    {
                        var resp = client.GET(SubtitleSource.Url, new Dictionary<string, string>());
                        if (!resp.IsOk)
                            throw new Exception("Failed to download subtitles.");
                        File.WriteAllText(SubtitleFilePath, resp.Body.AsString());
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
            catch(Exception ex)
            {
                Error = ex.Message;
                ChangeState(DownloadState.ERROR);
                StateDownloads.NotifyDownload(this);
                throw;
            }
            finally
            {
                if(!wasSuccesful)
                {
                    try
                    {
                        if(VideoFilePath != null)
                        {
                            if (File.Exists(VideoFilePath) && new FileInfo(VideoFilePath).Length > 0)
                            {
                                Logger.i(nameof(VideoDownload), "Deleting remaining video file");
                                File.Delete(VideoFilePath);
                            }
                        }
                        if(AudioFilePath != null)
                        {
                            if (File.Exists(AudioFilePath) && new FileInfo(AudioFilePath).Length > 0)
                            {
                                Logger.i(nameof(VideoDownload), "Deleting remaining audio file");
                                File.Delete(AudioFilePath);
                            }
                        }
                    }
                    catch(Exception ex)
                    {
                        Logger.e(nameof(VideoDownload), $"Failed to delete files after failure:\n{ex.Message}", ex);
                    }
                }
            }

        }
        #region Download Deps

        private async Task DownloadSourceFile(string name, ManagedHttpClient client, string url, string targetFile, Action<long, long, long> onProgress, bool allowByteRangeDownload = true, CancellationToken cancel = default)
        {
            if (File.Exists(targetFile))
                File.Delete(targetFile);

            long sourceLength = 0;
            try
            {
                using (FileStream stream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                {
                    var head = client.TryHead(url);
                    if (allowByteRangeDownload && GrayjaySettings.Instance.Downloads.ByteRangeDownload && head?.Headers?.ContainsKey("accept-ranges") == true && head?.Headers?.ContainsKey("content-length") == true)
                    {
                        var concurrency = GrayjaySettings.Instance.Downloads.GetByteRangeThreadCount();
                        Logger.i(nameof(VideoDownload), $"Download {Video.Name} ByteRange Parallel ({concurrency})");
                        sourceLength = long.Parse(head.Headers["content-length"]);
                        onProgress?.Invoke(sourceLength, 0, 0);
                        await DownloadSourceRanges(client, stream, url, sourceLength, 1024 * 512, concurrency, onProgress);
                    }
                    else
                    {
                        Logger.i(nameof(VideoDownload), $"Download {Video.Name} Sequentially");
                        sourceLength = DownloadSourceSequential(client, stream, url, onProgress);
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
        private long DownloadSourceSequential(ManagedHttpClient client, Stream fileStream, string url, Action<long, long, long> onProgress)
        {
            DateTime lastProgressNotify = DateTime.Now;
            int progressNotifyInterval = 500;

            int progressRate = 4096 * 128;
            int lastProgressCount = 0;
            int speedRate = 4096 * 25;
            long readSinceLastSpeedTest = 0;
            DateTime timeSinceLastSpeedTest = DateTime.Now;

            long lastSpeed = 0;

            var result = client.GET(url, new Dictionary<string, string>());
            if (!result.IsOk)
                throw new InvalidDataException($"Failed to download source. Web[{result.Code}] Error");
            if (result.Body == null)
                throw new InvalidDataException($"Failed to download source. Web[{result.Code}] No response");

            var sourceLength = result.ContentLength;
            using (var sourceStream = result.Body.AsStream())
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
                    readSinceLastSpeedTest += read;
                    if (DateTime.Now.Subtract(lastProgressNotify).TotalMilliseconds > progressNotifyInterval)
                    {
                        lastProgressNotify = DateTime.Now;
                        onProgress?.Invoke(sourceLength, totalRead, lastSpeed);
                        lastProgressCount++;
                    }
                    if(readSinceLastSpeedTest > speedRate)
                    {
                        var lastSpeedTime = timeSinceLastSpeedTest;
                        timeSinceLastSpeedTest = DateTime.Now;
                        var timeSince = timeSinceLastSpeedTest - lastSpeedTime;
                        if (timeSince.TotalSeconds > 1)
                            lastSpeed = (long)(readSinceLastSpeedTest / (timeSince.TotalSeconds));
                        readSinceLastSpeedTest = 0;
                    }

                    if (IsCancelled)
                        throw new OperationCanceledException("Cancelled");
                }
                while (read > 0);

                lastSpeed = 0;
                onProgress?.Invoke(sourceLength, totalRead, (long)(readSinceLastSpeedTest / ((DateTime.Now - timeSinceLastSpeedTest).TotalSeconds)));
            }
            return sourceLength;
        }
        private async Task DownloadSourceRanges(ManagedHttpClient client, Stream fileStream, string url, long sourceLength, int rangeSize, int concurrency, Action<long, long, long> onProgress, CancellationToken cancel = default)
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
            while(totalRead < sourceLength)
            {
                reqCount++;

                Logger.i(nameof(VideoDownload), $"Download {Video.Name} Batch #{reqCount} [{concurrency}] ({lastSpeed.ToHumanBytesSpeed()})");
                var byteRangeResults = await RequestByteRangeParallel(client, url, sourceLength, concurrency, totalRead, rangeSize, 1024 * 4, cancel);
                foreach(var byteRange in byteRangeResults)
                {
                    var read = ((byteRange.end - byteRange.start) + 1);

                    fileStream.Write(byteRange.data, 0, (int)read);
                    totalRead += read;
                    readSinceLastSpeedTest += read;
                }

                if(DateTime.Now.Subtract(lastProgressNotify).TotalMilliseconds > progressNotifyInterval)
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
        private async Task<(byte[] data, long start, long end)[]> RequestByteRangeParallel(ManagedHttpClient client, string url, long totalLength, int concurrency, long rangePosition, int rangeSize, int rangeVariance, CancellationToken cancel = default)
        {
            List<Task<(byte[] data, long start, long end)>> tasks = new List<Task<(byte[] data, long start, long end)>>();
            var readPosition = rangePosition;
            for(int i = 0; i < concurrency; i++)
            {
                if (readPosition >= totalLength - 1)
                    continue;

                var toRead = rangeSize + ((rangeVariance >= 1) ? _random.Next(rangeVariance * -1, rangeVariance) : 0);
                var rangeStart = readPosition;
                var rangeEnd = (rangeStart + toRead > totalLength) ? totalLength - 1 : readPosition + toRead;

                tasks.Add(Task.Run(() =>
                {
                    return RequestByteRange(client, url, rangeStart, rangeEnd);
                }, cancel));
                readPosition = rangeEnd + 1;
            }
            (byte[] data, long start, long end)[] items = new (byte[] data, long start, long end)[tasks.Count];
            for (int i = 0; i < tasks.Count; i++)
                items[i] = await tasks[i];
            return items;
        }
        private (byte[] data, long start, long end) RequestByteRange(ManagedHttpClient client, string url, long rangeStart, long rangeEnd)
        {
            var toRead = rangeEnd - rangeStart;
            var req = client.GET(url, new Dictionary<string, string>() { { "range", $"bytes={rangeStart}-{rangeEnd}" } });
            if (!req.IsOk)
                throw new InvalidDataException($"Range request failed Code [{req.Code}] due to: {req.Body.ToString()}");
            if (req.Body == null)
                throw new InvalidDataException($"Range request failed, no body");
            var read = req.ContentLength;
            if (read < toRead)
                throw new InvalidDataException($"Byte-Range request attempted to provide less ({read} < {toRead})");

            return (req.Body.AsBytes(), rangeStart, rangeEnd);
        }


        private async Task<(long, StreamMetaData)> DownloadDashRawSource(string name, ManagedHttpClient client, IDashManifestRawSource source, DashRepresentation rep, string targetFile, Action<long, long, long> onProgress, CancellationToken cancel = default)
        {
            if (File.Exists(targetFile))
                File.Delete(targetFile);

            if (rep == null)
            {
                var dash = source.Generate();
                rep = DashHelper.GetRepresentations(dash).First();
            }

            var executor = source.GetRequestExecutor();
            StreamMetaData metaData = null;

            if (source.HasStreamMetadata)
                metaData = new StreamMetaData()
                {
                    FileInitStart = source.InitStart,
                    FileInitEnd = source.InitEnd,
                    FileIndexStart = source.IndexStart,
                    FileIndexEnd = source.IndexEnd
                };

            long sourceLength = 0;
            try
            {
                using (FileStream stream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                {
                    Logger.i(nameof(VideoDownload), $"Download {Video.Name} segments (" + rep.Segments.Count.ToString() + ")");
                    int read = 0;
                    var speedmeter = new SpeedMonitor(TimeSpan.FromSeconds(5));
                    int preRead = 0;
                    if(rep?.InitializationUrl != null)
                    {
                        int segRead = 0;
                        if (executor != null)
                        {
                            var data = executor.ExecuteRequest(rep.InitializationUrl, new Dictionary<string, string>());
                            stream.Write(data, 0, data.Length);
                            segRead = data.Length;
                        }
                        else
                            segRead = (int)DownloadSourceSequential(client, stream, rep.InitializationUrl, onProgress);
                        read += segRead;
                        speedmeter.Activity(read);
                        onProgress?.Invoke(rep.Segments.Count * (read), read, speedmeter.GetCurrentSpeed());
                        preRead += segRead;
                    }
                    for(int i = 0; i < rep.Segments.Count; i++)
                    {
                        var segment = rep.Segments[i];
                        int segRead = 0;
                        if (executor != null)
                        {
                            var data = executor.ExecuteRequest(segment.Url, new Dictionary<string, string>());
                            stream.Write(data, 0, data.Length);
                            segRead = data.Length;
                        }
                        else
                            segRead = (int)DownloadSourceSequential(client, stream, segment.Url, onProgress);
                        read += segRead;
                        speedmeter.Activity(read);
                        onProgress?.Invoke(rep.Segments.Count * (read / (i + 1)) + preRead, read, speedmeter.GetCurrentSpeed());
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

        private long DownloadHLSSource(string name, ManagedHttpClient client, string hlsUrl, string targetFile, Action<long, long, long> onProgress, CancellationToken cancel = default)
        {
            long downloadedTotalLength = 0;

            List<FileInfo> segmentFiles = new List<FileInfo>();
            try
            {
                var response = client.GET(hlsUrl, new Dictionary<string, string>());
                if (!response.IsOk)
                    throw new InvalidDataException("Failed to get variant playlist: " + response.Code.ToString());

                string vpContent = response.Body?.AsString() ?? throw new InvalidDataException("Variant playlist content is empty");

                var variantPlaylist = HLS.ParseVariantPlaylist(vpContent, hlsUrl);
                SpeedMonitor speedMeter = new SpeedMonitor();
                for(int i = 0; i < variantPlaylist.Segments.Count; i++)
                {
                    var segment = variantPlaylist.Segments[i];
                    if(!(segment is HLS.MediaSegment))
                        continue;
                    var mediaSegment = segment as HLS.MediaSegment;

                    Logger.i(nameof(VideoDownload), $"Download {Video.Name} segment {i} sequential");
                    FileInfo segmentFile = StateApp.GetTemporaryFile(null, "segment-");
                    using (FileStream str = new FileStream(segmentFile.FullName, FileMode.Create, FileAccess.Write, FileShare.Delete))
                    {
                        var segmentLength = DownloadSourceSequential(client, str, mediaSegment.Uri, (long segmentLength, long totalRead, long lastSpeed) =>
                        {
                            var averageSegmentLength = (i == 0) ? segmentLength : downloadedTotalLength / i;
                            var expectedTotalLength = averageSegmentLength * (variantPlaylist.Segments.Count - 1) + segmentLength;
                            speedMeter.Activity(totalRead);
                            onProgress(expectedTotalLength, downloadedTotalLength + totalRead, speedMeter.GetCurrentSpeed());
                        });
                        downloadedTotalLength += segmentLength;
                    }
                    segmentFiles.Add(segmentFile);
                }
                Logger.i(nameof(VideoDownload), "Combining segments");
                CombineHLSSegments(segmentFiles, targetFile);
                if (!File.Exists(targetFile))
                    throw new InvalidDataException("No file found after ffmpeg");
                Logger.i(nameof(VideoDownload), $"Finished HLS Source for {Video.Name}");
            }
            catch(Exception ex)
            {
                //TODO: Filter errors like not enough space
                throw;
            }
            finally
            {
                foreach (var seg in segmentFiles)
                    seg.Delete();
            }
            return downloadedTotalLength;
        }
        private static void CombineHLSSegments(List<FileInfo> segmentFiles, string targetPath)
        {
            var fileList = StateApp.GetTemporaryFile(".txt", "filelist-");
            File.WriteAllText(fileList.FullName, string.Join("\n", segmentFiles.Select(x => $"file '{x.FullName}'")));

            var cmd = $"-f concat -safe 0 -i \"{fileList.FullName}\" -c copy \"{targetPath}\"";
            if (FFMPEG.Execute(cmd, true) == 0)
            {

            }
            else
                throw new InvalidDataException("Transcoding failed");
        }
        #endregion

        public void Validate()
        {
            Logger.i(nameof(VideoDownload), $"VideoDownload Validate [{Video.Name}]");
            if(VideoSource != null)
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
            if(AudioSource != null)
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
            if(SubtitleSource != null)
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
            var localAudioSource = (AudioFilePath != null ? LocalAudioSource.FromSource(AudioSourceToUse, AudioFilePath, AudioFileSize.Value, AudioSourceMetaDataOverride, AudioSourceMimeTypeOverride) : null);
            var localSubtitleSource = (SubtitleFilePath != null ? LocalSubtitleSource.FromSource(SubtitleSourcetoUse, SubtitleFilePath) : null);

            if (localVideoSource != null && VideoSourceToUse != null && VideoSourceToUse is IStreamMetaDataSource smsv)
                localVideoSource.MetaData = smsv.MetaData;
            if (localAudioSource != null && AudioSourceToUse != null && AudioSourceToUse is IStreamMetaDataSource smsa)
                localAudioSource.MetaData = smsa.MetaData;

            //TODO: Save stream metadata video?
            //TODO: Save stream metadata audio?

            if(existing != null)
            {
                existing.VideoDetails = VideoDetails;
                if(localVideoSource != null)
                {
                    var newVideos = new List<LocalVideoSource>(existing.VideoSources);
                    newVideos.Add(localVideoSource);
                    existing.VideoSources = newVideos;
                }
                if(localAudioSource != null)
                {
                    var newAudios = new List<LocalAudioSource>(existing.AudioSources);
                    newAudios.Add(localAudioSource);
                    existing.AudioSources = newAudios;
                }
                if(localSubtitleSource != null)
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
                if(localVideoSource != null)
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
