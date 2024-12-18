using System.Text.Json.Nodes;
using Grayjay.ClientServer.Settings;
using Grayjay.ClientServer.States;
using Grayjay.ClientServer.Store;
using Grayjay.Desktop.POC;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine;
using Grayjay.Engine.Setting;
using Microsoft.AspNetCore.Mvc;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class SettingsController : ControllerBase
    {
        private static DictionaryStore<string, string> _uiPersistence = new DictionaryStore<string, string>("uiPersistence", new Dictionary<string, string>()).Load();

        //UI Persistence
        [HttpGet]
        public ActionResult<JsonNode?> PersistGet(string key)
        {
            var data = _uiPersistence.GetValue(key, null);
            if (data == null)
                return Ok("null");
            return Ok(JsonNode.Parse(data));
        }

        [HttpPost]
        public ActionResult PersistSet(string key, [FromBody] JsonNode value)
        {
            _uiPersistence.SetValue(key, value.ToString());
            _uiPersistence.SaveThis();
            return Ok();
        }

        //Global settingsS
        public SettingsObject<GrayjaySettings> Settings()
            => GrayjaySettings.Instance.GetSettingsObject();
        public bool SettingsSave([FromBody]GrayjaySettings settings)
        {
            if (settings == null)
                return false;
            settings.Replace();
            Logger.i(nameof(SettingsController), "Saved settings");
            StateWebsocket.SettingsChanged(GrayjaySettings.Instance);
            return true;
        }


        //Source app settings
        public SettingsObject<PluginAppSettings> SourceAppSettings(string id)
            => StatePlugins.GetPlugin(id)?.AppSettings?.GetSettingsObject(id);

        [HttpPost]
        public bool SourceAppSettingsSave(string id, [FromBody]PluginAppSettings settings)
        {
            if (settings == null)
                return false;
            PluginDescriptor descriptor = StatePlugins.GetPlugin(id);
            descriptor.AppSettings = settings;
            StatePlugins.UpdatePlugin(id);
            StateUI.Toast($"Saved [{descriptor.Config.Name}] app settings");
            return true;
        }


        //Source-defined settings
        public SettingsObject<Dictionary<string, string>> SourceSettings(string id)
            => StatePlugins.GetPlugin(id)?.GetSettingsObject();

        [HttpPost]
        public bool SourceSettingsSave(string id, [FromBody] Dictionary<string, object> settings)
        {
            if (settings == null)
                return false;
            PluginDescriptor descriptor = StatePlugins.GetPlugin(id);
            descriptor.Settings = settings.ToDictionary(x=>x.Key, y=>y.Value?.ToString() ?? "");
            StatePlugins.UpdatePlugin(id);
            StateUI.Toast($"Saved [{descriptor.Config.Name}] plugin settings");
            return true;
        }

        [HttpGet]
        public bool SubscriptionGroupsDismiss()
        {
            GrayjaySettings.Instance.Subscriptions.ShowSubscriptionGroups = false;
            GrayjaySettings.Instance.Save();
            Logger.i(nameof(SettingsController), "Saved settings");
            StateWebsocket.SettingsChanged(GrayjaySettings.Instance);
            return true;
        }

    }
}
