using Grayjay.ClientServer.Controllers;
using Grayjay.ClientServer.Models;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine.Models.Channel;
using Grayjay.Engine.Models.General;
using System.Text.Json;
using static Grayjay.ClientServer.Controllers.StateUI;

namespace Grayjay.ClientServer.Dialogs
{
    public class ImportSubscriptionsDialog : RemoteDialog
    {
        public List<string> Subscriptions { get; set; } = new List<string>();
        public List<string> Selected { get; set; } = new List<string>();

        public List<PlatformChannel> Channels { get; set; } = new List<PlatformChannel>();

        public bool IsLoading { get; set; }

        public int Total { get; set; }
        public int Loaded { get; set; }
        public int Failed { get; set; }

        public List<string> Exceptions { get; set; } = new List<string>();

        public ImportSubscriptionsDialog(List<string> subs): base("importSubs")
        {
            Subscriptions = subs.Where(x => !StateSubscriptions.IsSubscribed(x)).ToList();
            Status = "selection";
            Total = Subscriptions.Count;
        }

        public async override Task Show()
        {
            await base.Show();

            int counter = 0;
            foreach(string sub in Subscriptions)
            {
                if (!IsOpen)
                    return;
                try
                {
                    var channel = StatePlatform.GetChannel(sub);
                    Channels.Add(channel);
                    Selected = Selected.Concat(new string[] { channel.Url }).Distinct().ToList();
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

        [DialogMethod("selectSubscription")]
        public void Dialog_SelectSubscription(CustomDialog dialog, JsonElement parameter)
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
            Selected = Channels.Select(x => x.Url).ToList();
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
            var toImport = Channels.Where(x => Selected.Any(y => x.IsSameUrl(y))).ToList();

            foreach(var item in toImport)
            {
                StateSubscriptions.AddSubscription(item);
            }
            Status = "finished";
            Update();
        }
    }
}
