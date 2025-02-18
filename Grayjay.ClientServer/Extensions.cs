using System.ComponentModel;
using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using Grayjay.ClientServer.Models;
using Grayjay.ClientServer.Pooling;
using Grayjay.Engine;
using Grayjay.Engine.Pagers;

namespace Grayjay.ClientServer
{
    public static class Extensions
    {
        public static GrayjayPlugin FromPool(this GrayjayPlugin client, PlatformMultiClientPool pool)
            => pool.GetClientPooled(client);


        public static PagerResult<T> AsPagerResult<T>(this IPager<T> pager)
        {
            var results = pager.GetResults();
            var hasMore = pager.HasMorePages();
            return new PagerResult<T>()
            {
                PagerID = pager.ID,
                Results = results,
                HasMore = hasMore
            };
        }
        public static PagerResult<R> AsPagerResult<T, R>(this IPager<T> pager, Func<T, bool> filter, Func<T, R> modifier)
        {
            var results = pager.GetResults().Where(filter).Select(modifier).ToArray();
            var hasMore = pager.HasMorePages();
            return new PagerResult<R>()
            {
                PagerID = pager.ID,
                Results = results,
                HasMore = hasMore
            };
        }
        public static PagerResult<R> AsPagerResult<T, R>(this IPager<T> pager, Func<T, R> modifier)
        {
            var results = pager.GetResults().Select(modifier).ToArray();
            var hasMore = pager.HasMorePages();
            return new PagerResult<R>()
            {
                PagerID = pager.ID,
                Results = results,
                HasMore = hasMore
            };
        }

        public static TimeSpan NowDiff(this DateTime current)
        {
            return DateTime.Now.Subtract(current);
        }

        public static async Task SkipAsync(this Stream stream, int size, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            int remaining = size;

            while (remaining > 0)
            {
                int toRead = Math.Min(buffer.Length, remaining);
                int read = await stream.ReadAsync(buffer, 0, toRead, cancellationToken);
                if (read == 0)
                {
                    throw new EndOfStreamException("End of stream reached before skip could be completed.");
                }
                remaining -= read;
            }
        }


        private const long CountInGbit = 1_000_000_000;
        private const long CountInMbit = 1_000_000;
        private const long CountInKbit = 1_000;

        public static string ToHumanBitrate(this int value) => ((long)value).ToHumanBitrate();
        public static string ToHumanBitrate(this long value)
        {
            long v = Math.Abs(value);
            if (v >= CountInGbit)
                return $"{value / CountInGbit}gbps";
            else if (v >= CountInMbit)
                return $"{value / CountInMbit}mbps";
            else if (v >= CountInKbit)
                return $"{value / CountInKbit}kbps";

            return $"{value}bps";
        }

        private static readonly string DecimalFormat = "0.00";

        public static string ToHumanBytesSpeed(this int value) => ((long)value).ToHumanBytesSpeed();
        public static string ToHumanBytesSpeed(this long value)
        {
            long v = Math.Abs(value);
            string formattedValue = value.ToString();
            if (v >= CountInGbit)
                formattedValue = $"{(value / (double)CountInGbit).ToString(DecimalFormat)}GB/s";
            else if (v >= CountInMbit)
                formattedValue = $"{(value / (double)CountInMbit).ToString(DecimalFormat)}MB/s";
            else if (v >= CountInKbit)
                formattedValue = $"{(value / (double)CountInKbit).ToString(DecimalFormat)}KB/s";

            return formattedValue;
        }

        public static string ToHumanBytesSize(this int value, bool withDecimal = true) => ((long)value).ToHumanBytesSize(withDecimal);
        public static string ToHumanBytesSize(this long value, bool withDecimal = true)
        {
            long v = Math.Abs(value);
            string formattedValue = value.ToString();

            if (withDecimal)
            {
                if (v >= CountInGbit)
                    formattedValue = $"{(value / (double)CountInGbit).ToString(DecimalFormat)}GB";
                else if (v >= CountInMbit)
                    formattedValue = $"{(value / (double)CountInMbit).ToString(DecimalFormat)}MB";
                else if (v >= CountInKbit)
                    formattedValue = $"{(value / (double)CountInKbit).ToString(DecimalFormat)}KB";
            }
            else
            {
                if (v >= CountInGbit)
                    formattedValue = $"{(value / (double)CountInGbit).ToString("0")}GB";
                else if (v >= CountInMbit)
                    formattedValue = $"{(value / (double)CountInMbit).ToString("0")}MB";
                else if (v >= CountInKbit)
                    formattedValue = $"{(value / (double)CountInKbit).ToString("0")}KB";
            }

            return formattedValue;
        }


        public static long DifferenceNowMinutes(this DateTime date)
        {
            return (long)DateTime.Now.Subtract(date).TotalMinutes;
        }
        public static long DifferenceNowSeconds(this DateTime date)
        {
            return (long)DateTime.Now.Subtract(date).TotalSeconds;
        }



        const string _allowedFileNameCharacters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz-.";
        public static string SanitizeFileName(this string fileName)
        {
            foreach(char invalidChar in fileName.Distinct().Where(x=>!_allowedFileNameCharacters.Contains(x)))
                fileName = fileName.Replace(invalidChar, '_');
            return fileName;
        }
        public static string SanitizeFileNameWithPath(this string path)
        {
            string dirName = Path.GetDirectoryName(path);
            string fileName = Path.GetFileName(path);

            foreach (char invalidChar in fileName.Distinct().Where(x => !_allowedFileNameCharacters.Contains(x)))
                fileName = fileName.Replace(invalidChar, '_');
            return Path.Combine(dirName, fileName);
        }


        public static string ToUrlAddress(this IPAddress address)
        {
            if (address.AddressFamily == AddressFamily.InterNetwork)
                return address.ToString();
            else if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv4MappedToIPv6)
                return address.MapToIPv4().ToString();
            else
                return $"[{address}]";
        }

        public static string EnsureAbsoluteUrl(this string path, Uri baseUri)
        {
            Uri? resultUri;

            if (Uri.TryCreate(path, UriKind.Absolute, out resultUri))
            {
                if ((resultUri.Scheme == Uri.UriSchemeHttp) || (resultUri.Scheme == Uri.UriSchemeHttps))
                    return path;
            }

            resultUri = new Uri(baseUri, path);
            return resultUri.ToString();
        }

        public static string EnsureAbsoluteUrl(this string path, string baseUrl)
        {
            Uri baseUri = new Uri(baseUrl);
            return path.EnsureAbsoluteUrl(baseUri);
        }

        public static string Capitalize(this string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            return char.ToUpper(value[0]) + value.Substring(1);
        }


        /*
        public static bool MatchesDomain(this string domain, string queryDomain)
        {
            if (queryDomain.StartsWith("."))
                return domain.EndsWith(queryDomain) || domain == queryDomain.TrimStart('.');
            else
                return domain == queryDomain;
        }*/








        public static string VideoContainerToExtension(this string container) {
            if (container.Contains("video/mp4") || container == "application/vnd.apple.mpegurl")
                return "mp4";
            else if (container.Contains("application/x-mpegURL"))
                return "m3u8";
            else if (container.Contains("video/3gpp"))
                return "3gp";
            else if (container.Contains("video/quicktime"))
                return "mov";
            else if (container.Contains("video/webm"))
                return "webm";
            else if (container.Contains("video/x-matroska"))
                return "mkv";
            else
                //throw new InvalidDataException("Could not determine container type for audio (" + container + ")");
            //else
                return "video";
        }

        public static string AudioContainerToExtension(this string container)
        {
            if (container.Contains("audio/mp4"))
                return "mp4a";
            else if (container.Contains("audio/mpeg"))
                return "mpga";
            else if (container.Contains("audio/mp3"))
                return "mp3";
            else if (container.Contains("audio/webm"))
                return "webm";
            else if (container == "application/vnd.apple.mpegurl")
                return "mp4";
            else
                //throw new InvalidDataException("Could not determine container type for audio (" + container + ")");
            //else
                return "audio";
        }
    }
}
