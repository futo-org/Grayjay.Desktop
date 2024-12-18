namespace Grayjay.ClientServer.Proxy
{
    public class HttpProxyResponse
    {
        public required int StatusCode;
        public required string Version;
        public required Dictionary<string, string> Headers;
        public byte[] Data = null;

        public byte[] ToBytes()
        {
            using var stream = new MemoryStream();
            using (var writer = new StreamWriter(stream))
            {
                writer.Write($"{Version} {StatusCode} {GetStatusCodeDescription(StatusCode)}\r\n");
                foreach (var header in Headers)
                    writer.Write($"{header.Key}: {header.Value}\r\n");
                writer.Write("\r\n");
            }
            return stream.ToArray();
        }


        public static HttpProxyResponse FromBytes(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes);
            using var streamReader = new StreamReader(stream);

            string? responseLine = streamReader.ReadLine();
            if (string.IsNullOrEmpty(responseLine))
                throw new Exception("Response line is empty.");

            var responseParts = responseLine.Split(' ');
            if (responseParts.Length < 3)
                throw new Exception("Invalid response line format.");

            var version = responseParts[0];
            var statusCode = int.Parse(responseParts[1]);

            var headers = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
            string? line;
            while ((line = streamReader.ReadLine()) != null && line != string.Empty)
            {
                var parts = line.Split([':', ' '], 2);
                if (parts.Length == 2)
                    headers[parts[0].Trim()] = parts[1].Trim();
            }

            return new HttpProxyResponse
            {
                StatusCode = statusCode,
                Headers = headers,
                Version = version
            };
        }

        public static string GetStatusCodeDescription(int statusCode)
        {
            return statusCode switch
            {
                100 => "Continue",
                101 => "Switching Protocols",
                102 => "Processing (WebDAV)",
                200 => "OK",
                201 => "Created",
                202 => "Accepted",
                203 => "Non-Authoritative Information",
                204 => "No Content",
                205 => "Reset Content",
                206 => "Partial Content",
                207 => "Multi-Status (WebDAV)",
                208 => "Already Reported (WebDAV)",
                226 => "IM Used",
                300 => "Multiple Choices",
                301 => "Moved Permanently",
                302 => "Found",
                303 => "See Other",
                304 => "Not Modified",
                305 => "Use Proxy",
                306 => "(Unused)",
                307 => "Temporary Redirect",
                308 => "Permanent Redirect (experimental)",
                400 => "Bad Request",
                401 => "Unauthorized",
                402 => "Payment Required",
                403 => "Forbidden",
                404 => "Not Found",
                405 => "Method Not Allowed",
                406 => "Not Acceptable",
                407 => "Proxy Authentication Required",
                408 => "Request Timeout",
                409 => "Conflict",
                410 => "Gone",
                411 => "Length Required",
                412 => "Precondition Failed",
                413 => "Request Entity =>o Large",
                414 => "Request-URI =>o Long",
                415 => "Unsupported Media Type",
                416 => "Requested Range Not Satisfiable",
                417 => "Expectation Failed",
                418 => "I'm a teapot (RFC 2324)",
                420 => "Enhance Your Calm (Twitter)",
                422 => "Unprocessable Entity (WebDAV)",
                423 => "Locked (WebDAV)",
                424 => "Failed Dependency (WebDAV)",
                425 => "Reserved for WebDAV",
                426 => "Upgrade Required",
                428 => "Precondition Required",
                429 => "Too Many Requests",
                431 => "Request Header Fields =>o Large",
                444 => "No Response (Nginx)",
                449 => "Retry With (Microsoft)",
                450 => "Blocked by Windows Parental Controls (Microsoft)",
                451 => "Unavailable For Legal Reasons",
                499 => "Client Closed Request (Nginx)",
                500 => "Internal Server Error",
                501 => "Not Implemented",
                502 => "Bad Gateway",
                503 => "Service Unavailable",
                504 => "Gateway Timeout",
                505 => "HTTP Version Not Supported",
                506 => "Variant Also Negotiates (Experimental)",
                507 => "Insufficient Storage (WebDAV)",
                508 => "Loop Detected (WebDAV)",
                509 => "Bandwidth Limit Exceeded (Apache)",
                510 => "Not Extended",
                511 => "Network Authentication Required",
                598 => "Network read timeout error",
                599 => "Network connect timeout error",
                _ => throw new NotImplementedException(),
            };
        }
    }
}