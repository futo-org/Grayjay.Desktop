using System.Text;
using Grayjay.Desktop.POC;

namespace Grayjay.ClientServer.Proxy
{
    public class HttpProxyRequest
    {
        public required string Method;
        public required string Path;
        public string QueryString;
        public required string Version;
        public required Dictionary<string, string> Headers;

        public byte[] ToBytes()
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.Append($"{Method} {Path} {Version}\r\n");
            foreach (var header in Headers)
                stringBuilder.Append($"{header.Key}: {header.Value}\r\n");
            stringBuilder.Append("\r\n");

            var request = stringBuilder.ToString();
            return Encoding.UTF8.GetBytes(request);
        }

        public static HttpProxyRequest FromBytes(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes);
            using var streamReader = new StreamReader(stream);

            string? requestLine = streamReader.ReadLine();
            if (string.IsNullOrEmpty(requestLine))
                throw new Exception("Request line is empty.");

            var requestParts = requestLine.Split(' ');
            if (requestParts.Length < 3)
                throw new Exception("Invalid request line format.");

            var method = requestParts[0];
            var path = requestParts[1];
            var version = requestParts[2];

            var headers = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
            string? line;
            while ((line = streamReader.ReadLine()) != null && line != string.Empty)
            {
                var parts = line.Split([':', ' '], 2);
                if (parts.Length == 2)
                    headers[parts[0].Trim()] = parts[1].Trim();
            }

            return new HttpProxyRequest
            {
                Method = method,
                Path = path,
                Headers = headers,
                Version = version
            };
        }
    }
}