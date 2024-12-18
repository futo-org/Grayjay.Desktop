using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine;
using Grayjay.Engine.Models.Capabilities;
using static Grayjay.Engine.GrayjayPlugin;

namespace Grayjay.ClientServer.Models.Sources
{
    public class PluginConfigState
    {
        public PluginConfig Config { get; set; }

        public PlatformClientCapabilities Capabilities { get; set; }

        public ResultCapabilities CapabilitiesChannel { get; set; }
        public ResultCapabilities CapabilitiesSearch { get; set; }

        public bool HasLoggedIn { get; set; }
        public bool HasCaptcha { get; set; }

        public bool HasUpdate { get; set; }

        public static PluginConfigState FromClient(GrayjayPlugin plugin)
        {
            return new PluginConfigState()
            {
                Config = plugin.Config,
                Capabilities = plugin.Capabilities,
                CapabilitiesChannel = plugin.GetChannelCapabilities(),
                CapabilitiesSearch = plugin.GetSearchCapabilities(),
                HasLoggedIn = plugin.Descriptor.HasLoggedIn,
                HasCaptcha = plugin.Descriptor.HasCaptcha,
                HasUpdate = StatePlugins.HasUpdate(plugin.ID)
            };
        }
    }
}
