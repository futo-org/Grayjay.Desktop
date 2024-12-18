using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Grayjay.Desktop.POC;

namespace Grayjay.ClientServer.Proxy
{
    public class HttpProxy : IDisposable
    {
        private readonly ConcurrentDictionary<Guid, HttpProxyRegistryEntry> _entries = new ConcurrentDictionary<Guid, HttpProxyRegistryEntry>();
        private readonly TcpListener _listener;
        private readonly List<HttpProxySession> _sessions = new List<HttpProxySession>();
        private CancellationTokenSource? _cancellationTokenSource = null;
        public IPEndPoint LocalEndPoint => (IPEndPoint)_listener.LocalEndpoint;

        public HttpProxy(IPEndPoint localEndPoint)
        {
            _listener = new TcpListener(localEndPoint.Address, localEndPoint.Port);
        }

        public string Add(HttpProxyRegistryEntry entry, IPAddress? localAddress = null)
        {
            if(entry.Url.Contains("#"))
            {

            }

            if (LocalEndPoint.Address == IPAddress.Any && localAddress == null)
                throw new ArgumentException("When adding a proxy on any, you must specify the local address.");

            var id = Guid.NewGuid();
            entry.Id = id;
            _entries.AddOrUpdate(id, entry, (i, e) => entry);
            return $"http://{(localAddress ?? LocalEndPoint.Address).ToUrlAddress()}:{LocalEndPoint.Port}/{id}";
        }
        public void Remove(Guid id)
        {
            _entries.Remove(id, out _);
        }

        public void Start()
        {
            _cancellationTokenSource = new CancellationTokenSource();

            Logger.i(nameof(HttpProxy), $"Started proxy listener on {LocalEndPoint}.");
            _listener.Start();

            _ = Task.Run(async () =>
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        var client = await _listener.AcceptTcpClientAsync(_cancellationTokenSource.Token);
                        var session = new HttpProxySession(this, client.GetStream(), _cancellationTokenSource.Token, (s) =>
                        {
                            lock (_sessions)
                                _sessions.Remove(s);
                        });

                        session.Start();
                        lock (_sessions)
                            _sessions.Add(session);

                        Logger.i(nameof(HttpProxy), "Client accepted.");
                    }
                    catch (Exception e)
                    {
                        Logger.e(nameof(HttpProxy), "Failed to accept client.", e);
                    }
                }
            }, _cancellationTokenSource.Token);
        }

        public void Dispose()
        {
            _sessions.Clear();
            _listener.Stop();
            _cancellationTokenSource?.Cancel();
        }

        public HttpProxyRegistryEntry GetEntry(Guid id)
        {
            if (!_entries.TryGetValue(id, out var registryEntry))
                throw new Exception($"A handler for this id {id} does not exist.");
            return registryEntry;
        }

        private static object _lockObject = new object();
        private static HttpProxy? _httpProxyLoopback = null;
        private static HttpProxy? _httpProxy = null;
        internal HttpProxyRegistryEntry _liveChatProxy;

        public static HttpProxy Get(bool loopback = true)
        {
            lock (_lockObject)
            {
                if (loopback)
                {
                    if (_httpProxyLoopback == null)
                    {
                        _httpProxyLoopback = new HttpProxy(new IPEndPoint(IPAddress.Loopback, 0));
                        _httpProxyLoopback.Start();
                    }
                    return _httpProxyLoopback;
                }
                else
                {
                    if (_httpProxy == null)
                    {
                        _httpProxy = new HttpProxy(new IPEndPoint(IPAddress.Any, 0));
                        _httpProxy.Start();
                    }
                    return _httpProxy;
                }
            }
        }

        public static void Stop()
        {
            lock (_lockObject)
            {
                _httpProxyLoopback?.Dispose();
                _httpProxyLoopback = null;
                _httpProxy?.Dispose();
                _httpProxy = null;
            }
        }
    }
}