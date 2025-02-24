using Noise;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;

namespace Grayjay.ClientServer.States
{
    public static class StateImages
    {
        private static DirectoryInfo _directory;
        private static Regex _regex = new Regex(".*?/[Ii]mages/[Gg]et[Cc]ached[Ii]mage\\?id=[a-zA-Z0-9\\-]");
        
        static StateImages()
        {
            string path = Path.Combine(StateApp.GetAppDirectory().FullName, "imageCache");
            if(!Directory.Exists(path))
                Directory.CreateDirectory(path);

            _directory = new DirectoryInfo(path);
        }

        private static string GetFilePath(string id)
        {
            return Path.Combine(_directory.FullName, id);
        }

        public static bool HasImage(string id)
        {
            string path = GetFilePath(id);
            if (File.Exists(path))
                return true;
            return false;
        }
        public static string GetImagePath(string id)
        {
            string path = GetFilePath(id);
            if (File.Exists(path))
                return path;
            return null;
        }
        public static string GetImageUrl(string id)
        {
            if (id == null)
                return null;
            if(HasImage(id))
                return "/Images/GetCachedImage?id=" + HttpUtility.UrlEncode(id);
            return null;
        }

        public static bool IsImageUrl(string url)
        {
            if (url == null)
                return false;
            return _regex.IsMatch(url);
        }

        public static async Task<string> StoreImageUrl(string url, string id = null)
        {
            using (HttpClient client = new HttpClient())
            {
                id = id ?? Guid.NewGuid().ToString();
                string path = GetFilePath(id);
                byte[] buffer = new byte[4096];
                using (Stream str = await client.GetStreamAsync(url))
                using (FileStream fstr = new FileStream(path, FileMode.Create))
                {
                    int read = 0;
                    do
                    {
                        read = await str.ReadAsync(buffer, 0, buffer.Length);
                        if (read > 0)
                            await fstr.WriteAsync(buffer, 0, read);
                    }
                    while (read > 0);
                }
                return id;
            }
        }
        public static async Task<string> StoreImageUrlOrKeep(string url, string imageId = null)
        {
            if (IsImageUrl(url))
                return url;
            var id = await StoreImageUrl(url, imageId);
            if (id == null)
                return url;
            return GetImageUrl(id) ?? url;
        }


        public static async Task<string> StoreImageUrlOrKeepPassthrough(string url)
        {
            string urlId = SimpleUrlHash(url.SanitizeFileName());

            string existingPath = GetImagePath(urlId);
            if (existingPath != null)
                return existingPath;
            else
            {
                string id = await StoreImageUrl(url, urlId);
                if (id != null)
                    return GetImagePath(id);
                else
                    return url;
            }
        }


        private static string SimpleUrlHash(string url)
        {
            UInt64 hashedValue = 3074457345618258791ul;
            for (int i = 0; i < url.Length; i++)
            {
                hashedValue += url[i];
                hashedValue *= 3074457345618258799ul;
            }
            return hashedValue.ToString();
        }
    }
}
