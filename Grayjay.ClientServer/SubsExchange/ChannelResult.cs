using Grayjay.Engine.Models.Channel;
using Grayjay.Engine.Models.Feed;

namespace Grayjay.ClientServer.SubsExchange
{
    public class ChannelResult
    {
        public DateTime DateTime { get; set; }
        public string ChannelUrl { get; set; }
        public PlatformChannel Channel { get; set; }
        public PlatformContent[] Content { get; set; }
    }
}
