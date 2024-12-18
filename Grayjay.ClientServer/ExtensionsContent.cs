using Grayjay.ClientServer.Helpers;
using Grayjay.Engine.Models.Detail;
using Grayjay.Engine.Models.Video.Sources;

namespace Grayjay.ClientServer
{
    public static class ExtensionsContent
    {

        public static bool IsDownloadable(this PlatformVideoDetails details)
            => VideoHelper.IsDownloadable(details);
        public static bool IsDownloadable(this IVideoSource videoSource)
            => VideoHelper.IsDownloadable(videoSource);
        public static bool IsDownloadable(this IAudioSource audioSource)
            => VideoHelper.IsDownloadable(audioSource);
    }
}
