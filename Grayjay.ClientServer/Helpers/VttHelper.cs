using System.Text;
using System.Text.RegularExpressions;

namespace Grayjay.ClientServer.Helpers
{
    public static class VttHelper
    {
        private static readonly Regex _tagRegex = new Regex("<(/?)([^\\s>./]*)[^>]*>", RegexOptions.Compiled);
        private static readonly HashSet<string> _supportedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "b", "i", "u", "c", "v", "lang", "ruby", "rt" };

        public static bool IsVtt(string? contentType, byte[] bytes)
        {
            if (contentType != null && contentType.Contains("vtt", StringComparison.OrdinalIgnoreCase))
                return true;
            var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            return bytes.Length - offset >= 6 && Encoding.ASCII.GetString(bytes, offset, 6) == "WEBVTT";
        }

        public static byte[] StripUnsupportedTags(byte[] bytes)
        {
            var text = Encoding.UTF8.GetString(bytes);
            if (!text.Contains('<'))
                return bytes;

            var lines = text.Split('\n');
            var changed = false;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!line.Contains('<') || line.Contains("-->"))
                    continue;
                var cleaned = _tagRegex.Replace(line, m =>
                {
                    var name = m.Groups[2].Value;
                    if (m.Groups[1].Value.Length == 0 && m.Value.Length > 1 && char.IsDigit(m.Value[1]))
                        return m.Value;
                    return _supportedTags.Contains(name) ? m.Value : "";
                });
                if (cleaned != line)
                {
                    lines[i] = cleaned;
                    changed = true;
                }
            }
            return changed ? Encoding.UTF8.GetBytes(string.Join('\n', lines)) : bytes;
        }
    }
}
