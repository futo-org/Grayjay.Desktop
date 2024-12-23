using Grayjay.ClientServer.Dialogs;
using Grayjay.ClientServer.Exceptions;
using Grayjay.ClientServer.Models.Downloads;
using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine.Models.Detail;
using Grayjay.Engine.Models.General;
using Grayjay.Engine.Models.Subtitles;
using Grayjay.Engine.Models.Video;
using Grayjay.Engine.Models.Video.Sources;
using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;
using PlatformID = Grayjay.Engine.Models.General.PlatformID;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class ImportController : ControllerBase
    {

        [HttpGet]
        public List<string> GetUserSubscriptions(string id)
        {
            return StatePlatform.GetUserSubscriptions(id);
        }
        [HttpGet]
        public List<string> GetUserPlaylists(string id)
        {
            return StatePlatform.GetUserPlaylists(id);
        }


        [HttpGet]
        public async Task<bool> ImportZip()
        {
            if (GrayjayServer.Instance.ServerMode)
                throw DialogException.FromException("Import not supported in server-mode", new Exception("For import support, run the application in ui mode, server support might be added at a later time"));

            var file = await GrayjayServer.Instance.GetWindowProviderOrThrow().ShowFileDialogAsync([ 
                ("Zip (*.zip)", "*.zip") 
            ]);

            if (!string.IsNullOrEmpty(file))
            {
                using (FileStream str = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                using (ZipArchive archive = new ZipArchive(str)) 
                {
                    var export = StateBackup.ExportStructure.FromZip(archive);
                    StateBackup.Import(export);
                }
            }
            return true;
        }


        [HttpPost]
        public async Task<bool> ImportSubscriptions([FromBody]List<string> urls)
        {
            urls = urls.Distinct().ToList();
            var dialog = new ImportSubscriptionsDialog(urls);
            dialog.Show();
            return true;
        }
        [HttpPost]
        public async Task<bool> ImportPlaylists([FromBody] List<string> urls)
        {
            urls = urls.Distinct().ToList();
            var dialog = new ImportPlaylistsDialog(urls);
            dialog.Show();
            return true;
        }

        [HttpGet]
        public async Task<bool> ImportPlaylists(string id)
        {
            return false;
        }

    }
}
