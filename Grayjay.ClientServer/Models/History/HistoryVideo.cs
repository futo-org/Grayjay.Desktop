using Grayjay.Engine.Models.Feed;

namespace Grayjay.ClientServer.Models.History
{
    public class HistoryVideo
    {
        public PlatformVideo Video { get; set; }
        public long Position { get; set; }
        public DateTime Date { get; set; }


        public static HistoryVideo FromVideo(PlatformVideo video, long position, DateTime watchDate)
        {
            return new HistoryVideo()
            {
                Video = video,
                Position = position,
                Date = watchDate
            };
        }
    }
}
