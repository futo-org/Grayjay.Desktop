using Futo.PlatformPlayer.States;
using Google.Protobuf.Reflection;
using Grayjay.ClientServer.Database;
using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC;
using Grayjay.Engine;
using Grayjay.Engine.Exceptions;
using Grayjay.Engine.Models.Channel;
using Grayjay.Engine.Models.Comments;
using Grayjay.Engine.Models.Detail;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Pagers;
using Microsoft.ClearScript.V8;

using Logger = Grayjay.Desktop.POC.Logger;

namespace Grayjay.ClientServer.Developer
{
    public class DevGrayjayPlugin : GrayjayPlugin
    {
        public override string ID => StateDeveloper.DEV_ID;

        public string OriginalID { get; set; }
        public string DevID { get; set; }
        public string DevScript { get; set; }

        public DevGrayjayPlugin(PluginDescriptor descriptor, string originalId, string script, string? savedState = null, PluginHttpClient client = null, PluginHttpClient clientAuth = null, string devID = null) : base(descriptor, script, savedState, client, clientAuth)
        {
            OriginalID = originalId;
            DevScript = script;
            DevID = devID ?? Guid.NewGuid().ToString().Substring(0, 5);
            OnLog += (sender, msg) =>
            {
                StateDeveloper.Instance.LogDevInfo(DevID, msg);
            };
            OnScriptException += (sender, ex) =>
            {
                if (ex is ScriptCaptchaRequiredException capEx)
                {
                    Logger.Warning<DatabaseConnection>($"Captcha required: " + capEx.Message + "\n" + capEx.Url + "\n" + "Has Body: " + (capEx.Body != null).ToString());
                    StateApp.HandleCaptchaException(descriptor.Config, capEx);
                }
            };
        }

        public DevGrayjayPlugin(PluginConfig config, string originalId, string script, Dictionary<string, string?>? settings = null, string savedState = null, string devID = null) : base(config, script, settings, savedState)
        {
            OriginalID = originalId;
            DevScript = script;
            DevID = devID ?? Guid.NewGuid().ToString().Substring(0, 5);

            OnScriptException += (sender, ex) =>
            {
                if (ex is ScriptCaptchaRequiredException capEx)
                {
                    Logger.Warning<DatabaseConnection>($"Captcha required: " + capEx.Message + "\n" + capEx.Url + "\n" + "Has Body: " + (capEx.Body != null).ToString());
                    StateApp.HandleCaptchaException(config, capEx);
                }
            };
        }

        public override GrayjayPlugin GetCopy(bool privateCopy = false)
        {
            if(!privateCopy)
                return new DevGrayjayPlugin(Descriptor, OriginalID, DevScript, GetSavedState(), null, null, DevID);
            else
                return new DevGrayjayPlugin(Descriptor.Config, OriginalID, DevScript, Descriptor.Settings, GetSavedState(), DevID);
        }

        public override void Enable() => StateDeveloper.Instance.HandleDevCall(DevID, "enable", false, () =>
        {
            base.Enable();
            return true;
        });

        public override void Disable() => StateDeveloper.Instance.HandleDevCall(DevID, "disable", false, () =>
        {
            base.Disable();
            return true;
        });

        public override IPager<PlatformContent> GetHome() => StateDeveloper.Instance.HandleDevCall(DevID, "getHome", false, () =>
        {
            return base.GetHome();
        });

        public override List<string> SearchSuggestions(string query) => StateDeveloper.Instance.HandleDevCall(DevID, "searchSuggestions", false, () =>
        {
            return base.SearchSuggestions(query);
        });

        public override IPager<PlatformContent> Search(string query, string? type = null, string? order = null, Dictionary<string, string[]>? filters = null) => StateDeveloper.Instance.HandleDevCall(DevID, "search", false, () =>
        {
            return base.Search(query, type, order, filters);
        });

        public override bool IsChannelUrl(string url) => StateDeveloper.Instance.HandleDevCall(DevID, "isChannelUrl", false, () =>
        {
            return base.IsChannelUrl(url);
        });

        public override PlatformChannel GetChannel(string url) => StateDeveloper.Instance.HandleDevCall(DevID, "getChannel", false, () =>
        {
            return base.GetChannel(url);
        });

        public override IPager<PlatformContent> GetChannelContents(string channelUrl, string? type = null, string? order = null, Dictionary<string, List<string>>? filters = null) => StateDeveloper.Instance.HandleDevCall(DevID, "getChannelContents", false, () =>
        {
            return base.GetChannelContents(channelUrl, type, order, filters);
        });

        public override bool IsContentDetailsUrl(string url) => StateDeveloper.Instance.HandleDevCall(DevID, "isContentDetailsUrl", false, () =>
        {
            return base.IsContentDetailsUrl(url);
        });

        public override IPlatformContentDetails GetContentDetails(string url) => StateDeveloper.Instance.HandleDevCall(DevID, "getContentDetails", false, () =>
        {
            return base.GetContentDetails(url);
        });

        public override IPager<PlatformComment> GetComments(string url) => StateDeveloper.Instance.HandleDevCall(DevID, "getComments", false, () =>
        {
            return base.GetComments(url);
        });

        public override IPager<PlatformComment> GetSubComments(PlatformComment comment) => StateDeveloper.Instance.HandleDevCall(DevID, "getSubComments", false, () =>
        {
            return base.GetSubComments(comment);
        });

        public override IPager<PlatformContent> SearchPlaylists(string query) => StateDeveloper.Instance.HandleDevCall(DevID, "searchPlaylists", false, () =>
        {
            return base.SearchPlaylists(query);
        });
    }
}
