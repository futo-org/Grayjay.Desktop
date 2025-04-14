using System.Diagnostics.Contracts;

namespace Grayjay.ClientServer.SubsExchange
{
    public class ExchangeContract
    {
        public string ID { get; set; }
        public ChannelRequest[] Requests { get; set; }
        
        public string[] Provided { get; set; }
        public string[] Required { get; set; }

        public DateTime Expire { get; set; }

        public int ContractVersion { get; set; } = 1;
    }
}
