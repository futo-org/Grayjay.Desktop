using Grayjay.ClientServer.States;

namespace Grayjay.ClientServer.Models.Downloads
{
    public class PlaylistDownload
    {
        public string PlaylistID { get; set; }
        public int TargetPixelCount { get; set; }
        public int TargetBitrate { get; set; }

        public PlaylistDownload() { }
        public PlaylistDownload(string playlistId, int? targetPixelCount, int? targetBitrate)
        {
            PlaylistID = playlistId;
            TargetPixelCount = targetPixelCount ?? -1;
            TargetBitrate = targetBitrate ?? -1;
        }

        public WithPlaylistModel WithPlaylist()
        {
            return new WithPlaylistModel(this);
        }

        public class WithPlaylistModel
        {
            public string PlaylistID { get; set; }
            public int TargetPixelCount { get; set; }
            public int TargetBitrate { get; set; }
            public Playlist Playlist { get; set; }

            public WithPlaylistModel(PlaylistDownload download)
            {
                PlaylistID = download.PlaylistID;
                TargetPixelCount = download.TargetPixelCount;
                TargetBitrate = download.TargetBitrate;
                Playlist = StatePlaylists.Get(download.PlaylistID);
            }

        }
    }
}
