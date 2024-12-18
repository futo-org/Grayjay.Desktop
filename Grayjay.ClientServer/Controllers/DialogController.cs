using Grayjay.ClientServer.Dialogs;
using Grayjay.ClientServer.Settings;
using Grayjay.ClientServer.States;
using Grayjay.ClientServer.Store;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine;
using Grayjay.Engine.Setting;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics.Tracing;
using System.Dynamic;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Grayjay.ClientServer.Controllers.StateUI;
using static Grayjay.ClientServer.States.StateBackup;
using static System.Formats.Asn1.AsnWriter;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class DialogController : ControllerBase
    {

        [HttpPost]
        public bool DialogRespond(string id, [FromBody]DialogResponse resp)
        {
            return StateUI.RespondDialog(id, resp);
        }

        [HttpPost]
        public bool DialogRespondCustom(string id, string actionName, [FromBody] JsonElement obj)
        {
            return StateUI.RespondDialogCustom(id, actionName, obj);
        }

        [HttpGet]
        public async Task Test()
        {

            var export = new ExportStructure(new Dictionary<string, string>(){}, 
                "test", 
                new Dictionary<string, List<string>>()
                {
                    { "subscriptions", new List<string>(){ "", "", "", "" } },
                    { "playlists", new List<string>(){ "", "", "" } },
                    { "watch_later", new List<string>(){ "", "", "" } }
                }, 
                new Dictionary<string, string>()
                {
                    { "a", "b" },
                    { "b", "b" }
                }, 
                new Dictionary<string, Dictionary<string, string>>()
                {
                    { "a", new Dictionary<string, string>(){ {"a", "1"}, { "b", "1" } } },
                    { "b", new Dictionary<string, string>(){ {"a", "1"}, { "b", "1" } } },
                    { "c", new Dictionary<string, string>(){ {"a", "1"}, { "b", "1" } } }
                });
            var import = new ImportStructureDialog(export);
            await import.Start();

            /*
            int progress = 1;
            int total = 10;



            dynamic model = new ExpandoObject();
            model.storeName = "Playlists";
            model.settings = new
            {
                value = false,
                progress = 0
            };
            model.plugins = new
            {
                count = 5,
                value = false,
                progress = 0
            };
            model.pluginSettings = new
            {
                count = 10,
                value = false,
                progress = 0
            };
            model.stores = new[]
            {
                new
                {
                    id = "subscriptions",
                    name = "Subscriptions",
                    count = 153,
                    value = false,
                    progress = 0
                },
                new
                {
                    id = "playlists",
                    name = "Playlists",
                    count = 7,
                    value = false,
                    progress = 0
                },
                new
                {
                    id = "watch_later",
                    name = "Watch later",
                    count = 23,
                    value = false,
                    progress = 0
                }
            };
            model.status = "choice";
            model.progress = 0;
            model.total = 0;
            
            var dialog = await StateUI.DialogCustom("import", model, new Dictionary<string, Action<CustomDialog, JsonElement>>()
            {
                { "choice", (dialog, obj) =>
                {
                    string parameter = obj.GetString();
                    string[] parts = parameter.Split(";");

                    switch (parts[0])
                    {
                        case "import":
                            model.status = "importing";
                            string[] toImport = parts[1].Split(",");
                            dialog.UpdateData(model);

                            model.settings.progress++;
                            dialog.UpdateData(model);
                            Thread.Sleep(800);

                            for(int i = 0; i < model.plugins.count; i++)
                            {
                                model.plugins.progress++;
                                dialog.UpdateData(model);
                                Thread.Sleep(800);
                            }
                            for(int i = 0; i < model.pluginSettings.count; i++)
                            {
                                model.pluginSettings.progress++;
                                dialog.UpdateData(model);
                                Thread.Sleep(800);
                            }


                            foreach(var store in model.stores)
                            {
                                for(int i = 0; i < store.count; i++)
                                {
                                    store.progress++;
                                    dialog.UpdateData(model);
                                    Thread.Sleep(800);
                                }
                            }
                            break;
                         case "cancel":
                            dialog.Close();
                            break;
                    }
                }},
                { "close", (dialog, obj) =>
                {

                }}
            });
            */
        }
    }
}
