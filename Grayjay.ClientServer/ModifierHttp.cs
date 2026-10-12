using System.IO.Compression;
using Grayjay.Engine.Models;
using Grayjay.Engine.Web;
using Grayjay.Engine.Models.Video.Additions;
using Grayjay.Engine.Packages;
using Grayjay.ClientServer.Helpers;

namespace Grayjay.ClientServer
{
    public static partial class ModifierHttp
    {
        public readonly record struct BytesResult(string FinalUrl, int Code, byte[] Bytes)
        {
            public bool IsOk => Code >= 200 && Code < 300;
        }

        public readonly record struct BoundedBytesResult(int Code, byte[] Bytes, bool LimitExceeded)
        {
            public bool IsOk => Code >= 200 && Code < 300;
        }

        public readonly record struct StreamResult(string FinalUrl, int Code, long ContentLength, Stream Stream)
        {
            public bool IsOk => Code >= 200 && Code < 300;
            public HttpHeaders? Headers { get; init; }
        }

        private static List<KeyValuePair<string, string>> ToHeaderList(HttpHeaders headers)
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (var h in headers)
            {
                if (!string.IsNullOrEmpty(h.Key) && h.Value != null)
                    list.Add(new KeyValuePair<string, string>(h.Key, h.Value));
            }
            return list;
        }

        // Libcurl bodies arrive decoded, so decodeContent only applies to the managed client.
        public static BytesResult GetBytes(
            ManagedHttpClient client,
            string url,
            IRequestModifier? modifier = null,
            HttpHeaders? headers = null,
            bool decodeContent = false,
            CancellationToken cancel = default,
            TimeSpan? readIdleTimeout = null)
            => SendBytes(client, "GET", url, null, modifier, headers, decodeContent, cancel, readIdleTimeout);

        public static BytesResult PostBytes(
            ManagedHttpClient client,
            string url,
            byte[] body,
            IRequestModifier? modifier = null,
            HttpHeaders? headers = null)
            => SendBytes(client, "POST", url, body, modifier, headers, true, default, null);

        private static BytesResult SendBytes(
            ManagedHttpClient client,
            string method,
            string url,
            byte[]? body,
            IRequestModifier? modifier,
            HttpHeaders? headers,
            bool decodeContent,
            CancellationToken cancel,
            TimeSpan? readIdleTimeout)
        {
            headers ??= new HttpHeaders();
            var modified = modifier?.ModifyRequest(url, headers);

            var finalUrl = modified?.Url ?? url;
            var finalHeaders = modified?.Headers ?? headers;
            var impersonate = modified?.Options?.ImpersonateTarget;

            if (!string.IsNullOrEmpty(impersonate))
            {
                var res = PerformImpersonated(new Libcurl.Request
                {
                    Url = finalUrl,
                    Method = method,
                    Headers = ToHeaderList(finalHeaders),
                    Body = body,
                    ImpersonateTarget = impersonate
                }, cancel);
                if (res.EffectiveUrl != null)
                    finalUrl = res.EffectiveUrl;

                var code = Convert.ToInt32(res.Status);
                return new BytesResult(finalUrl, code, res.BodyBytes ?? Array.Empty<byte>());
            }

            var resp = (body != null)
                ? client.Request(method, finalUrl, body, finalHeaders)
                : client.GET(finalUrl, finalHeaders);
            if (resp.Url != null)
                finalUrl = resp.Url;

            if (resp.Body == null)
                return new BytesResult(finalUrl, resp.Code, Array.Empty<byte>());

            byte[] responseBody;
            if (readIdleTimeout.HasValue)
            {
                using var bodyStream = new IdleTimeoutStream(resp.Body.AsStream(), readIdleTimeout.Value, cancel);
                using var buffered = new MemoryStream();
                bodyStream.CopyTo(buffered);
                responseBody = buffered.ToArray();
            }
            else
            {
                responseBody = resp.Body.AsBytes();
            }
            if (decodeContent && resp.Headers != null)
                responseBody = DecodeContent(resp.Headers, responseBody);
            return new BytesResult(finalUrl, resp.Code, responseBody);
        }

        public static BoundedBytesResult GetBytesBounded(
            ManagedHttpClient client,
            string url,
            IRequestModifier? modifier,
            HttpHeaders? headers,
            long maxBytes)
        {
            var res = GetStream(client, url, modifier, headers);
            using var responseStream = res.Stream;

            if (res.ContentLength > maxBytes)
                return new BoundedBytesResult(res.Code, Array.Empty<byte>(), true);

            using var buffered = new MemoryStream();
            var chunk = new byte[64 * 1024];
            long total = 0;
            while (true)
            {
                int read = responseStream.Read(chunk, 0, chunk.Length);
                if (read <= 0)
                    break;

                total += read;
                if (total > maxBytes)
                    return new BoundedBytesResult(res.Code, Array.Empty<byte>(), true);

                buffered.Write(chunk, 0, read);
            }

            return new BoundedBytesResult(res.Code, buffered.ToArray(), false);
        }

        public static byte[] DecodeContent(HttpHeaders headers, byte[] body)
        {
            var codings = headers.GetAll("content-encoding")
                .SelectMany(value => value.Split(','))
                .Select(part => part.Trim().ToLowerInvariant())
                .ToList();
            if (body.Length == 0 || codings.Count == 0)
                return body;

            codings.Reverse();
            foreach (var coding in codings)
            {
                switch (coding)
                {
                    case "identity":
                    case "":
                        break;
                    case "gzip":
                    case "x-gzip":
                        body = Decompress(body, input => new GZipStream(input, CompressionMode.Decompress));
                        break;
                    case "deflate":
                        // Servers send both zlib-wrapped (RFC 1950) and raw deflate.
                        body = HasZlibHeader(body)
                            ? Decompress(body, input => new ZLibStream(input, CompressionMode.Decompress))
                            : Decompress(body, input => new DeflateStream(input, CompressionMode.Decompress));
                        break;
                    case "br":
                        body = Decompress(body, input => new BrotliStream(input, CompressionMode.Decompress));
                        break;
                    case "zstd":
                        // The streaming decoder also accepts frames that omit the content size.
                        body = Decompress(body, input => new ZstdNet.DecompressionStream(input));
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported content encoding: {coding}");
                }
            }
            return body;
        }

        private static bool HasZlibHeader(byte[] body)
        {
            if (body.Length < 2)
                return false;
            var compressionMethodAndFlags = body[0];
            var flags = body[1];
            return (compressionMethodAndFlags & 0x0F) == 8
                && (compressionMethodAndFlags >> 4) <= 7
                && ((compressionMethodAndFlags << 8) | flags) % 31 == 0;
        }

        private static byte[] Decompress(byte[] data, Func<Stream, Stream> createDecoder)
        {
            using var input = new MemoryStream(data);
            using var decoder = createDecoder(input);
            using var output = new MemoryStream();
            decoder.CopyTo(output);
            return output.ToArray();
        }

        public static StreamResult GetStream(
            ManagedHttpClient client,
            string url,
            IRequestModifier? modifier = null,
            HttpHeaders? headers = null,
            CancellationToken cancel = default,
            TimeSpan? readIdleTimeout = null)
        {
            headers ??= new HttpHeaders();
            var modified = modifier?.ModifyRequest(url, headers);

            var finalUrl = modified?.Url ?? url;
            var finalHeaders = modified?.Headers ?? headers;
            var impersonate = modified?.Options?.ImpersonateTarget;

            if (!string.IsNullOrEmpty(impersonate))
            {
                var res = PerformImpersonated(new Libcurl.Request
                {
                    Url = finalUrl,
                    Method = "GET",
                    Headers = ToHeaderList(finalHeaders),
                    ImpersonateTarget = impersonate
                }, cancel);

                var bytes = res.BodyBytes ?? Array.Empty<byte>();
                var code = Convert.ToInt32(res.Status);
                return new StreamResult(finalUrl, code, bytes.LongLength, new MemoryStream(bytes, writable: false)) { Headers = new HttpHeaders(res.Headers) };
            }

            var resp = client.GET(finalUrl, finalHeaders);
            var contentLength = ReadContentLength(resp.Headers);
            if (resp.Body == null)
                return new StreamResult(finalUrl, resp.Code, contentLength, Stream.Null);

            var bodyStream = readIdleTimeout.HasValue
                ? new IdleTimeoutStream(resp.Body.AsStream(), readIdleTimeout.Value, cancel)
                : resp.Body.AsStream();
            return new StreamResult(finalUrl, resp.Code, contentLength, bodyStream) { Headers = resp.Headers };
        }

        //Libcurl cannot be interrupted, so a cancelled caller stops waiting and the transfer ends within Request.TimeoutMs
        internal static Libcurl.Response PerformImpersonated(Libcurl.Request request, CancellationToken cancel, Func<Libcurl.Request, Libcurl.Response>? perform = null)
        {
            perform ??= Libcurl.Perform;
            cancel.ThrowIfCancellationRequested();
            if (!cancel.CanBeCanceled)
                return perform(request);
            return Task.Run(() => perform(request)).WaitAsync(cancel).GetAwaiter().GetResult();
        }

        private static long ReadContentLength(HttpHeaders headers)
        {
            if (headers.TryGetFirst("content-length", out var value) && long.TryParse(value, out var contentLength))
                return contentLength;
            return 0;
        }
    }
}
