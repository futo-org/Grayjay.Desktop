using Grayjay.Desktop.POC.Port;
using Grayjay.Engine;

namespace Grayjay.ClientServer.Pooling
{
    public class PlatformMultiClientPool
    {
        private readonly string _name;
        private readonly int _maxCap;
        private readonly Dictionary<GrayjayPlugin, PlatformClientPool> _clientPools = new Dictionary<GrayjayPlugin, PlatformClientPool>();

        private bool _isFake = false;

        public PlatformMultiClientPool(string name, int maxCap = -1)
        {
            _name = name;
            _maxCap = maxCap > 0 ? maxCap : 99;
        }

        public GrayjayPlugin GetClientPooled(GrayjayPlugin parentClient, int capacity = -1)
        {
            if (_isFake)
                return parentClient;

            var pool = (_clientPools.ContainsKey(parentClient)) ? _clientPools[parentClient] : new PlatformClientPool(parentClient, _name);

            lock (_clientPools)
            {
                if (!_clientPools.ContainsKey(parentClient))
                {
                    _clientPools[parentClient] = pool;

                    pool.OnDead += (_, poolToRemove) =>
                    {
                        lock (_clientPools)
                        {
                            if (_clientPools[parentClient] == poolToRemove)
                                _clientPools.Remove(parentClient);
                        }
                    };
                }

                pool = _clientPools[parentClient];
            }

            return pool.GetClient(capacity > 0 ? Math.Min(capacity, _maxCap) : _maxCap);
        }

        // Allows for testing disabling pooling without changing callers
        public PlatformMultiClientPool AsFake()
        {
            _isFake = true;
            return this;
        }
    }
}
