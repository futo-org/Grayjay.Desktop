using Grayjay.Engine.Models.Channel;
using Grayjay.Engine.Models.Feed;

namespace Grayjay.ClientServer.SubsExchange
{
    public class ChannelResolve
    {
        public string ChannelUrl { get; set; }
        public PlatformChannel Channel { get; set; }
        public PlatformContent[] Content { get; set; }

        public ChannelResolve() { }
        public ChannelResolve(string channelUrl, PlatformContent[] content, PlatformChannel channel = null)
        {
            ChannelUrl = channelUrl;
            Content = content;
            Channel = channel;
        }
    }
}
