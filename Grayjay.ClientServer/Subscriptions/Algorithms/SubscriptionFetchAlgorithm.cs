using Grayjay.ClientServer.Threading;
using Grayjay.Engine;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Pagers;

namespace Grayjay.ClientServer.Subscriptions.Algorithms
{
    public abstract class SubscriptionFetchAlgorithm
    {
        /*
        private readonly ForkJoinPool _threadPool;
        */

        public bool AllowFailure { get; }
        public bool WithCacheFallback { get; }

        /*
        public ForkJoinPool ThreadPool
        {
            get
            {
                if (_threadPool == null)
                {
                    throw new InvalidOperationException("Require thread pool parameter");
                }
                return _threadPool;
            }
        }*/

        public event Action<Subscription, PlatformContent> OnNewCacheHit;
        public event Action<int, int> OnProgress;

        protected void SetNewCacheHit(Subscription sub, PlatformContent content) => OnNewCacheHit?.Invoke(sub, content);
        protected void SetProgress(int progress, int total) => OnProgress?.Invoke(progress, total);

        protected SubscriptionFetchAlgorithm(bool allowFailure = false, bool withCacheFallback = true)
        {
            AllowFailure = allowFailure;
            WithCacheFallback = withCacheFallback;
        }

        public Dictionary<GrayjayPlugin, int> CountRequests(List<Subscription> subs)
        {
            return CountRequests(subs.ToDictionary(sub => sub, sub => new List<string> { sub.Channel.Url }));
        }

        public abstract Dictionary<GrayjayPlugin, int> CountRequests(Dictionary<Subscription, List<string>> subs);

        public Result GetSubscriptions(List<Subscription> subs)
        {
            return GetSubscriptions(subs.ToDictionary(sub => sub, sub => new List<string> { sub.Channel.Url }));
        }

        public abstract Result GetSubscriptions(Dictionary<Subscription, List<string>> subs);

        public class Result
        {
            public IPager<PlatformContent> Pager { get; }
            public List<Exception> Exceptions { get; }

            public Result(IPager<PlatformContent> pager, List<Exception> exceptions)
            {
                Pager = pager;
                Exceptions = exceptions;
            }
        }

        public static SubscriptionFetchAlgorithm GetAlgorithm(SubscriptionFetchAlgorithms algo, bool allowFailure, bool withCacheFallback, ManagedThreadPool threadPool = null)
        {
            switch (algo)
            {
                case SubscriptionFetchAlgorithms.Smart:
                    return new SmartSubscriptionAlgorithm(allowFailure, withCacheFallback, threadPool);
                case SubscriptionFetchAlgorithms.Cache:
                    return new CachedSubscriptionAlgorithm(20);
                default:
                    throw new NotImplementedException();
            }
        }
    }
}
