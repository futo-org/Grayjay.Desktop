using Grayjay.ClientServer.Constants;
using Grayjay.ClientServer.Controllers;
using Grayjay.ClientServer.Settings;
using Grayjay.Engine.Models.Detail;
using Grayjay.Engine.Models.Video;
using Grayjay.Engine.Models.Video.Sources;
using System.Collections.Generic;
using System.Linq;

namespace Grayjay.ClientServer.Helpers
{
    public static class VideoHelper
    {

        public static bool IsDownloadable(PlatformVideoDetails details)
        {
            if (details.Video.VideoSources.Any(x => IsDownloadable(x)))
                return true;
            if (details.Video is UnMuxedVideoDescriptor unmuxed)
                return unmuxed.AudioSources.Any(x => IsDownloadable(x));

            return false;
        }

        public static bool IsDownloadable(IVideoSource source) => source is VideoUrlSource videoUrlSource || source is HLSManifestSource || source is DashManifestRawSource || (source is UMPSource ump && !ump.IsLive) || (source is UMPVideoFormatSource umpFormat && !umpFormat.Parent.IsLive);
        public static bool IsDownloadable(IAudioSource source) => source is AudioUrlSource videoUrlSource || source is HLSManifestAudioSource || source is DashManifestRawAudioSource || (source is UMPAudioFormatSource umpFormat && !umpFormat.Parent.IsLive);

        public static List<IVideoSource> ExpandUMPVideoSources(IEnumerable<IVideoSource> sources) =>
            sources.SelectMany(x => x is UMPSource ump ? ump.GetVideoFormatSources().Cast<IVideoSource>() : new[] { x }).ToList();

        public static List<IAudioSource> GetUMPAudioSources(IEnumerable<IVideoSource> sources) =>
            sources.OfType<UMPSource>().SelectMany(x => x.GetAudioFormatSources().Where(a => !a.Format.IsDrc)).Cast<IAudioSource>().ToList();


        public static IVideoSource SelectBestVideoSource(List<IVideoSource> sources, int desiredPixelCount, List<string> prefContainers, string preferredLanguage = null, bool ignoreOriginal = false)
        {
            if(preferredLanguage == null)
                preferredLanguage = GrayjaySettings.Instance.Playback.GetPrimaryLanguage();

            var umpSource = sources.FirstOrDefault(x => x is UMPSource);
            if (umpSource != null)
                return umpSource;

            var targetVideo = (desiredPixelCount > 0) ? sources.OrderBy(x => Math.Abs(x.Height * x.Width - desiredPixelCount)).FirstOrDefault()
                : sources.LastOrDefault();
            var hasPriority = sources.Any(x => x.Priority);

            var targetPixelCount = (targetVideo != null) ? targetVideo.Width * targetVideo.Height : desiredPixelCount;

            //Filter Priority & ordering
            var altSources = (hasPriority) ? sources.Where(x => x.Priority).OrderBy(x => Math.Abs(x.Height * x.Width - desiredPixelCount))
                : sources.Where(x => x.Height == (targetVideo?.Height ?? 0));
            
            //Filter Original
            var hasOriginal = altSources.Any(x => x.Original);
            if (!ignoreOriginal && hasOriginal && GrayjaySettings.Instance.Playback.PreferOriginalAudio)
                altSources = altSources.Where(x => x.Original);

            //Filter Language
            var languageToFilter = (preferredLanguage != null && altSources.Any(x => x.Language == preferredLanguage)) ?
                preferredLanguage :
                (altSources.Any(x => x.Language == Language.ENGLISH) ? Language.ENGLISH : Language.UNKNOWN);
            if (altSources.Any(x => x.Language == preferredLanguage))
                altSources = altSources.Where(x => x.Language == preferredLanguage);
            
            var bestSource = altSources.FirstOrDefault();
            foreach(var prefContainer in prefContainers)
            {
                var betterSource = altSources.FirstOrDefault(x => x.Container == prefContainer);
                if(betterSource != null)
                {
                    bestSource = betterSource;
                    break;
                }
            }
            return bestSource;
        }
        public static int SelectBestVideoSourceIndex(List<IVideoSource> sources, int desiredPixelCount, List<string> prefContainers)
        {
            var bestVideoSource = VideoHelper.SelectBestVideoSource(sources.Cast<IVideoSource>().ToList(), desiredPixelCount, new List<string>() { "video/mp4" });
            return (bestVideoSource != null) ? sources.IndexOf(bestVideoSource) : -1;
        }

        public static List<IVideoSource> ReorderVideoSources(List<IVideoSource> list, bool hasAudio)
        {
            if (hasAudio)
                return list;

            List<IVideoSource> newList = new List<IVideoSource>();
            if (list.Any(x => x.Original))
            {
                newList.AddRange(list.Where(x => x.Original));
                list = list.Where(x => !x.Original).ToList();
            }

            var prefLanguage = GrayjaySettings.Instance.Playback.GetPrimaryLanguage();
            if (prefLanguage != null && list.Any(x => x.Language == prefLanguage))
            {
                newList.AddRange(list.Where(x => x.Language == prefLanguage));
                list = list.Where(x => x.Language != prefLanguage).ToList();
            }

            newList.AddRange(list);
            return newList;
        }


        public static IAudioSource SelectBestAudioSource(List<IAudioSource> sources, List<string> prefContainers, string? prefLanguage = Language.ENGLISH, long? targetBitrate = null, bool ignoreOriginal = false)
        {
            var hasPriority = sources.Any(x => x.Priority);
            if (hasPriority)
                sources = sources.Where(x => x.Priority).ToList();
            var hasOriginal = sources.Any(x => x.Original);
            if (hasOriginal && GrayjaySettings.Instance.Playback.PreferOriginalAudio && !ignoreOriginal)
                sources = sources.Where(x => x.Original).ToList();

            var languageToFilter = (prefLanguage != null && sources.Any(x => x.Language == prefLanguage) 
                ? prefLanguage
                : (sources.Any(x => x.Language == Language.ENGLISH) ? Language.ENGLISH : Language.UNKNOWN));

            var usableSources = (sources.Any(x => x.Language == languageToFilter))
                ? sources.Where(x => x.Language == languageToFilter).OrderBy(x => x.Bitrate).ToList()
                : (sources.OrderBy(x => x.Bitrate).ToList());

            if (usableSources.Any(x => x.Priority))
                usableSources = usableSources.Where(x => x.Priority).ToList();

            var bestSource = (targetBitrate != null)
                ? usableSources.OrderBy(x => Math.Abs(x.Bitrate - (int)targetBitrate)).FirstOrDefault()
                : usableSources.LastOrDefault();

            foreach(var prefContainer in prefContainers)
            {
                var betterSources = usableSources.Where(x => x.Container == prefContainer).ToList();
                var betterSource = (targetBitrate != null)
                    ? betterSources.OrderBy(x => Math.Abs(x.Bitrate - (int)targetBitrate)).FirstOrDefault()
                    : betterSources.LastOrDefault();

                if(betterSource != null)
                {
                    bestSource = betterSource;
                    break;
                }
            }
            return bestSource;
        }
        public static int SelectBestAudioSourceIndex(List<IAudioSource> sources, List<string> prefContainers, string? prefLanguage = null, long? targetBitrate = null)
        {
            var bestAudioSource = VideoHelper.SelectBestAudioSource(sources.Cast<IAudioSource>().ToList(), new List<string>() { "audio/mp4" }, GrayjaySettings.Instance.Playback.GetPrimaryLanguage(), 9999 * 9999);
            return (bestAudioSource != null) ? sources.IndexOf(bestAudioSource) : -1;
        }
        public static List<IAudioSource> ReorderAudioSources(List<IAudioSource> list)
        {
            List<IAudioSource> newList = new List<IAudioSource>();
            if(list.Any(x=>x.Original))
            {
                newList.AddRange(list.Where(x => x.Original));
                list = list.Where(x => !x.Original).ToList();
            }

            var prefLanguage = GrayjaySettings.Instance.Playback.GetPrimaryLanguage();
            if(prefLanguage != null && list.Any(x=>x.Language == prefLanguage))
            {
                newList.AddRange(list.Where(x => x.Language == prefLanguage));
                list = list.Where(x => x.Language != prefLanguage).ToList();
            }

            newList.AddRange(list);
            return newList;
        }


        public static string VideoContainerToExtension(string container)
        {
            container = container.ToLower().Trim();

            if (container.Contains("video/mp4") || container == "application/vnd.apple.mpegurl")
                return "mp4";
            else if (container.Contains("application/x-mpegURL"))
                return "m3u8";
            else if (container.Contains("video/3gpp"))
                return "3gp";
            else if (container.Contains("video/quicktime"))
                return "mov";
            else if (container.Contains("video/webm"))
                return "webm";
            else if (container.Contains("video/x-matroska"))
                return "mkv";
            else
                //throw new InvalidDataException("Could not determine container type for video (" + container + ")");
                return "video";
        }
        public static string AudioContainerToExtension(string container)
        {
            if (container.Contains("audio/mp4"))
                return "mp4a";
            else if (container.Contains("audio/mpeg"))
                return "mpga";
            else if (container.Contains("audio/mp3"))
                return "mp3";
            else if (container.Contains("audio/webm"))
                return "webma";
            else if (container == "application/vnd.apple.mpegurl")
                return "mp4";
            else
                //throw new InvalidDataException("Could not determine container type for audio (" + container + ")");
            return "audio";
        }
        public static string SubtitleContainerToExtension(string container)
        {
            if (container == null)
                return "subtitle";

            if (container.Contains("text/vtt"))
                return "vtt";
            else if (container.Contains("text/plain"))
                return "srt";
            else if (container.Contains("application/x-subrip"))
                return "srt";
            else
                return "subtitle";
        }
    }
}
