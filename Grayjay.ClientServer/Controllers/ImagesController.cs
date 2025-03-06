using Grayjay.ClientServer.Settings;
using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine;
using Grayjay.Engine.Setting;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class ImagesController : ControllerBase
    {
        private static string[] _allowedExtensions = new[] { ".png", ".jpg", ".webp" }; 
        private static DirectoryInfo _imageDirectory = StateApp.GetAppDirectory().CreateSubdirectory("custom_images");

        [HttpGet]
        public string[] Images()
        {
            return _imageDirectory.GetFiles()
                .Where(x => _allowedExtensions.Contains(x.Extension.ToLower()))
                .OrderByDescending(x=>x.LastWriteTime)
                .Select(x => "/Images/Image?name=" + x.Name.SanitizeFileName())
                .ToArray();
        }

        [HttpGet]
        public IActionResult Image(string name)
        {
            string fileName = name.SanitizeFileName();
            string path = Path.Combine(_imageDirectory.FullName, fileName);

            if (System.IO.File.Exists(path))
                return File(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), "image/png");
            return NoContent();
        }

        [HttpGet]
        public async Task<IActionResult> ImageSubscription(string subUrl)
        {
            var sub = StateSubscriptions.GetSubscription(subUrl);
            if (sub == null)
                return NotFound();
            return await CachePassthrough(sub.Channel.Thumbnail);
        }


        [HttpPost]
        public async Task<IActionResult> ImageUpload()
        {
            if (Request.HasFormContentType)
            {
                var formData = await Request.ReadFormAsync();
                if (!formData.Files.Any())
                    return BadRequest();
                var file = formData.Files[0];
                string name = file.FileName.SanitizeFileName();
                if (string.IsNullOrEmpty(name))
                    name = Guid.NewGuid().ToString() + ".png";
                using (FileStream str = new FileStream(Path.Combine(_imageDirectory.FullName, name), FileMode.Create, FileAccess.Write, FileShare.Delete))
                {
                    using (var formFileStream = file.OpenReadStream())
                        formFileStream.CopyTo(str);
                }
                return Ok(JsonSerializer.Serialize(name));
            }
            else
                return BadRequest();
        }

        [HttpGet]
        public IActionResult GetCachedImage(string id)
        {
            var imagePath = StateImages.GetImagePath(id);
            if (imagePath != null)
                return PhysicalFile(imagePath, "image/png");
            return null;
        }

        [HttpGet]
        public async Task<IActionResult> CachePassthrough(string url)
        {
            string path = await StateImages.StoreImageUrlOrKeepPassthrough(url);
            Logger.i(nameof(ImagesController), $"Loading image from cache for [{url}]");
            return PhysicalFile(path, "image/png");
        }
    }
}
