using Grayjay.ClientServer.Store;
using Grayjay.Desktop.POC;
using Newtonsoft.Json;
using System.Net;
using System.Text.Json.Serialization;

namespace Grayjay.ClientServer.States
{
    public class StateTelemetry
    {
        private static StringStore _id = new StringStore("id", null).Load();

        static StateTelemetry()
        {
            if(_id.Value == null)
            {
                _id.Save(Guid.NewGuid().ToString());
            }
        }

        public static void Upload()
        {
            var tel = new Telemtry()
            {
                Id = _id.Value,
                ApplicationId = "Grayjay.Desktop",
                VersionName = StateApp.VersionName,
                VersionCode = StateApp.VersionCode.ToString(),
                BuildType = "",
                Debug = false,
                IsUnstableBuild = false,
                Platform = StateApp.GetPlatformName(),
                Manufacturer = StateApp.GetPlatformName(),
                Brand = "",
                Model = "",
                SdkVersion = -1,
            };
            try
            {
                using (WebClient client = new WebClient())
                {
                    client.Headers.Add("Content-Type", "application/json");
                    var result = client.UploadString("https://logs.grayjay.app/telemetry", System.Text.Json.JsonSerializer.Serialize(tel));
                }
            }
            catch(Exception ex)
            {
                Logger.w(nameof(StateTelemetry), "Failed to submit launch telemtry");
            }
        }
    }

    public class Telemtry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
        [JsonPropertyName("applicationId")]
        public string ApplicationId { get; set; }
        [JsonPropertyName("versionCode")]
        public string VersionCode { get; set; }
        [JsonPropertyName("versionName")]
        public string VersionName { get; set; }
        [JsonPropertyName("buildType")]
        public string BuildType { get; set; }
        [JsonPropertyName("debug")]
        public bool Debug { get; set; }
        [JsonPropertyName("isUnstableBuild")]
        public bool IsUnstableBuild { get; set; }
        [JsonPropertyName("brand")]
        public string Brand { get; set; }
        [JsonPropertyName("manufacturer")]
        public string Manufacturer { get; set; }
        [JsonPropertyName("model")]
        public string Model { get; set; }
        [JsonPropertyName("sdkVersion")]
        public int SdkVersion { get; set; }
        [JsonPropertyName("platform")]
        public string Platform { get; set; }
    }
}
