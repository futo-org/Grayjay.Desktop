using Grayjay.ClientServer.States;
using Grayjay.Engine;
using Grayjay.Engine.Pagers;

namespace Grayjay.ClientServer.Subscriptions.Algorithms
{
    public class CachedSubscriptionAlgorithm : SubscriptionFetchAlgorithm
    {
        private int _pageSize;

        public CachedSubscriptionAlgorithm(int pageSize)
        {
            _pageSize = pageSize;
        }


        public override Dictionary<GrayjayPlugin, int> CountRequests(Dictionary<Subscription, List<string>> subs)
        {
            return new Dictionary<GrayjayPlugin, int>();
        }

        public override Result GetSubscriptions(Dictionary<Subscription, List<string>> subs)
        {
            return new Result(new DedupContentPager(StateCache.GetChannelCachePager(subs.SelectMany(x => x.Value).Distinct(), _pageSize)), new List<Exception>());
        }
    }
}
