using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Grayjay.ClientServer;
using Grayjay.ClientServer.Proxy;
using Microsoft.ClearScript.V8;
using Microsoft.VisualBasic;

namespace Grayjay.Desktop.Tests;

[TestClass]
public class ProxyTests
{
    [TestMethod]
    public async Task TestReadRequestUntilEnd()
    {
        var content = RandomStringGenerator.GenerateRandomString(200000);
        var inputStream = new MemoryStream(Encoding.UTF8.GetBytes($"""
        POST /test/demo_form.php HTTP/1.1
        Host: grayjay.app

        {content}
        """));
        using var httpStream = new HttpProxyStream(inputStream);

        var request = await httpStream.ReadRequestHeadersAsync();
        Assert.AreEqual("POST", request.Method);
        Assert.AreEqual("/test/demo_form.php", request.Path);
        Assert.AreEqual("HTTP/1.1", request.Version);

        var expectedHeaders = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
        {
            { "host", "grayjay.app"}
        };

        AssertDictionariesAreEqual(expectedHeaders, request.Headers);

        using var bodyStream = new MemoryStream();
        await httpStream.TransferUntilEndOfStreamAsync(bodyStream);
        Assert.AreEqual(content, Encoding.UTF8.GetString(bodyStream.ToArray()));
    }

    [TestMethod]
    public async Task TestReadMultipleRequests()
    {
        var contents = new string[3]
        {
            RandomStringGenerator.GenerateRandomString(200000),
            RandomStringGenerator.GenerateRandomString(500000),
            RandomStringGenerator.GenerateRandomString(800000)
        };
        var byteContents = contents.Select(v => Encoding.UTF8.GetBytes(v)).ToArray();

        var inputStream = new MemoryStream(Encoding.UTF8.GetBytes($"""
        POST /test/demo_form.php HTTP/1.1
        Host: grayjay.app
        Content-Length: {byteContents[0].Length}
        I: 0

        {contents[0]}POST /test/demo_form.php HTTP/1.1
        Host: grayjay.app
        Content-Length: {byteContents[1].Length}
        I: 1

        {contents[1]}POST /test/demo_form.php HTTP/1.1
        Host: grayjay.app
        Content-Length: {byteContents[2].Length}
        I: 2

        {contents[2]}
        """));
        using var httpStream = new HttpProxyStream(inputStream);

        for (var i = 0; i < 3; i++)
        {
            var request = await httpStream.ReadRequestHeadersAsync();
            Assert.AreEqual("POST", request.Method);
            Assert.AreEqual("/test/demo_form.php", request.Path);
            Assert.AreEqual("HTTP/1.1", request.Version);

            var expectedHeaders = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
            {
                { "host", "grayjay.app"},
                { "content-length", byteContents[i].Length.ToString()},
                { "i", i.ToString()}
            };

            AssertDictionariesAreEqual(expectedHeaders, request.Headers);

            using var bodyStream = new MemoryStream();
            await httpStream.TransferFixedLengthContentAsync(bodyStream, byteContents[i].Length);
            Assert.AreEqual(contents[i], Encoding.UTF8.GetString(bodyStream.ToArray()));
        }
    }

    [TestMethod]
    public async Task TestWriteRequest()
    {
        var content = Encoding.UTF8.GetBytes(RandomStringGenerator.GenerateRandomString(200000));

        //Forge the request
        var expectedHeaders = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
        {
            { "host", "grayjay.app"}
        };

        byte[] requestData;
        using (var outputStream = new MemoryStream())
        {
            using (var httpStream = new HttpProxyStream(outputStream, true))
            {
                await httpStream.WriteRequestAsync(new HttpProxyRequest()
                {
                    Headers = expectedHeaders,
                    Method = "GET",
                    Path = "/hello",
                    Version = "HTTP/1.1"
                });

                await httpStream.WriteAsync(content);
            }

            requestData = outputStream.ToArray();
        }

        //Check if the written request is valid
        {
            using var inputHttpStream = new HttpProxyStream(new MemoryStream(requestData));
            var readRequest = await inputHttpStream.ReadRequestHeadersAsync();

            Assert.AreEqual("GET", readRequest.Method);
            Assert.AreEqual("/hello", readRequest.Path);
            Assert.AreEqual("HTTP/1.1", readRequest.Version);
            CollectionAssert.AreEquivalent(expectedHeaders, readRequest.Headers);

            using var bodyStream = new MemoryStream();
            await inputHttpStream.TransferUntilEndOfStreamAsync(bodyStream);
            CollectionAssert.AreEqual(content, bodyStream.ToArray());
        }
    }

    [TestMethod]
    public async Task TestWriteMultipleRequest()
    {
        var contents = new byte[3][]
        {
            Encoding.UTF8.GetBytes(RandomStringGenerator.GenerateRandomString(200000)),
            Encoding.UTF8.GetBytes(RandomStringGenerator.GenerateRandomString(500000)),
            Encoding.UTF8.GetBytes(RandomStringGenerator.GenerateRandomString(800000))
        };

        //Forge the requests
        byte[] requestData;
        using (var outputStream = new MemoryStream())
        {
            using (var httpStream = new HttpProxyStream(outputStream, true))
            {
                for (var i = 0; i < 3; i++)
                {
                    await httpStream.WriteRequestAsync(new HttpProxyRequest()
                    {
                        Headers = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
                        {
                            { "host", "grayjay.app"},
                            { "content-length", contents[i].Length.ToString() },
                            { "i", i.ToString() }
                        },
                        Method = "GET",
                        Path = "/hello",
                        Version = "HTTP/1.1"
                    });

                    await httpStream.WriteAsync(contents[i]);
                }
            }

            requestData = outputStream.ToArray();
        }

        //Check if the written request is valid
        using var inputHttpStream = new HttpProxyStream(new MemoryStream(requestData));
        for (var i = 0; i < 3; i++)
        {
            var readRequest = await inputHttpStream.ReadRequestHeadersAsync();

            Assert.AreEqual("GET", readRequest.Method);
            Assert.AreEqual("/hello", readRequest.Path);
            Assert.AreEqual("HTTP/1.1", readRequest.Version);
            CollectionAssert.AreEquivalent(new Dictionary<string, string>()
            {
                { "host", "grayjay.app"},
                { "content-length", contents[i].Length.ToString() },
                { "i", i.ToString() }
            }, readRequest.Headers);

            using var bodyStream = new MemoryStream();
            await inputHttpStream.TransferFixedLengthContentAsync(bodyStream, contents[i].Length);
            CollectionAssert.AreEqual(contents[i], bodyStream.ToArray());
        }
    }

    [TestMethod]
    public async Task TestTransferEncodingChunked()
    {
        var chunkData = """
        7
        Grayjay
        1D
         is the best app in the world
        0
        
        
        """;

        var inputStream = new MemoryStream(Encoding.UTF8.GetBytes($"""
        HTTP/1.1 200 OK
        Content-Type: text/plain
        Transfer-Encoding: chunked

        {chunkData}
        """));
        using var httpStream = new HttpProxyStream(inputStream);

        var response = await httpStream.ReadResponseHeadersAsync();
        Assert.AreEqual("HTTP/1.1", response.Version);
        Assert.AreEqual("200", response.StatusCode.ToString());

        var expectedHeaders = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
        {
            { "content-type", "text/plain" },
            { "transfer-encoding", "chunked" }
        };

        AssertDictionariesAreEqual(expectedHeaders, response.Headers);

        byte[] chunkedData;
        using (var stream = new MemoryStream())
        {
            using (var chunkedStream = new HttpProxyStream(stream, true))
                await httpStream.TransferAllChunksAsync(chunkedStream);

            chunkedData = stream.ToArray();
        }

        var dataChunks = Encoding.UTF8.GetString(chunkedData);
        Assert.AreEqual(chunkData, dataChunks);
    }

    [TestMethod]
    public async Task TestTransferEncodingChunkedConcat()
    {
        var chunkData = """
        7
        Grayjay
        1D
         is the best app in the world
        0
        
        
        """;

        var inputStream = new MemoryStream(Encoding.UTF8.GetBytes($"""
        HTTP/1.1 200 OK
        Content-Type: text/plain
        Transfer-Encoding: chunked

        {chunkData}
        """));
        using var httpStream = new HttpProxyStream(inputStream);

        var response = await httpStream.ReadResponseHeadersAsync();
        Assert.AreEqual("HTTP/1.1", response.Version);
        Assert.AreEqual("200", response.StatusCode.ToString());

        var expectedHeaders = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
        {
            { "content-type", "text/plain" },
            { "transfer-encoding", "chunked" }
        };

        AssertDictionariesAreEqual(expectedHeaders, response.Headers);

        byte[] chunkedData;
        using (var stream = new MemoryStream())
        {
            using (var chunkedStream = new HttpProxyStream(stream, true))
                await httpStream.TransferAllChunksAsync(chunkedStream, true);

            chunkedData = stream.ToArray();
        }

        Assert.AreEqual("Grayjay is the best app in the world", Encoding.UTF8.GetString(chunkedData));
    }

    [TestMethod]
    public async Task TestIntegration_TransferEncodingChunked()
    {
        var chunkData = """
        7
        Grayjay
        1D
         is the best app in the world
        0


        """;

        var expectedResponse = $"""
        HTTP/1.1 200 OK
        Content-Type: text/plain
        Transfer-Encoding: chunked

        {chunkData}
        """;

        HttpProxyRequest? receivedRequest = null;
        var serverCancellationTokenSource = new CancellationTokenSource();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = StartMockServer(listener, Encoding.UTF8.GetBytes(expectedResponse), req => receivedRequest = req, serverCancellationTokenSource.Token);

        var httpProxy = new HttpProxy(new IPEndPoint(IPAddress.Loopback, 31413));
        var requestUri = new Uri(httpProxy.Add(new HttpProxyRegistryEntry()
        {
            Url = $"http://localhost:{port}/",
            SupportedMethods = [ "GET" ],
            ResponseHeaderOptions = new ResponseHeaderOptions()
            {
                InjectPermissiveCORS = false
            }
        }));

        var portString = requestUri.IsDefaultPort ? "" : (":" + 31413);
        var inputRequest = $"""
        GET {requestUri.LocalPath} HTTP/1.1
        Host: {requestUri.Host}{portString}
        Content-Length: 0

        
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(inputRequest));
        using var outputStream = new MemoryStream();

        var combinedStream = new DuplexStream(inputStream, outputStream, true);
        var session = new HttpProxySession(httpProxy, combinedStream, CancellationToken.None, s => { });

        session.Start();
        await Task.Delay(1000);

        var responseBytes = outputStream.ToArray();
        var responseString = Encoding.UTF8.GetString(responseBytes);

        Assert.AreEqual(expectedResponse, responseString);
        Assert.IsNotNull(receivedRequest);
        serverCancellationTokenSource.Cancel();
        await serverTask;
    }

    [TestMethod]
    public async Task TestIntegration_TransferEncodingChunked_ModifiedHeaders()
    {
        var chunkData = """
        7
        Grayjay
        1D
         is the best app in the world
        0


        """;

        var expectedResponse = $"""
        HTTP/1.1 200 OK
        Content-Type: text/plain
        Transfer-Encoding: chunked
        Test: SomeTestValue

        {chunkData}
        """;

        HttpProxyRequest? receivedRequest = null;
        var serverCancellationTokenSource = new CancellationTokenSource();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = StartMockServer(listener, Encoding.UTF8.GetBytes(expectedResponse), req => receivedRequest = req, serverCancellationTokenSource.Token);

        var httpProxy = new HttpProxy(new IPEndPoint(IPAddress.Loopback, 31413));
        var requestUri = new Uri(httpProxy.Add(new HttpProxyRegistryEntry()
        {
            Url = $"http://localhost:{port}/",
            SupportedMethods = [ "GET" ],
            ResponseHeaderOptions = new ResponseHeaderOptions()
            {
                InjectPermissiveCORS = false
            },
            ResponseModifier = (response) =>
            {
                response.Headers["Test"] = "SomeTestValue";
                return null;
            }
        }));

        var portString = requestUri.IsDefaultPort ? "" : (":" + 31413);
        var inputRequest = $"""
        GET {requestUri.LocalPath} HTTP/1.1
        Host: {requestUri.Host}{portString}
        Content-Length: 0

        
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(inputRequest));
        using var outputStream = new MemoryStream();

        var combinedStream = new DuplexStream(inputStream, outputStream, true);
        var session = new HttpProxySession(httpProxy, combinedStream, CancellationToken.None, s => { });

        session.Start();
        await Task.Delay(1000);

        var responseBytes = outputStream.ToArray();
        var responseString = Encoding.UTF8.GetString(responseBytes);

        Assert.AreEqual(expectedResponse, responseString);
        Assert.IsNotNull(receivedRequest);
        serverCancellationTokenSource.Cancel();
        await serverTask;
    }

    [TestMethod]
    public async Task TestIntegration_TransferEncodingChunked_ModifiedBody()
    {
        var chunkData = """
        7
        Grayjay
        1D
         is the best app in the world
        0


        """;

        var response = $"""
        HTTP/1.1 200 OK
        Content-Type: text/plain
        Transfer-Encoding: chunked

        {chunkData}
        """;

        HttpProxyRequest? receivedRequest = null;
        var serverCancellationTokenSource = new CancellationTokenSource();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = StartMockServer(listener, Encoding.UTF8.GetBytes(response), req => receivedRequest = req, serverCancellationTokenSource.Token);

        var httpProxy = new HttpProxy(new IPEndPoint(IPAddress.Loopback, 31413));
        var requestUri = new Uri(httpProxy.Add(new HttpProxyRegistryEntry()
        {
            Url = $"http://localhost:{port}/",
            SupportedMethods = [ "GET" ],
            ResponseHeaderOptions = new ResponseHeaderOptions()
            {
                InjectPermissiveCORS = false
            },
            ResponseModifier = (response) =>
            {
                return (requestBody) => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(requestBody) + "YAAA");
            }
        }));

        var portString = requestUri.IsDefaultPort ? "" : (":" + 31413);
        var inputRequest = $"""
        GET {requestUri.LocalPath} HTTP/1.1
        Host: {requestUri.Host}{portString}
        Content-Length: 0

        
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(inputRequest));
        using var outputStream = new MemoryStream();

        var combinedStream = new DuplexStream(inputStream, outputStream, true);
        var session = new HttpProxySession(httpProxy, combinedStream, CancellationToken.None, s => { });

        session.Start();
        await Task.Delay(1000);

        var responseBytes = outputStream.ToArray();
        var responseString = Encoding.UTF8.GetString(responseBytes);

        var modifiedResponse = $"""
        HTTP/1.1 200 OK
        Content-Type: text/plain
        content-length: 40

        Grayjay is the best app in the worldYAAA
        """;
        Assert.AreEqual(modifiedResponse, responseString);
        Assert.IsNotNull(receivedRequest);
        serverCancellationTokenSource.Cancel();
        await serverTask;
    }

    [TestMethod]
    public async Task TestIntegration_ContentLength()
    {
        var response = $"""
        HTTP/1.1 200 OK
        Content-Type: text/plain
        Content-Length: 36

        Grayjay is the best app in the world
        """;

        HttpProxyRequest? receivedRequest = null;
        var serverCancellationTokenSource = new CancellationTokenSource();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = StartMockServer(listener, Encoding.UTF8.GetBytes(response), req => receivedRequest = req, serverCancellationTokenSource.Token);

        var httpProxy = new HttpProxy(new IPEndPoint(IPAddress.Loopback, 31413));
        var requestUri = new Uri(httpProxy.Add(new HttpProxyRegistryEntry()
        {
            Url = $"http://localhost:{port}/",
            SupportedMethods = [ "GET" ],
            ResponseHeaderOptions = new ResponseHeaderOptions()
            {
                InjectPermissiveCORS = false
            }
        }));

        var portString = requestUri.IsDefaultPort ? "" : (":" + 31413);
        var inputRequest = $"""
        GET {requestUri.LocalPath} HTTP/1.1
        Host: {requestUri.Host}{portString}
        Content-Length: 0

        
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(inputRequest));
        using var outputStream = new MemoryStream();

        var combinedStream = new DuplexStream(inputStream, outputStream, true);
        var session = new HttpProxySession(httpProxy, combinedStream, CancellationToken.None, s => { });

        session.Start();
        await Task.Delay(1000);

        var responseBytes = outputStream.ToArray();
        var responseString = Encoding.UTF8.GetString(responseBytes);

        Assert.AreEqual(response, responseString);
        Assert.IsNotNull(receivedRequest);
        serverCancellationTokenSource.Cancel();
        await serverTask;
    }

    [TestMethod]
    public async Task TestIntegration_ContentLength_ModifiedBody()
    {
        var response = $"""
        HTTP/1.1 200 OK
        Content-Type: text/plain
        Content-Length: 36

        Grayjay is the best app in the world
        """;

        HttpProxyRequest? receivedRequest = null;
        var serverCancellationTokenSource = new CancellationTokenSource();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = StartMockServer(listener, Encoding.UTF8.GetBytes(response), req => receivedRequest = req, serverCancellationTokenSource.Token);

        var httpProxy = new HttpProxy(new IPEndPoint(IPAddress.Loopback, 31413));
        var requestUri = new Uri(httpProxy.Add(new HttpProxyRegistryEntry()
        {
            Url = $"http://localhost:{port}/",
            SupportedMethods = [ "GET" ],
            ResponseHeaderOptions = new ResponseHeaderOptions()
            {
                InjectPermissiveCORS = false
            },
            ResponseModifier = (response) =>
            {
                return (requestBody) => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(requestBody) + "YAAA");
            }
        }));

        var portString = requestUri.IsDefaultPort ? "" : (":" + 31413);
        var inputRequest = $"""
        GET {requestUri.LocalPath} HTTP/1.1
        Host: {requestUri.Host}{portString}
        Content-Length: 0

        
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(inputRequest));
        using var outputStream = new MemoryStream();

        var combinedStream = new DuplexStream(inputStream, outputStream, true);
        var session = new HttpProxySession(httpProxy, combinedStream, CancellationToken.None, s => { });

        session.Start();
        await Task.Delay(1000);

        var responseBytes = outputStream.ToArray();
        var responseString = Encoding.UTF8.GetString(responseBytes);

        var modifiedResponse = $"""
        HTTP/1.1 200 OK
        Content-Type: text/plain
        Content-Length: 40

        Grayjay is the best app in the worldYAAA
        """;
        Assert.AreEqual(modifiedResponse, responseString);
        Assert.IsNotNull(receivedRequest);
        serverCancellationTokenSource.Cancel();
        await serverTask;
    }

    [TestMethod]
    public async Task TestIntegration_EndOfStream_ModifiedBody()
    {
        var response = $"""
        HTTP/1.1 200 OK
        Content-Type: text/plain

        Grayjay is the best app in the world
        """;

        HttpProxyRequest? receivedRequest = null;
        var serverCancellationTokenSource = new CancellationTokenSource();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = StartMockServer(listener, Encoding.UTF8.GetBytes(response), req => receivedRequest = req, serverCancellationTokenSource.Token);

        var httpProxy = new HttpProxy(new IPEndPoint(IPAddress.Loopback, 31413));
        var requestUri = new Uri(httpProxy.Add(new HttpProxyRegistryEntry()
        {
            Url = $"http://localhost:{port}/",
            SupportedMethods = [ "GET" ],
            ResponseHeaderOptions = new ResponseHeaderOptions()
            {
                InjectPermissiveCORS = false
            },
            ResponseModifier = (response) =>
            {
                return (requestBody) => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(requestBody) + "YAAA");
            }
        }));

        var portString = requestUri.IsDefaultPort ? "" : (":" + 31413);
        var inputRequest = $"""
        GET {requestUri.LocalPath} HTTP/1.1
        Host: {requestUri.Host}{portString}
        Content-Length: 0

        
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(inputRequest));
        using var outputStream = new MemoryStream();

        var combinedStream = new DuplexStream(inputStream, outputStream, true);
        var session = new HttpProxySession(httpProxy, combinedStream, CancellationToken.None, s => { });

        session.Start();
        await Task.Delay(1000);

        var responseBytes = outputStream.ToArray();
        var responseString = Encoding.UTF8.GetString(responseBytes);

        var modifiedResponse = $"""
        HTTP/1.1 200 OK
        Content-Type: text/plain
        content-length: 40

        Grayjay is the best app in the worldYAAA
        """;
        Assert.AreEqual(modifiedResponse, responseString);
        Assert.IsNotNull(receivedRequest);
        serverCancellationTokenSource.Cancel();
        await serverTask;
    }

    [TestMethod]
    public async Task TestIntegration_EndOfStream()
    {
        var response = $"""
        HTTP/1.1 200 OK
        Content-Type: text/plain

        Grayjay is the best app in the world
        """;

        HttpProxyRequest? receivedRequest = null;
        var serverCancellationTokenSource = new CancellationTokenSource();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = StartMockServer(listener, Encoding.UTF8.GetBytes(response), req => receivedRequest = req, serverCancellationTokenSource.Token);

        var httpProxy = new HttpProxy(new IPEndPoint(IPAddress.Loopback, 31413));
        var requestUri = new Uri(httpProxy.Add(new HttpProxyRegistryEntry()
        {
            Url = $"http://localhost:{port}/",
            SupportedMethods = [ "GET" ],
            ResponseHeaderOptions = new ResponseHeaderOptions()
            {
                InjectPermissiveCORS = false
            }
        }));

        var portString = requestUri.IsDefaultPort ? "" : (":" + 31413);
        var inputRequest = $"""
        GET {requestUri.LocalPath} HTTP/1.1
        Host: {requestUri.Host}{portString}
        Content-Length: 0

        
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(inputRequest));
        using var outputStream = new MemoryStream();

        var combinedStream = new DuplexStream(inputStream, outputStream, true);
        var session = new HttpProxySession(httpProxy, combinedStream, CancellationToken.None, s => { });

        session.Start();
        await Task.Delay(1000);

        var responseBytes = outputStream.ToArray();
        var responseString = Encoding.UTF8.GetString(responseBytes);

        var modifiedResponse = $"""
        HTTP/1.1 200 OK
        Content-Type: text/plain

        Grayjay is the best app in the world
        """;
        Assert.AreEqual(modifiedResponse, responseString);
        Assert.IsNotNull(receivedRequest);
        serverCancellationTokenSource.Cancel();
        await serverTask;
    }

    [TestMethod]
    public async Task TestIntegration_RequestBody()
    {
        var response = $"""
        HTTP/1.1 200 OK
        Content-Type: text/plain

        Grayjay is the best app in the world
        """;

        HttpProxyRequest? receivedRequest = null;
        var serverCancellationTokenSource = new CancellationTokenSource();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = StartMockServer(listener, Encoding.UTF8.GetBytes(response), req => receivedRequest = req, serverCancellationTokenSource.Token);

        var httpProxy = new HttpProxy(new IPEndPoint(IPAddress.Loopback, 31413));
        var requestUri = new Uri(httpProxy.Add(new HttpProxyRegistryEntry()
        {
            Url = $"http://localhost:{port}/",
            SupportedMethods = [ "GET" ],
            ResponseHeaderOptions = new ResponseHeaderOptions()
            {
                InjectPermissiveCORS = false
            }
        }));

        var portString = requestUri.IsDefaultPort ? "" : (":" + 31413);
        var inputRequest = $"""
        GET {requestUri.LocalPath} HTTP/1.1
        Host: {requestUri.Host}{portString}
        Content-Length: 34

        What is the best app in the world?
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(inputRequest));
        using var outputStream = new MemoryStream();

        var combinedStream = new DuplexStream(inputStream, outputStream, true);
        var session = new HttpProxySession(httpProxy, combinedStream, CancellationToken.None, s => { });

        session.Start();
        await Task.Delay(1000);

        var responseBytes = outputStream.ToArray();
        var responseString = Encoding.UTF8.GetString(responseBytes);

        var modifiedResponse = $"""
        HTTP/1.1 200 OK
        Content-Type: text/plain

        Grayjay is the best app in the world
        """;
        Assert.AreEqual(modifiedResponse, responseString);
        Assert.IsNotNull(receivedRequest);
        serverCancellationTokenSource.Cancel();
        await serverTask;
    }

    [TestMethod]
    public async Task TestIntegration_ContentLength_Gzip()
    {
        var responseBody = "Grayjay is the best app in the world";
        var compressedResponseBody = CompressGzip(responseBody);

        var response = Encoding.UTF8.GetBytes($"""
        HTTP/1.1 200 OK
        Content-Type: text/plain
        Content-Encoding: gzip
        Content-Length: {compressedResponseBody.Length}


        """).Concat(compressedResponseBody).ToArray();

        HttpProxyRequest? receivedRequest = null;
        var serverCancellationTokenSource = new CancellationTokenSource();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = StartMockServer(listener, response, req => receivedRequest = req, serverCancellationTokenSource.Token);

        var httpProxy = new HttpProxy(new IPEndPoint(IPAddress.Loopback, 31413));
        var requestUri = new Uri(httpProxy.Add(new HttpProxyRegistryEntry()
        {
            Url = $"http://localhost:{port}/",
            SupportedMethods = [ "GET" ],
            ResponseHeaderOptions = new ResponseHeaderOptions()
            {
                InjectPermissiveCORS = false
            }
        }));

        var portString = requestUri.IsDefaultPort ? "" : (":" + 31413);
        var inputRequest = $"""
        GET {requestUri.LocalPath} HTTP/1.1
        Host: {requestUri.Host}{portString}
        Content-Length: 0

        
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(inputRequest));
        using var outputStream = new MemoryStream();

        var combinedStream = new DuplexStream(inputStream, outputStream, true);
        var session = new HttpProxySession(httpProxy, combinedStream, CancellationToken.None, s => { });

        session.Start();
        await Task.Delay(1000);

        var responseBytes = outputStream.ToArray();
        CollectionAssert.AreEqual(response, responseBytes);
        Assert.IsNotNull(receivedRequest);
        serverCancellationTokenSource.Cancel();
        await serverTask;
    }

    [TestMethod]
    public async Task TestIntegration_ContentLength_Gzip_WithModifier()
    {
        var responseBody = "Grayjay is the best app in the world";
        var compressedResponseBody = CompressGzip(responseBody);

        var response = Encoding.UTF8.GetBytes($"""
        HTTP/1.1 200 OK
        Content-Type: text/plain
        Content-Encoding: gzip
        Content-Length: {compressedResponseBody.Length}


        """).Concat(compressedResponseBody).ToArray();

        HttpProxyRequest? receivedRequest = null;
        var serverCancellationTokenSource = new CancellationTokenSource();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = StartMockServer(listener, response, req => receivedRequest = req, serverCancellationTokenSource.Token);

        var httpProxy = new HttpProxy(new IPEndPoint(IPAddress.Loopback, 31413));
        var requestUri = new Uri(httpProxy.Add(new HttpProxyRegistryEntry()
        {
            Url = $"http://localhost:{port}/",
            SupportedMethods = [ "GET" ],
            ResponseHeaderOptions = new ResponseHeaderOptions()
            {
                InjectPermissiveCORS = false
            },
            ResponseModifier = (resp) =>
            {
                return (body) => body;
            }
        }));

        var portString = requestUri.IsDefaultPort ? "" : (":" + 31413);
        var inputRequest = $"""
        GET {requestUri.LocalPath} HTTP/1.1
        Host: {requestUri.Host}{portString}
        Content-Length: 0

        
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(inputRequest));
        using var outputStream = new MemoryStream();

        var combinedStream = new DuplexStream(inputStream, outputStream, true);
        var session = new HttpProxySession(httpProxy, combinedStream, CancellationToken.None, s => { });

        session.Start();
        await Task.Delay(1000);

        var responseBytes = outputStream.ToArray();
        Assert.AreEqual($"""
        HTTP/1.1 200 OK
        Content-Type: text/plain
        Content-Length: 36

        Grayjay is the best app in the world
        """, Encoding.UTF8.GetString(responseBytes));
        Assert.IsNotNull(receivedRequest);
        serverCancellationTokenSource.Cancel();
        await serverTask;
    }

    [TestMethod]
    public async Task TestIntegration_ContentLength_Br_WithModifier()
    {
        var responseBody = "Grayjay is the best app in the world";
        var compressedResponseBody = CompressBrotli(responseBody);

        var response = Encoding.UTF8.GetBytes($"""
        HTTP/1.1 200 OK
        Content-Type: text/plain
        Content-Encoding: br
        Content-Length: {compressedResponseBody.Length}


        """).Concat(compressedResponseBody).ToArray();

        HttpProxyRequest? receivedRequest = null;
        var serverCancellationTokenSource = new CancellationTokenSource();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = StartMockServer(listener, response, req => receivedRequest = req, serverCancellationTokenSource.Token);

        var httpProxy = new HttpProxy(new IPEndPoint(IPAddress.Loopback, 31413));
        var requestUri = new Uri(httpProxy.Add(new HttpProxyRegistryEntry()
        {
            Url = $"http://localhost:{port}/",
            SupportedMethods = [ "GET" ],
            ResponseHeaderOptions = new ResponseHeaderOptions()
            {
                InjectPermissiveCORS = false
            },
            ResponseModifier = (resp) =>
            {
                return (body) => body;
            }
        }));

        var portString = requestUri.IsDefaultPort ? "" : (":" + 31413);
        var inputRequest = $"""
        GET {requestUri.LocalPath} HTTP/1.1
        Host: {requestUri.Host}{portString}
        Content-Length: 0

        
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(inputRequest));
        using var outputStream = new MemoryStream();

        var combinedStream = new DuplexStream(inputStream, outputStream, true);
        var session = new HttpProxySession(httpProxy, combinedStream, CancellationToken.None, s => { });

        session.Start();
        await Task.Delay(1000);

        var responseBytes = outputStream.ToArray();
        Assert.AreEqual($"""
        HTTP/1.1 200 OK
        Content-Type: text/plain
        Content-Length: 36

        Grayjay is the best app in the world
        """, Encoding.UTF8.GetString(responseBytes));
        Assert.IsNotNull(receivedRequest);
        serverCancellationTokenSource.Cancel();
        await serverTask;
    }
    
    [TestMethod]
    public async Task TestIntegration_ContentLength_Deflate_WithModifier()
    {
        var responseBody = "Grayjay is the best app in the world";
        var compressedResponseBody = CompressDeflate(responseBody);

        var response = Encoding.UTF8.GetBytes($"""
        HTTP/1.1 200 OK
        Content-Type: text/plain
        Content-Encoding: deflate
        Content-Length: {compressedResponseBody.Length}


        """).Concat(compressedResponseBody).ToArray();

        HttpProxyRequest? receivedRequest = null;
        var serverCancellationTokenSource = new CancellationTokenSource();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = StartMockServer(listener, response, req => receivedRequest = req, serverCancellationTokenSource.Token);

        var httpProxy = new HttpProxy(new IPEndPoint(IPAddress.Loopback, 31413));
        var requestUri = new Uri(httpProxy.Add(new HttpProxyRegistryEntry()
        {
            Url = $"http://localhost:{port}/",
            SupportedMethods = [ "GET" ],
            ResponseHeaderOptions = new ResponseHeaderOptions()
            {
                InjectPermissiveCORS = false
            },
            ResponseModifier = (resp) =>
            {
                return (body) => body;
            }
        }));

        var portString = requestUri.IsDefaultPort ? "" : (":" + 31413);
        var inputRequest = $"""
        GET {requestUri.LocalPath} HTTP/1.1
        Host: {requestUri.Host}{portString}
        Content-Length: 0

        
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(inputRequest));
        using var outputStream = new MemoryStream();

        var combinedStream = new DuplexStream(inputStream, outputStream, true);
        var session = new HttpProxySession(httpProxy, combinedStream, CancellationToken.None, s => { });

        session.Start();
        await Task.Delay(1000);

        var responseBytes = outputStream.ToArray();
        Assert.AreEqual($"""
        HTTP/1.1 200 OK
        Content-Type: text/plain
        Content-Length: 36

        Grayjay is the best app in the world
        """, Encoding.UTF8.GetString(responseBytes));
        Assert.IsNotNull(receivedRequest);
        serverCancellationTokenSource.Cancel();
        await serverTask;
    }
    
    [TestMethod]
    public async Task TestIntegration_ContentLength_Gzip_ModifyResponseString()
    {
        var responseBody = "Grayjay is the best app in the world";
        var compressedResponseBody = CompressGzip(responseBody);

        var response = Encoding.UTF8.GetBytes($"""
        HTTP/1.1 200 OK
        Content-Type: text/plain
        Content-Encoding: gzip
        Content-Length: {compressedResponseBody.Length}


        """).Concat(compressedResponseBody).ToArray();

        HttpProxyRequest? receivedRequest = null;
        var serverCancellationTokenSource = new CancellationTokenSource();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = StartMockServer(listener, response, req => receivedRequest = req, serverCancellationTokenSource.Token);

        var httpProxy = new HttpProxy(new IPEndPoint(IPAddress.Loopback, 31413));
        var requestUri = new Uri(httpProxy.Add(new HttpProxyRegistryEntry()
        {
            Url = $"http://localhost:{port}/",
            SupportedMethods = [ "GET" ],
            ResponseHeaderOptions = new ResponseHeaderOptions()
            {
                InjectPermissiveCORS = false
            }
        }.WithModifyResponseString((resp, body) => body)));

        var portString = requestUri.IsDefaultPort ? "" : (":" + 31413);
        var inputRequest = $"""
        GET {requestUri.LocalPath} HTTP/1.1
        Host: {requestUri.Host}{portString}
        Content-Length: 0

        
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(inputRequest));
        using var outputStream = new MemoryStream();

        var combinedStream = new DuplexStream(inputStream, outputStream, true);
        var session = new HttpProxySession(httpProxy, combinedStream, CancellationToken.None, s => { });

        session.Start();
        await Task.Delay(1000);

        var responseBytes = outputStream.ToArray();
        Assert.AreEqual($"""
        HTTP/1.1 200 OK
        Content-Type: text/plain
        Content-Length: 36

        Grayjay is the best app in the world
        """, Encoding.UTF8.GetString(responseBytes));
        Assert.IsNotNull(receivedRequest);
        serverCancellationTokenSource.Cancel();
        await serverTask;
    }

    [TestMethod]
    public async Task TestIntegration_ContentLength_Gzip_ModifyResponseString_ASCII()
    {
        var responseBody = "Grayjay is the best app in the world";
        var compressedResponseBody = CompressGzip(responseBody, Encoding.ASCII);

        var response = Encoding.UTF8.GetBytes($"""
        HTTP/1.1 200 OK
        Content-Type: text/plain; charset=ascii
        Content-Encoding: gzip
        Content-Length: {compressedResponseBody.Length}


        """).Concat(compressedResponseBody).ToArray();

        HttpProxyRequest? receivedRequest = null;
        var serverCancellationTokenSource = new CancellationTokenSource();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = StartMockServer(listener, response, req => receivedRequest = req, serverCancellationTokenSource.Token);

        var httpProxy = new HttpProxy(new IPEndPoint(IPAddress.Loopback, 31413));
        var requestUri = new Uri(httpProxy.Add(new HttpProxyRegistryEntry()
        {
            Url = $"http://localhost:{port}/",
            SupportedMethods = [ "GET" ],
            ResponseHeaderOptions = new ResponseHeaderOptions()
            {
                InjectPermissiveCORS = false
            }
        }.WithModifyResponseString((resp, body) => body)));

        var portString = requestUri.IsDefaultPort ? "" : (":" + 31413);
        var inputRequest = $"""
        GET {requestUri.LocalPath} HTTP/1.1
        Host: {requestUri.Host}{portString}
        Content-Length: 0

        
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(inputRequest));
        using var outputStream = new MemoryStream();

        var combinedStream = new DuplexStream(inputStream, outputStream, true);
        var session = new HttpProxySession(httpProxy, combinedStream, CancellationToken.None, s => { });

        session.Start();
        await Task.Delay(1000);

        var responseBytes = outputStream.ToArray();
        Assert.AreEqual($"""
        HTTP/1.1 200 OK
        Content-Type: text/plain; charset=ascii
        Content-Length: 36

        Grayjay is the best app in the world
        """, Encoding.UTF8.GetString(responseBytes));
        Assert.IsNotNull(receivedRequest);
        serverCancellationTokenSource.Cancel();
        await serverTask;
    }

    [TestMethod]
    public async Task TestIntegration_RelativeProxy_ShouldFail()
    {
        var response = $"""
        HTTP/1.1 200 OK
        Content-Type: text/plain

        Grayjay is the best app in the world
        """;

        HttpProxyRequest? receivedRequest = null;
        var serverCancellationTokenSource = new CancellationTokenSource();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = StartMockServer(listener, Encoding.UTF8.GetBytes(response), req => receivedRequest = req, serverCancellationTokenSource.Token);

        var httpProxy = new HttpProxy(new IPEndPoint(IPAddress.Loopback, 31413));
        var requestUri = new Uri(httpProxy.Add(new HttpProxyRegistryEntry()
        {
            Url = $"http://localhost:{port}/",
            SupportedMethods = [ "GET" ],
            ResponseHeaderOptions = new ResponseHeaderOptions()
            {
                InjectPermissiveCORS = false
            }
        }));

        var portString = requestUri.IsDefaultPort ? "" : (":" + 31413);
        var inputRequest = $"""
        GET /test/somepath?q=3 HTTP/1.1
        Host: {requestUri.Host}{portString}
        Referer: {requestUri.Scheme}://{requestUri.Host}{portString}{requestUri.LocalPath}
        Content-Length: 34

        What is the best app in the world?
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(inputRequest));
        using var outputStream = new MemoryStream();

        var combinedStream = new DuplexStream(inputStream, outputStream, true);
        var session = new HttpProxySession(httpProxy, combinedStream, CancellationToken.None, s => { });

        Exception? exception = null;
        _ = Task.Run(async () => 
        {
            try
            {
                await session.RunAsync();
            }
            catch (Exception e)
            {
                exception = e;
            }
        });

        await Task.Delay(1000);

        Assert.IsNotNull(exception);
        serverCancellationTokenSource.Cancel();
    }

    [TestMethod]
    public async Task TestIntegration_RelativeProxy_ShouldSucceed()
    {
        var response = $"""
        HTTP/1.1 200 OK
        Content-Type: text/plain

        Grayjay is the best app in the world
        """;

        HttpProxyRequest? receivedRequest = null;
        var serverCancellationTokenSource = new CancellationTokenSource();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = StartMockServer(listener, Encoding.UTF8.GetBytes(response), req => receivedRequest = req, serverCancellationTokenSource.Token);

        var httpProxy = new HttpProxy(new IPEndPoint(IPAddress.Loopback, 31413));
        var requestUri = new Uri(httpProxy.Add(new HttpProxyRegistryEntry()
        {
            Url = $"http://localhost:{port}/",
            SupportedMethods = [ "GET" ],
            ResponseHeaderOptions = new ResponseHeaderOptions()
            {
                InjectPermissiveCORS = false
            },
            SupportRelativeProxy = true
        }));

        var portString = requestUri.IsDefaultPort ? "" : (":" + 31413);
        var inputRequest = $"""
        GET /test/somepath?q=3 HTTP/1.1
        Host: {requestUri.Host}{portString}
        Referer: {requestUri.Scheme}://{requestUri.Host}{portString}{requestUri.LocalPath}
        Content-Length: 34

        What is the best app in the world?
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(inputRequest));
        using var outputStream = new MemoryStream();

        var combinedStream = new DuplexStream(inputStream, outputStream, true);
        var session = new HttpProxySession(httpProxy, combinedStream, CancellationToken.None, s => { });

        session.Start();
        await Task.Delay(1000);

        var responseBytes = outputStream.ToArray();
        var responseString = Encoding.UTF8.GetString(responseBytes);

        var modifiedResponse = $"""
        HTTP/1.1 200 OK
        Content-Type: text/plain

        Grayjay is the best app in the world
        """;

        Assert.IsNotNull(receivedRequest);
        Assert.AreEqual("/test/somepath?q=3", receivedRequest!.Path);
        Assert.AreEqual("GET", receivedRequest!.Method);
        Assert.AreEqual("localhost", receivedRequest!.Headers["Host"]);
        Assert.AreEqual($"http://localhost:{port}/", receivedRequest!.Headers["Referer"]);
        Assert.AreEqual("http://localhost", receivedRequest!.Headers["origin"]);
        Assert.AreEqual("34", receivedRequest!.Headers["Content-Length"]);
        Assert.AreEqual(modifiedResponse, responseString);
        Assert.IsNotNull(receivedRequest);
        serverCancellationTokenSource.Cancel();
        await serverTask;
    }

    [TestMethod]
    public async Task TestIntegration_RequestBody_WithRedirect_FollowRedirects_True()
    {
        var finalResponse = $"""
        HTTP/1.1 200 OK
        Content-Type: text/plain

        Grayjay is the best app in the world
        """;

        HttpProxyRequest? receivedRedirectRequest = null;
        HttpProxyRequest? receivedFinalRequest = null;

        var serverCancellationTokenSource = new CancellationTokenSource();

        var redirectListener = new TcpListener(IPAddress.Loopback, 0);
        redirectListener.Start();
        var redirectPort = ((IPEndPoint)redirectListener.LocalEndpoint).Port;

        var finalListener = new TcpListener(IPAddress.Loopback, 0);
        finalListener.Start();
        var finalPort = ((IPEndPoint)finalListener.LocalEndpoint).Port;
        
        var redirectResponse = $"""
        HTTP/1.1 301 Moved Permanently
        Location: http://localhost:{finalPort}/


        """;

        var redirectServerTask = StartMockServer(redirectListener, Encoding.UTF8.GetBytes(redirectResponse.Replace("{{finalPort}}", finalPort.ToString())), req => receivedRedirectRequest = req, serverCancellationTokenSource.Token);
        var finalServerTask = StartMockServer(finalListener, Encoding.UTF8.GetBytes(finalResponse), req => receivedFinalRequest = req, serverCancellationTokenSource.Token);

        var httpProxy = new HttpProxy(new IPEndPoint(IPAddress.Loopback, 31413));
        var requestUri = new Uri(httpProxy.Add(new HttpProxyRegistryEntry()
        {
            Url = $"http://localhost:{redirectPort}/",
            SupportedMethods = ["GET"],
            ResponseHeaderOptions = new ResponseHeaderOptions()
            {
                InjectPermissiveCORS = false
            },
            FollowRedirects = true
        }));

        var portString = requestUri.IsDefaultPort ? "" : (":" + 31413);
        var inputRequest = $"""
        GET {requestUri.LocalPath} HTTP/1.1
        Host: {requestUri.Host}{portString}


        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(inputRequest));
        using var outputStream = new MemoryStream();

        var combinedStream = new DuplexStream(inputStream, outputStream, true);
        var session = new HttpProxySession(httpProxy, combinedStream, CancellationToken.None, s => { });

        session.Start();
        await Task.Delay(1000);

        var responseBytes = outputStream.ToArray();
        var responseString = Encoding.UTF8.GetString(responseBytes);

        Assert.AreEqual(finalResponse, responseString);
        Assert.IsNotNull(receivedRedirectRequest);
        Assert.IsNotNull(receivedFinalRequest);

        serverCancellationTokenSource.Cancel();
    }

    [TestMethod]
    public async Task TestIntegration_RequestBody_WithRedirect_FollowRedirects_False()
    {
        var finalResponse = $"""
        HTTP/1.1 200 OK
        Content-Type: text/plain

        Grayjay is the best app in the world
        """;

        HttpProxyRequest? receivedRedirectRequest = null;
        HttpProxyRequest? receivedFinalRequest = null;

        var serverCancellationTokenSource = new CancellationTokenSource();

        var redirectListener = new TcpListener(IPAddress.Loopback, 0);
        redirectListener.Start();
        var redirectPort = ((IPEndPoint)redirectListener.LocalEndpoint).Port;

        var finalListener = new TcpListener(IPAddress.Loopback, 0);
        finalListener.Start();
        var finalPort = ((IPEndPoint)finalListener.LocalEndpoint).Port;

        var redirectResponse = $"""
        HTTP/1.1 301 Moved Permanently
        Location: http://localhost:{finalPort}/


        """;

        var redirectServerTask = StartMockServer(redirectListener, Encoding.UTF8.GetBytes(redirectResponse.Replace("{{finalPort}}", finalPort.ToString())), req => receivedRedirectRequest = req, serverCancellationTokenSource.Token);
        var finalServerTask = StartMockServer(finalListener, Encoding.UTF8.GetBytes(finalResponse), req => receivedFinalRequest = req, serverCancellationTokenSource.Token);

        var httpProxy = new HttpProxy(new IPEndPoint(IPAddress.Loopback, 31413));
        var requestUri = new Uri(httpProxy.Add(new HttpProxyRegistryEntry()
        {
            Url = $"http://localhost:{redirectPort}/",
            SupportedMethods = ["GET"],
            ResponseHeaderOptions = new ResponseHeaderOptions()
            {
                InjectPermissiveCORS = false
            },
            FollowRedirects = false
        }));

        var portString = requestUri.IsDefaultPort ? "" : (":" + 31413);
        var inputRequest = $"""
        GET {requestUri.LocalPath} HTTP/1.1
        Host: {requestUri.Host}{portString}



        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(inputRequest));
        using var outputStream = new MemoryStream();

        var combinedStream = new DuplexStream(inputStream, outputStream, true);
        var session = new HttpProxySession(httpProxy, combinedStream, CancellationToken.None, s => { });

        session.Start();
        await Task.Delay(1000);

        var responseBytes = outputStream.ToArray();
        var responseString = Encoding.UTF8.GetString(responseBytes);

        Assert.AreEqual(redirectResponse.Replace("{{finalPort}}", finalPort.ToString()), responseString);
        Assert.IsNotNull(receivedRedirectRequest);
        Assert.IsNull(receivedFinalRequest);

        serverCancellationTokenSource.Cancel();
    }

    [TestMethod]
    public async Task TestIntegration_RelativeProxy_WithRedirect_FollowRedirects_True()
    {
        var finalResponse = $"""
        HTTP/1.1 200 OK
        Content-Type: text/plain

        Grayjay is the best app in the world
        """;

        HttpProxyRequest? receivedRedirectRequest = null;
        HttpProxyRequest? receivedFinalRequest = null;

        var serverCancellationTokenSource = new CancellationTokenSource();

        var redirectListener = new TcpListener(IPAddress.Loopback, 0);
        redirectListener.Start();
        var redirectPort = ((IPEndPoint)redirectListener.LocalEndpoint).Port;

        var finalListener = new TcpListener(IPAddress.Loopback, 0);
        finalListener.Start();
        var finalPort = ((IPEndPoint)finalListener.LocalEndpoint).Port;
        
        var redirectResponse = $"""
        HTTP/1.1 301 Moved Permanently
        Location: http://localhost:{finalPort}/test/somepath?q=3


        """;

        var redirectServerTask = StartMockServer(redirectListener, Encoding.UTF8.GetBytes(redirectResponse.Replace("{{finalPort}}", finalPort.ToString())), req => receivedRedirectRequest = req, serverCancellationTokenSource.Token);
        var finalServerTask = StartMockServer(finalListener, Encoding.UTF8.GetBytes(finalResponse), req => receivedFinalRequest = req, serverCancellationTokenSource.Token);

        var httpProxy = new HttpProxy(new IPEndPoint(IPAddress.Loopback, 31413));
        var requestUri = new Uri(httpProxy.Add(new HttpProxyRegistryEntry()
        {
            Url = $"http://localhost:{redirectPort}/",
            SupportedMethods = ["GET"],
            ResponseHeaderOptions = new ResponseHeaderOptions()
            {
                InjectPermissiveCORS = false
            },
            FollowRedirects = true,
            SupportRelativeProxy = true
        }));

        var portString = requestUri.IsDefaultPort ? "" : (":" + 31413);
        var inputRequest = $"""
        GET {requestUri.LocalPath} HTTP/1.1
        Host: {requestUri.Host}{portString}
        Content-Length: 34

        What is the best app in the world?
        """;

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(inputRequest));
        using var outputStream = new MemoryStream();

        var combinedStream = new DuplexStream(inputStream, outputStream, true);
        var session = new HttpProxySession(httpProxy, combinedStream, CancellationToken.None, s => { });

        session.Start();
        await Task.Delay(1000);

        var responseBytes = outputStream.ToArray();
        var responseString = Encoding.UTF8.GetString(responseBytes);

        var expectedResponse = $"""
        HTTP/1.1 200 OK
        Content-Type: text/plain

        Grayjay is the best app in the world
        """;

        Assert.AreEqual(expectedResponse, responseString);
        Assert.IsNotNull(receivedRedirectRequest);
        Assert.IsNotNull(receivedFinalRequest);
        Assert.AreEqual("/test/somepath?q=3", receivedFinalRequest.Path);
        Assert.AreEqual("GET", receivedFinalRequest.Method);
        Assert.AreEqual("localhost", receivedFinalRequest.Headers["Host"]);
        //TODO: This needs to be checked, referer should have something related to the previous request url (before redirect)
        //Assert.AreEqual($"http://localhost:{redirectPort}/test/somepath?q=3", receivedFinalRequest.Headers["Referer"]);
        Assert.AreEqual("http://localhost", receivedFinalRequest.Headers["origin"]);
        Assert.AreEqual("34", receivedFinalRequest.Headers["Content-Length"]);

        serverCancellationTokenSource.Cancel();
    }


    private static async Task StartMockServer(TcpListener listener, byte[] response, Action<HttpProxyRequest> onRequestReceived, CancellationToken cancellationToken)
    {
        try
        {
            var client = await listener.AcceptTcpClientAsync();
            using (var networkStream = client.GetStream())
            {
                using var httpStream = new HttpProxyStream(networkStream);
                var request = await httpStream.ReadRequestHeadersAsync();
                onRequestReceived(request);
                await networkStream.WriteAsync(response, 0, response.Length, cancellationToken);
            }
            client.Close();
        }
        catch (OperationCanceledException)
        {
            // Server is shutting down
        }
        finally
        {
            listener.Stop();
        }
    }

    public static void AssertDictionariesAreEqual<TKey, TValue>(IDictionary<TKey, TValue> expected, IDictionary<TKey, TValue> actual)
    {
        Assert.AreEqual(expected.Count, actual.Count, "Dictionary counts are not equal.");

        foreach (var kvp in expected)
        {
            if (!actual.TryGetValue(kvp.Key, out var actualValue) || !StringComparer.InvariantCultureIgnoreCase.Equals(kvp.Key, actual.Keys.First(k => StringComparer.InvariantCultureIgnoreCase.Equals(kvp.Key, k))))
                Assert.Fail($"Key '{kvp.Key}' not found in actual dictionary.");
            Assert.AreEqual(kvp.Value, actualValue, $"Value for key '{kvp.Key}' does not match.");
        }
    }

    private byte[] CompressGzip(string data, Encoding? encoding = null)
    {
        using var outputStream = new MemoryStream();
        using var gzipStream = new GZipStream(outputStream, CompressionMode.Compress);
        var bytes = (encoding ?? Encoding.UTF8).GetBytes(data);
        gzipStream.Write(bytes, 0, bytes.Length);
        gzipStream.Flush();
        return outputStream.ToArray();
    }

    private byte[] CompressDeflate(string data)
    {
        using var outputStream = new MemoryStream();
        using var deflateStream = new DeflateStream(outputStream, CompressionMode.Compress);
        using var writer = new StreamWriter(deflateStream);
        writer.Write(data);
        writer.Flush();
        deflateStream.Flush();
        return outputStream.ToArray();
    }

    private byte[] CompressBrotli(string data)
    {
        using var outputStream = new MemoryStream();
        using var brotliStream = new BrotliStream(outputStream, CompressionMode.Compress);
        using var writer = new StreamWriter(brotliStream);
        writer.Write(data);
        writer.Flush();
        brotliStream.Flush();
        return outputStream.ToArray();
    }

    public byte[] CompressZstd(string data)
    {
        var bytes = Encoding.UTF8.GetBytes(data);
        using var compressor = new ZstdNet.Compressor();
        return compressor.Wrap(bytes);
    }
}
