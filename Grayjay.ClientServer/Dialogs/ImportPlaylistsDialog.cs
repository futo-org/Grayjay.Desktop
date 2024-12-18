using Grayjay.ClientServer.Controllers;
using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine.Models.Channel;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Models.General;
using System.Text.Json;
using System.Threading.Channels;
using static Grayjay.ClientServer.Controllers.StateUI;

namespace Grayjay.ClientServer.Dialogs
{
    public class ImportPlaylistsDialog : RemoteDialog
    {
        public List<string> PlaylistUrls { get; set; } = new List<string>();
        public List<string> Selected { get; set; } = new List<string>();

        public List<PlatformPlaylistDetails> Playlists { get; set; } = new List<PlatformPlaylistDetails>();

        public bool IsLoading { get; set; }

        public int Total { get; set; }
        public int Loaded { get; set; }
        public int Failed { get; set; }

        public List<string> Exceptions { get; set; } = new List<string>();

        public ImportPlaylistsDialog(List<string> subs): base("importPlaylists")
        {
            PlaylistUrls = subs.Where(x => !StateSubscriptions.IsSubscribed(x)).ToList();
            Status = "selection";
            Total = PlaylistUrls.Count;
        }

        public async override Task Show()
        {
            await base.Show();

            int counter = 0;
            foreach(string sub in PlaylistUrls)
            {
                if (!IsOpen)
                    return;
                try
                {
                    var playlist = StatePlatform.GetPlaylist(sub);
                    Playlists.Add(playlist);
                    Selected = Selected.Concat(new string[] { playlist.Url }).Distinct().ToList();
                    Loaded++;
                    Update();
                }
                catch(Exception ex)
                {
                    Failed++;
                    Update();
                }
                if(counter > 99)
                {
                    if (counter == 100)
                        StateUI.Toast("Slowing down import to avoid ratelimits");
                    Thread.Sleep(800);
                }
                counter++;
            }
        }

        [DialogMethod("selectPlaylist")]
        public void Dialog_SelectPlaylist(CustomDialog dialog, JsonElement parameter)
        {
            string toSelect = parameter.GetString();

            var currentList = Selected;
            if (currentList.Contains(toSelect))
                Selected = currentList.Where(x => x != toSelect).ToList();
            else
                Selected = currentList.Concat(new string[] { toSelect }).ToList();
            Update();
        }
        [DialogMethod("selectAll")]
        public void Dialog_SelectAll(CustomDialog dialog, JsonElement parameter)
        {
            Selected = Playlists.Select(x => x.Url).ToList();
            Update();
        }
        [DialogMethod("deselectAll")]
        public void Dialog_DeselectAll(CustomDialog dialog, JsonElement parameter)
        {
            Selected = new List<string>();
            Update();
        }

        [DialogMethod("import")]
        public void Dialog_Import(CustomDialog dialog, JsonElement parameter)
        {
            var toImport = Playlists.Where(x => Selected.Any(y => x.Url == y)).ToList();

            foreach (var item in toImport)
            {
                StatePlaylists.CreateOrUpdate(Models.Playlist.FromPlaylistDetails(item), false);
            }
            Status = "finished";
            Update();
        }
    }
}
