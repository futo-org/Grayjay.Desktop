namespace Grayjay.ClientServer.SubsExchange
{
    public class ChannelRequest
    {
        public string ChannelUrl { get; set; }


        public ChannelRequest(string channelUrl)
        {
            ChannelUrl = channelUrl;
        }
        public ChannelRequest() { }
    }
}
