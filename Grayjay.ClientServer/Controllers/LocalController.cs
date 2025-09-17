using Grayjay.Desktop.POC;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class LocalController : ControllerBase
    {
        [HttpGet]
        public IActionResult Open(string uri)
        {
            if (string.IsNullOrEmpty(uri))
                throw new BadHttpRequestException("Missing uri");

            try
            {
                Process.Start(uri);
            }
            catch
            {
                // hack because of this: https://github.com/dotnet/corefx/issues/10361
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    uri = uri.Replace("&", "^&");
                    Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", uri);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", uri);
                }
                else
                {
                    throw;
                }
            }
            return Ok();
        }

        public record class QuickAccessRow
        {
            [JsonPropertyName("name")]
            public required string Name { get; init; }
            [JsonPropertyName("path")]
            public required string Path { get; init; }
            [JsonPropertyName("type")]
            public required string Type { get; init; }
        }

        [HttpGet]
        public ActionResult<QuickAccessRow> DefaultPath()
        {
            return Ok(GetQuickAccessRows().FirstOrDefault());
        }

        private static List<QuickAccessRow> GetQuickAccessRows()
        {
            var rows = new List<QuickAccessRow>();
            static void AddIfExists(List<QuickAccessRow> list, string name, string path, string type)
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                if (path.Contains(':') && path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(new QuickAccessRow { Name = name, Path = path, Type = type });
                }
                else if (Directory.Exists(path))
                {
                    list.Add(new QuickAccessRow { Name = name, Path = path, Type = type });
                }
            }

            static void AddFolder(List<QuickAccessRow> list, string name, Environment.SpecialFolder sf, string type)
                => AddIfExists(list, name, Environment.GetFolderPath(sf), type);

            static string Home()
                => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                AddFolder(rows, "Desktop", Environment.SpecialFolder.Desktop, "desktop");
                AddFolder(rows, "Documents", Environment.SpecialFolder.MyDocuments, "documents");
                AddIfExists(rows, "Downloads", Path.Combine(Home(), "Downloads"), "downloads");
                AddFolder(rows, "Music", Environment.SpecialFolder.MyMusic, "music");
                AddFolder(rows, "Pictures", Environment.SpecialFolder.MyPictures, "pictures");
                AddFolder(rows, "Videos", Environment.SpecialFolder.MyVideos, "videos");
                AddFolder(rows, "Windows", Environment.SpecialFolder.Windows, "folder");
                AddFolder(rows, "Program Files", Environment.SpecialFolder.ProgramFiles, "folder");
                AddFolder(rows, "Program Files (x86)", Environment.SpecialFolder.ProgramFilesX86, "folder");

                foreach (var d in DriveInfo.GetDrives())
                {
                    if ((d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable) && d.IsReady)
                    {
                        var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.Name.TrimEnd('\\') : $"{d.VolumeLabel} ({d.Name.TrimEnd('\\')})";
                        AddIfExists(rows, label, d.RootDirectory.FullName, "volume");
                    }
                }
            }
            // LINUX
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var home = Home();
                var xdg = ReadXdgUserDirs(home);
                AddIfExists(rows, "Home", home, "home");
                AddIfExists(rows, "Desktop", xdg("XDG_DESKTOP_DIR", "Desktop"), "desktop");
                AddIfExists(rows, "Documents", xdg("XDG_DOCUMENTS_DIR", "Documents"), "documents");
                AddIfExists(rows, "Downloads", xdg("XDG_DOWNLOAD_DIR", "Downloads"), "downloads");
                AddIfExists(rows, "Music", xdg("XDG_MUSIC_DIR", "Music"), "music");
                AddIfExists(rows, "Pictures", xdg("XDG_PICTURES_DIR", "Pictures"), "pictures");
                AddIfExists(rows, "Videos", xdg("XDG_VIDEOS_DIR", "Videos"), "videos");
                AddIfExists(rows, "Public", xdg("XDG_PUBLICSHARE_DIR", "Public"), "folder");
                AddIfExists(rows, "Templates", xdg("XDG_TEMPLATES_DIR", "Templates"), "folder");
                AddIfExists(rows, "File System", "/", "volume");
            }
            // macOS
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var home = Home();

                AddIfExists(rows, "Home", home, "home");
                AddFolder(rows, "Desktop", Environment.SpecialFolder.Desktop, "desktop");
                AddFolder(rows, "Documents", Environment.SpecialFolder.MyDocuments, "documents");
                AddIfExists(rows, "Downloads", Path.Combine(home, "Downloads"), "downloads");
                AddFolder(rows, "Music", Environment.SpecialFolder.MyMusic, "music");
                AddFolder(rows, "Pictures", Environment.SpecialFolder.MyPictures, "pictures");
                AddFolder(rows, "Videos", Environment.SpecialFolder.MyVideos, "videos");
                AddIfExists(rows, "Applications", "/Applications", "folder");
                AddIfExists(rows, "Volumes", "/Volumes", "folder");
                AddIfExists(rows, "System Applications", "/System/Applications", "folder");
            }
            else
            {
                throw new Exception("Not a valid platform.");
            }
            return rows;
        }

        [HttpGet]
        public ActionResult<IEnumerable<QuickAccessRow>> QuickAccess()
        {
            return Ok(GetQuickAccessRows());
        }

        private static Func<string, string, string> ReadXdgUserDirs(string home)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>())
            {
                var key = kv.Key?.ToString();
                var val = kv.Value?.ToString();
                if (key is not null && val is not null && key.StartsWith("XDG_") && key.EndsWith("_DIR"))
                {
                    map[key] = NormalizeXdgPath(val, home);
                }
            }

            var cfg = Path.Combine(home, ".config", "user-dirs.dirs");
            if (System.IO.File.Exists(cfg))
            {
                var rx = new Regex(@"^\s*(XDG_[A-Z_]+_DIR)\s*=\s*""([^""]+)""\s*$");
                foreach (var line in System.IO.File.ReadLines(cfg))
                {
                    var m = rx.Match(line);
                    if (m.Success)
                    {
                        var key = m.Groups[1].Value;
                        var val = NormalizeXdgPath(m.Groups[2].Value, home);
                        if (!map.ContainsKey(key))
                            map[key] = val;
                    }
                }
            }

            return (key, fallback) =>
            {
                if (map.TryGetValue(key, out var p) && !string.IsNullOrWhiteSpace(p))
                    return p;
                return Path.Combine(home, fallback);
            };

            static string NormalizeXdgPath(string value, string home)
            {
                var v = value.Replace("$HOME", home, StringComparison.Ordinal);
                if (v.StartsWith("~")) v = Path.Combine(home, v.TrimStart('~').TrimStart('/'));
                return v;
            }
        }


        public record class FileRow
        {
            [JsonPropertyName("name")]
            public required string Name { get; init; }
            [JsonPropertyName("path")]
            public required string Path { get; init; }
            [JsonPropertyName("date")]
            public required string Date { get; init; }
            [JsonPropertyName("type")]
            public required string Type { get; init; }
        }

        [HttpGet]
        public ActionResult<IEnumerable<FileRow>> List(string path, string? q = null, bool includeHidden = false, bool includeFiles = true, bool dirsFirst = true, int offset = 0, int limit = 1000, string? filter = null)
        {
            if (!Directory.Exists(path))
                return NotFound("Directory not found.");

            var nameFilter = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
            var globPredicate = CompileGlobPredicate(filter);
            var rows = new List<FileRow>(capacity: 256);

            IEnumerable<string> dirEnum;
            try { dirEnum = Directory.EnumerateDirectories(path); }
            catch (Exception ex) { return Problem($"Unable to enumerate directories: {ex.Message}"); }

            foreach (var d in dirEnum)
            {
                try
                {
                    var name = System.IO.Path.GetFileName(d);
                    if (string.IsNullOrEmpty(name)) name = d.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

                    if (!includeHidden && IsHidden(d, isDirectory: true)) continue;
                    if (nameFilter is not null && !name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)) continue;

                    rows.Add(new FileRow
                    {
                        Name = name,
                        Path = d,
                        Date = FormatTimestamp(Directory.GetLastWriteTime(d)),
                        Type = "folder"
                    });
                }
                catch
                {
                    // Skip directories we can't touch
                }
            }

            if (includeFiles)
            {
                IEnumerable<string> fileEnum;
                try { fileEnum = Directory.EnumerateFiles(path); }
                catch (Exception ex) { return Problem($"Unable to enumerate files: {ex.Message}"); }

                foreach (var f in fileEnum)
                {
                    try
                    {
                        var name = System.IO.Path.GetFileName(f);
                        if (!includeHidden && IsHidden(f, isDirectory: false)) continue;
                        if (nameFilter is not null && !name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!MatchesFilters(name, globPredicate)) continue;

                        rows.Add(new FileRow
                        {
                            Name = name,
                            Path = f,
                            Date = FormatTimestamp(System.IO.File.GetLastWriteTime(f)),
                            Type = "file"
                        });
                    }
                    catch
                    {
                        // Skip unreadable files
                    }
                }
            }

            if (dirsFirst)
                {
                    rows = rows
                        .OrderBy(r => r.Type == "file")
                        .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                else
                {
                    rows = rows
                        .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

            if (offset < 0) offset = 0;
            if (limit <= 0) limit = 1000;

            var paged = rows.Skip(offset).Take(limit).ToList();
            return Ok(paged);
        }

        private static bool MatchesFilters(string fileName, Func<string, bool>? globPredicate)
        {
            if (globPredicate is null) return true;
            return globPredicate(fileName);
        }

        private static Func<string, bool>? CompileGlobPredicate(string? filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return null;
            var parts = filter.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return null;

            var regexes = new List<Regex>(parts.Length);
            foreach (var raw in parts)
            {
                var p = raw.Trim().Trim('"');
                if (p.Length == 0) continue;
                var escaped = Regex.Escape(p)
                    .Replace(@"\*", ".*")
                    .Replace(@"\?", ".");
                var pattern = $"^{escaped}$";
                Logger.i("LocalController", $"CompileGlobPredicate {filter} {raw} {pattern}");
                regexes.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled));
            }

            if (regexes.Count == 0) return null;

            return fileName =>
            {
                foreach (var rx in regexes)
                {
                    if (rx.IsMatch(fileName)) return true;
                }
                return false;
            };
        }

        [HttpGet]
        public ActionResult<FileRow> Stat(string path)
        {
            if (Directory.Exists(path))
            {
                var name = SafeNameForDirectory(path);
                return Ok(new FileRow
                {
                    Name = name,
                    Path = path,
                    Date = FormatTimestamp(Directory.GetLastWriteTime(path)),
                    Type = "folder"
                });
            }

            if (System.IO.File.Exists(path))
            {
                var name = System.IO.Path.GetFileName(path);
                return Ok(new FileRow
                {
                    Name = name,
                    Path = path,
                    Date = FormatTimestamp(System.IO.File.GetLastWriteTime(path)),
                    Type = "file"
                });
            }

            return NotFound("Path not found.");
        }

        [HttpGet]
        public ActionResult<string?> Parent(string path)
        {
            try
            {
                var parent = Directory.GetParent(path)?.FullName;
                return Ok(parent);
            }
            catch (Exception ex)
            {
                return Problem($"Unable to get parent: {ex.Message}");
            }
        }

        private static HashSet<string>? ParseExtensions(string? csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return null;
            var parts = csv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return null;
            return new HashSet<string>(parts.Select(p => p.TrimStart('.')), StringComparer.OrdinalIgnoreCase);
        }

        private static string FormatTimestamp(DateTime dt)
        {
            return dt.ToString("g", CultureInfo.CurrentCulture);
        }

        private static string SafeNameForDirectory(string fullPath)
        {
            var name = System.IO.Path.GetFileName(fullPath);
            if (!string.IsNullOrEmpty(name)) return name;
            return fullPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }

        private static bool IsHidden(string path, bool isDirectory)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var attrs = System.IO.File.GetAttributes(path);
                    return attrs.HasFlag(FileAttributes.Hidden) || attrs.HasFlag(FileAttributes.System);
                }
                else
                {
                    var name = isDirectory ? System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar)) 
                                           : System.IO.Path.GetFileName(path);
                    return name.StartsWith(".", StringComparison.Ordinal);
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
