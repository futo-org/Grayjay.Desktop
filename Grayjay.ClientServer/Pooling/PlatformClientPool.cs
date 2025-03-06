using Grayjay.ClientServer.Controllers;
using Grayjay.Desktop.POC;
using Grayjay.Engine;
using System.Net;

using Logger = Grayjay.Desktop.POC.Logger;

namespace Grayjay.ClientServer.Pooling
{
    public class PlatformClientPool
    {
        private readonly GrayjayPlugin _parent;
        private readonly Dictionary<GrayjayPlugin, int> _pool = new Dictionary<GrayjayPlugin, int>();
        private int _poolCounter = 0;
        private readonly string _poolName;

        public bool IsDead { get; private set; } = false;
        public event Action<GrayjayPlugin, PlatformClientPool> OnDead;

        public PlatformClientPool(GrayjayPlugin parentClient, string? name = null)
        {
            _poolName = name;
            if (!(parentClient is GrayjayPlugin))
                throw new ArgumentException("Pooling only supported for JSClients right now");
            _parent = parentClient;

            Logger.Info<PlatformClientPool>($"Pool for {_parent.Config.Name} was started");

            parentClient.OnStopped += (plugin) =>
            {
                if (parentClient == plugin)
                {
                    IsDead = true;
                    OnDead?.Invoke(parentClient, this);
                    foreach (var client in _pool)
                        client.Key.Disable();
                }
            };
        }

        public GrayjayPlugin GetClient(int capacity)
        {
            if (capacity < 1)
                throw new ArgumentException("Capacity should be at least 1");

            GrayjayPlugin? reserved;
            lock (_pool)
            {
                _poolCounter++;
                reserved = _pool.Keys.FirstOrDefault(client => !client.IsBusy);
                if (reserved == null && _pool.Count < capacity)
                {
                    Logger.Info<PlatformClientPool>($"Started additional [{_parent.Config.Name}] client in pool [{_poolName}] ({_pool.Count + 1}/{capacity})");
                    reserved = _parent.GetCopy();
                    reserved.OnLog += (config, msg) =>
                    {
                        Logger.i("Plugin [" + config.Name + "]", msg);
                    };
                    reserved.OnToast += (a, b) => StateUI.Toast($"[{a.Name}] " + b);

                    reserved.Initialize();
                    _pool[reserved] = _poolCounter;
                }
                else
                {
                    reserved = _pool.OrderBy(pair => pair.Value).First().Key;
                }
                _pool[reserved] = _poolCounter;
            }
            return reserved;
        }

        public static string TAG => "PlatformClientPool";
    }
}
