using System.Net;
using System.Text;

namespace Grayjay.ClientServer.Proxy
{
    public class RequestHeaderOptions
    {
        public bool InjectHost = true;
        public bool InjectOrigin = true;
        public bool InjectReferer = true;
        public Dictionary<string, string> HeadersToInject = new Dictionary<string, string>();
    }

    public class ResponseHeaderOptions
    {
        public bool InjectPermissiveCORS = true;
        public Dictionary<string, string> HeadersToInject = new Dictionary<string, string>();
    }

    public class HttpProxyRegistryEntry
    {
        public Guid Id { get; set; }
        public string Url { get; set; }
        public bool IsRelative { get; set; }
        public RequestHeaderOptions RequestHeaderOptions { get; set; } = new();
        public ResponseHeaderOptions ResponseHeaderOptions { get; set; } = new();
        /// <summary>
        /// Return a modifier when the response body is to be modified, else return null
        /// </summary>
        public Func<HttpProxyRequest, HttpProxyResponse> RequestExecutor { get; set; }
        public Func<HttpProxyResponse, Func<byte[], byte[]>?>? ResponseModifier { get; set; } = null;
        public string[]? SupportedMethods { get; set; } = null;
        public bool FollowRedirects { get; set; } = true;
        public bool SupportRelativeProxy { get; set; } = false;

        public HttpProxyRegistryEntry WithModifyResponseString(Func<HttpProxyResponse, string, string> modifier)
        {
            ResponseModifier = (resp) => 
            {
                Encoding encoding = Encoding.UTF8;                
                if (resp.Headers.TryGetValue("content-type", out var contentType))
                {
                    try
                    {
                        var contentTypeHeader = new System.Net.Mime.ContentType(contentType);
                        if (!string.IsNullOrEmpty(contentTypeHeader.CharSet))
                            encoding = Encoding.GetEncoding(contentTypeHeader.CharSet);
                    }
                    catch (ArgumentException)
                    {
                        // Handle invalid encoding by falling back to UTF-8
                        encoding = Encoding.UTF8;
                    }
                }

                return (bodyBytes) =>
                {
                    return encoding.GetBytes(modifier(resp, encoding.GetString(bodyBytes)));
                };
            };

            return this;
        }
    }

    public struct ProxySettings
    {
        public readonly bool IsLoopback;
        public readonly bool ShouldProxy;
        public readonly bool ExposeLocalAsAny;
        public readonly IPAddress? ProxyAddress;

        public ProxySettings(bool isLoopback = true, bool shouldProxy = true, IPAddress? proxyAddress = null, bool exposeLocalAsAny = false)
        {
            IsLoopback = isLoopback;
            ShouldProxy = shouldProxy;
            ProxyAddress = proxyAddress;
            ExposeLocalAsAny = exposeLocalAsAny;
        }
    }
}