using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC;
using Grayjay.Engine;
using Grayjay.Engine.Setting;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Grayjay.ClientServer.Settings
{
    public class GrayjayDevSettings : SettingsInstanced<GrayjayDevSettings>
    {
        public override string FileName => "settings.json";

        [SettingsField("Developer Mode", SettingsField.TOGGLE, "Developer mode allows access to development features, may not be as secure.", 1)]
        public bool DeveloperMode { get; set; } = true;



    }

}
