using Grayjay.Engine.Models.Feed;

namespace Grayjay.ClientServer.Models;

public class Playlist
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public List<PlatformVideo> Videos { get; set; } = new List<PlatformVideo>();
    public DateTime DateCreation { get; set; }
    public DateTime DateUpdate { get; set; }
    public DateTime DatePlayed { get; set; }


    public static Playlist FromPlaylistDetails(PlatformPlaylistDetails details)
    {
        List<PlatformVideo> videos = new List<PlatformVideo>();
        videos = details.Contents.GetResults().Where(x => x is PlatformVideo).Select(x => (PlatformVideo)x).ToList();
        while (details.Contents.HasMorePages())
        {
            details.Contents.NextPage();
            List<PlatformVideo> newResults = details.Contents.GetResults().Where(x => x is PlatformVideo).Select(x=>(PlatformVideo)x).ToList();
            if (newResults.Count == 0)
                break;
            videos.AddRange(newResults);
        }

        return new Playlist()
        {
            Id = Guid.NewGuid().ToString(),
            Name = details.Name,
            Videos = videos,
            DateCreation = details.DateTime,
            DatePlayed = DateTime.MinValue,
            DateUpdate = DateTime.Now
        };
    }
}