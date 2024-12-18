using System.IO.Compression;
using System.Net.Security;
using System.Net.Sockets;
using Grayjay.Desktop.POC;

namespace Grayjay.ClientServer.Proxy
{
    public class HttpProxySession : IDisposable
    {
        private readonly HttpProxy _proxy;
        private readonly Stream _stream;
        private readonly Action<HttpProxySession> _onDisconnected;
        private CancellationTokenSource _cancellationTokenSource;
        private static int[] _redirectStatusCodes = [ 301, 302, 207, 308 ];
        private static string[] _noBodyMethods = 
        [
                "GET",
                "HEAD",
                "DELETE",
                "OPTIONS",
                "TRACE"
        ];

        public HttpProxySession(HttpProxy proxy, Stream stream, CancellationToken cancellationToken, Action<HttpProxySession> onDisconnected)
        {
            _proxy = proxy;
            _stream = stream;
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _onDisconnected = onDisconnected;
        }

        public void Start()
        {
            Task.Run(async () => 
            {
                try
                {
                    await RunAsync();
                }
                catch (Exception e)
                {
                    Logger.e(nameof(HttpProxy), "Failed to handle data.", e);
                }
                finally
                {
                    //_logger.LogInformation("Closing connection.");
                    Dispose();
                }
            });
        }

        public async Task RunAsync()
        {
            using var clientStream = new HttpProxyStream(_stream);
            bool keepAlive = false;
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                var incomingRequest = await clientStream.ReadRequestHeadersAsync(_cancellationTokenSource.Token);
                var idString = incomingRequest.Path.StartsWith("/") ? incomingRequest.Path.Substring(1) : incomingRequest.Path;
                if (idString.Contains("?"))
                    idString = idString.Substring(0, idString.IndexOf("?"));
                Guid id = Guid.Empty;
                bool isRelativeProxy = false;
                if(!Guid.TryParse(idString, out id))
                {
                    string? referer;
                    int port = _proxy.LocalEndPoint.Port;
                    if (incomingRequest.Headers.TryGetValue("referer", out referer) && referer != null) 
                    {
                        if (referer.Contains("localhost:" + port) || referer.Contains("127.0.0.1:" + port))
                        {
                            Uri refererUri = new Uri(referer);
                            idString = refererUri.LocalPath.StartsWith("/") ? refererUri.LocalPath.Substring(1) : refererUri.LocalPath;
                            if (Guid.TryParse(idString, out id))
                                isRelativeProxy = true;
                        }
                    }
                }

                if (id == Guid.Empty)
                    throw new InvalidOperationException($"Request was not a valid proxy request: " + incomingRequest.Path);
                var registryEntry = _proxy.GetEntry(id);
                if (isRelativeProxy && !registryEntry.SupportRelativeProxy)
                    throw new InvalidOperationException($"Relative proxy request not supported: " + incomingRequest.Path);

                if (incomingRequest.Method == "OPTIONS" && registryEntry.ResponseHeaderOptions.InjectPermissiveCORS && (registryEntry.SupportedMethods == null || registryEntry.SupportedMethods.Contains("OPTIONS")))
                {
                    await clientStream.WriteResponseAsync(new HttpProxyResponse()
                    {
                        Headers = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
                        {
                            { "access-control-allow-headers", "*" },
                            { "access-control-allow-methods", registryEntry.SupportedMethods != null ? string.Join(", ", registryEntry.SupportedMethods) : "*" },
                            { "access-control-allow-origin", "*" },
                            { "content-length", "0" } //TODO: Can this become 204 and can content-length: 0 then be removed?
                        },
                        StatusCode = 200,
                        Version = "HTTP/1.1"
                    }, _cancellationTokenSource.Token);
                }
                else
                {
                    if (registryEntry.SupportedMethods != null && !registryEntry.SupportedMethods.Contains(incomingRequest.Method))
                        throw new Exception("Unsupported method.");

                    //else proxy the request


                    //TODO: Keep TCP stream alive if host doesn't change and keep alive is true
                    string url = registryEntry.Url;
                    string? previousUrl = null;
                    HttpProxyResponse? returnedResponse = null;
                    while (true)
                    {
                        var parsedUrl = Utilities.ParseUrl(url);
                        if (isRelativeProxy || registryEntry.IsRelative)
                        {
                            url = $"{parsedUrl.Scheme}://{parsedUrl.HostAndPort}{incomingRequest.Path}";
                            parsedUrl = Utilities.ParseUrl(url);
                        }

                        incomingRequest.Path = parsedUrl.Path;

                        if (registryEntry.RequestHeaderOptions.InjectHost)
                            incomingRequest.Headers["host"] = parsedUrl.Host;
                        if (registryEntry.RequestHeaderOptions.InjectOrigin)
                            incomingRequest.Headers["origin"] = parsedUrl.Scheme + "://" + parsedUrl.Host;

                        if (registryEntry.RequestHeaderOptions.InjectReferer)
                        {
                            //TODO: This is not entirely correct, referer should have something related to the previous request
                            if (isRelativeProxy)
                                incomingRequest.Headers["referer"] = registryEntry.Url;
                            else
                                incomingRequest.Headers["referer"] = url;
                        }

                        foreach (var r in registryEntry.RequestHeaderOptions.HeadersToInject)
                        {
                            if (r.Value == null)
                                incomingRequest.Headers.Remove(r.Key);
                            else
                                incomingRequest.Headers[r.Key] = r.Value;
                        }

                        if (registryEntry.RequestExecutor != null)
                        {
                            returnedResponse = registryEntry.RequestExecutor.Invoke(incomingRequest);
                            if (returnedResponse == null)
                                throw new InvalidOperationException("Failed to run request executor for: " + url);

                            if (registryEntry.ResponseHeaderOptions.InjectPermissiveCORS)
                                returnedResponse.Headers["access-control-allow-origin"] = "*";

                            await clientStream.WriteResponseAsync(returnedResponse, _cancellationTokenSource.Token);
                            await clientStream.FlushAsync(_cancellationTokenSource.Token);
                            return;
                        }

                        var (c, s) = await OpenWriteConnectionAsync(parsedUrl.Scheme, parsedUrl.Host, parsedUrl.Port, _cancellationTokenSource.Token);
                        using var destinationClient = c;
                        using var destinationStream = new HttpProxyStream(s);

                        await destinationStream.WriteRequestAsync(incomingRequest, _cancellationTokenSource.Token);
                        if (incomingRequest.Headers.TryGetValue("transfer-encoding", out var te) && te == "chunked")
                        {
                            await clientStream.TransferAllChunksAsync(destinationStream);
                            await destinationStream.FlushAsync(_cancellationTokenSource.Token);
                        }
                        else if (incomingRequest.Headers.TryGetValue("content-length", out var contentLengthStr) && int.TryParse(contentLengthStr, out var contentLength))
                        {
                            await clientStream.TransferFixedLengthContentAsync(destinationStream, contentLength);
                            await destinationStream.FlushAsync(_cancellationTokenSource.Token);
                        }
                        else if (!_noBodyMethods.Contains(incomingRequest.Method))
                        {
                            await clientStream.TransferUntilEndOfStreamAsync(destinationStream);
                            await destinationStream.FlushAsync(_cancellationTokenSource.Token);
                            break;
                        }

                        returnedResponse = await destinationStream.ReadResponseHeadersAsync(_cancellationTokenSource.Token);
                        
                        var shouldFollowRedirect = _redirectStatusCodes.Contains(returnedResponse.StatusCode) && registryEntry.FollowRedirects && returnedResponse.Headers.ContainsKey("location");
                        if (!shouldFollowRedirect)
                        {
                            if (registryEntry.ResponseHeaderOptions.InjectPermissiveCORS)
                                returnedResponse.Headers["access-control-allow-origin"] = "*";

                            foreach (var r in registryEntry.ResponseHeaderOptions.HeadersToInject)
                            {
                                if (r.Value == null)
                                    returnedResponse.Headers.Remove(r.Key);
                                else
                                    returnedResponse.Headers[r.Key] = r.Value;
                            }

                            if (incomingRequest.Method != "HEAD")
                            {
                                var bodyModifier = registryEntry.ResponseModifier?.Invoke(returnedResponse);
                                if (bodyModifier != null)
                                {
                                    var invokeBodyModifier = (byte[] bodyBytes) =>
                                    {
                                        if (returnedResponse.Headers.TryGetValue("content-encoding", out var contentEncoding))
                                        {
                                            switch (contentEncoding.ToLower())
                                            {
                                                case "gzip":
                                                    bodyBytes = DecompressGzip(bodyBytes);
                                                    break;
                                                case "deflate":
                                                    bodyBytes = DecompressDeflate(bodyBytes);
                                                    break;
                                                case "br":
                                                    bodyBytes = DecompressBrotli(bodyBytes);
                                                    break;
                                                case "zstd":
                                                    bodyBytes = DecompressZstd(bodyBytes);
                                                    break;
                                                default:
                                                    throw new Exception("Unsupported content encoding.");                                            
                                            }

                                            returnedResponse.Headers.Remove("content-encoding");
                                        }
                                        return bodyModifier(bodyBytes);
                                    };

                                    if (returnedResponse.Headers.TryGetValue("transfer-encoding", out var transferEncoding) && transferEncoding == "chunked")
                                    {
                                        returnedResponse.Headers.Remove("transfer-encoding");
                                        using var bodyStream = new MemoryStream();
                                        using (var httpBodyStream = new HttpProxyStream(bodyStream))
                                            await destinationStream.TransferAllChunksAsync(httpBodyStream, true, _cancellationTokenSource.Token);
                                        var modifiedBody = invokeBodyModifier(bodyStream.ToArray());
                                        returnedResponse.Headers["content-length"] = modifiedBody.Length.ToString();
                                        await clientStream.WriteResponseAsync(returnedResponse, _cancellationTokenSource.Token);
                                        await clientStream.WriteAsync(modifiedBody);
                                        await clientStream.FlushAsync(_cancellationTokenSource.Token);
                                    }
                                    else if (returnedResponse.Headers.TryGetValue("content-length", out var contentLengthStr) && int.TryParse(contentLengthStr, out var contentLength))
                                    {
                                        using var bodyStream = new MemoryStream();
                                        using (var httpBodyStream = new HttpProxyStream(bodyStream))
                                            await destinationStream.TransferFixedLengthContentAsync(httpBodyStream, contentLength, _cancellationTokenSource.Token);
                                        var modifiedBody = invokeBodyModifier(bodyStream.ToArray());
                                        returnedResponse.Headers["content-length"] = modifiedBody.Length.ToString();
                                        await clientStream.WriteResponseAsync(returnedResponse, _cancellationTokenSource.Token);
                                        await clientStream.WriteAsync(modifiedBody);
                                        await clientStream.FlushAsync(_cancellationTokenSource.Token);
                                    }
                                    else
                                    {
                                        using var bodyStream = new MemoryStream();
                                        using (var httpBodyStream = new HttpProxyStream(bodyStream))
                                            await destinationStream.TransferUntilEndOfStreamAsync(httpBodyStream, _cancellationTokenSource.Token);
                                        var modifiedBody = invokeBodyModifier(bodyStream.ToArray());
                                        returnedResponse.Headers["content-length"] = modifiedBody.Length.ToString();
                                        await clientStream.WriteResponseAsync(returnedResponse, _cancellationTokenSource.Token);
                                        await clientStream.WriteAsync(modifiedBody);
                                        await clientStream.FlushAsync(_cancellationTokenSource.Token);
                                        break;
                                    }
                                }
                                else
                                {
                                    await clientStream.WriteResponseAsync(returnedResponse, _cancellationTokenSource.Token);
                                    await clientStream.FlushAsync(_cancellationTokenSource.Token);

                                    if (returnedResponse.Headers.TryGetValue("transfer-encoding", out var transferEncoding) && transferEncoding == "chunked")
                                    {
                                        await destinationStream.TransferAllChunksAsync(clientStream);
                                        await clientStream.FlushAsync(_cancellationTokenSource.Token);
                                    }
                                    else if (returnedResponse.Headers.TryGetValue("content-length", out var contentLengthStr) && int.TryParse(contentLengthStr, out var contentLength))
                                    {
                                        await destinationStream.TransferFixedLengthContentAsync(clientStream, contentLength);
                                        await clientStream.FlushAsync(_cancellationTokenSource.Token);
                                    }
                                    else
                                    {
                                        await destinationStream.TransferUntilEndOfStreamAsync(clientStream);
                                        await clientStream.FlushAsync(_cancellationTokenSource.Token);
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                await clientStream.WriteResponseAsync(returnedResponse, _cancellationTokenSource.Token);
                                await clientStream.FlushAsync(_cancellationTokenSource.Token);
                            }
                            break;
                        }

                        var location = returnedResponse.Headers["location"];
                        //Logger.v(nameof(HttpProxySession), $"Redirected ({returnedResponse.StatusCode}) to location: {location}");
                        previousUrl = url;
                        url = location;
                    }
                }

                keepAlive = incomingRequest.Headers.TryGetValue("connection", out var connection) && connection.ToLowerInvariant() == "keep-alive";
                if (!keepAlive)
                {
                    //_logger.LogInformation("Keep alive is false, terminating connection.");
                    break;
                }

                //_logger.LogInformation("Keep alive is true, keeping connection alive.");
            }
        }

        private async Task<(TcpClient Client, Stream Stream)> OpenWriteConnectionAsync(string scheme, string host, int port, CancellationToken cancellationToken = default)
        {
            if (scheme == "http")
            {
                var c = new TcpClient();
                await c.ConnectAsync(host, port, cancellationToken);
                return (c, c.GetStream());
            }
            else if (scheme == "https")
            {
                var c = new TcpClient();
                await c.ConnectAsync(host, port, cancellationToken);
                var sslStream = new SslStream(c.GetStream());
                await sslStream.AuthenticateAsClientAsync(host); //TODO: Add cancellation token?
                return (c, sslStream);
            }
            else
                throw new NotImplementedException();
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _stream.Dispose();
            _onDisconnected(this);
        }

        private byte[] DecompressGzip(byte[] data)
        {
            using var inputStream = new MemoryStream(data);
            using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
            using var outputStream = new MemoryStream();
            gzipStream.CopyTo(outputStream);
            return outputStream.ToArray();
        }

        private byte[] DecompressDeflate(byte[] data)
        {
            using var inputStream = new MemoryStream(data);
            using var deflateStream = new DeflateStream(inputStream, CompressionMode.Decompress);
            using var outputStream = new MemoryStream();
            deflateStream.CopyTo(outputStream);
            return outputStream.ToArray();
        }

        private byte[] DecompressBrotli(byte[] data)
        {
            using var inputStream = new MemoryStream(data);
            using var brotliStream = new BrotliStream(inputStream, CompressionMode.Decompress);
            using var outputStream = new MemoryStream();
            brotliStream.CopyTo(outputStream);
            return outputStream.ToArray();
        }

        public byte[] DecompressZstd(byte[] data)
        {
            using var inputStream = new MemoryStream(data);
            using var decompressor = new ZstdNet.Decompressor();
            byte[] decompressedData = decompressor.Unwrap(data);
            return decompressedData;
        }
    }
}