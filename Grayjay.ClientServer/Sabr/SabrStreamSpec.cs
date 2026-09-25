using Grayjay.ClientServer.Sabr.Proto;
using Grayjay.Engine.Models.Video.Additions;
using Grayjay.Engine.Models.Video.Sources;

namespace Grayjay.ClientServer.Sabr
{
    public class SabrStreamSpec
    {
        public required Func<HttpClient> HttpClientFactory { get; init; }
        public bool OwnsHttpClient { get; init; } = true;
        public required string ServerAbrStreamingUrl { get; init; }
        public required byte[] UstreamerConfig { get; init; }
        public required string VideoId { get; init; }
        public bool IsLive { get; init; }
        public long DurationUs { get; init; }
        public required UMPFormat[] VideoFormats { get; init; }
        public required UMPFormat[] AudioFormats { get; init; }
        public string? PoToken { get; init; }
        public Func<bool, string?>? PoTokenRefresher { get; init; }
        public IRequestModifier? RequestModifier { get; init; }
        public int ClientName { get; init; } = 1;
        public string ClientVersion { get; init; } = "";
        public string OsName { get; init; } = "";
        public string OsVersion { get; init; } = "";

        public ClientInfo BuildClientInfo() => new ClientInfo()
        {
            ClientName = ClientName,
            ClientVersion = ClientVersion,
            OsName = OsName,
            OsVersion = OsVersion
        };

        public SabrSession CreateSession() => new SabrSession(
            HttpClientFactory(),
            ServerAbrStreamingUrl,
            UstreamerConfig,
            VideoId,
            BuildClientInfo(),
            PoToken,
            IsLive,
            DurationUs,
            OwnsHttpClient,
            RequestModifier,
            PoTokenRefresher);

        public static HttpClient CreateDefaultHttpClient()
        {
            var handler = new SocketsHttpHandler()
            {
                AutomaticDecompression = System.Net.DecompressionMethods.None,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                ConnectTimeout = TimeSpan.FromSeconds(15)
            };
            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMilliseconds(SABR_CALL_TIMEOUT_MS)
            };
        }

        public const long SABR_CALL_TIMEOUT_MS = 60_000;
        public const string DEFAULT_USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; rv:91.0) Gecko/20100101 Firefox/91.0";

        public static SabrStreamSpec FromSource(UMPSource source, Func<HttpClient>? httpClientFactory = null, UMPFormat[]? videoFormats = null, UMPFormat[]? audioFormats = null)
        {
            return new SabrStreamSpec()
            {
                HttpClientFactory = httpClientFactory ?? CreateDefaultHttpClient,
                OwnsHttpClient = httpClientFactory == null,
                ServerAbrStreamingUrl = source.Url,
                UstreamerConfig = DecodeBase64Url(source.UstreamerConfig),
                VideoId = source.VideoId ?? "",
                IsLive = source.IsLive,
                DurationUs = source.Duration > 0 ? source.Duration * 1_000_000L : -1L,
                VideoFormats = videoFormats ?? source.VideoFormats,
                AudioFormats = audioFormats ?? source.AudioFormats,
                PoToken = source.PoToken,
                PoTokenRefresher = source.HasGetPoToken ? source.GetPoToken : null,
                RequestModifier = source.HasRequestModifier ? source.GetRequestModifier() : null,
                ClientName = source.ClientName,
                ClientVersion = source.ClientVersion,
                OsName = source.OsName,
                OsVersion = source.OsVersion
            };
        }

        public static byte[] DecodeBase64Url(string value)
        {
            var s = value.Replace('-', '+').Replace('_', '/').Trim();
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }

        public static byte[]? DecodeBase64Lenient(string value)
        {
            try
            {
                return DecodeBase64Url(value);
            }
            catch
            {
                return null;
            }
        }
    }
}
