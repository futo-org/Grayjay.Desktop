using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC;
using Grayjay.Engine;
using Grayjay.Engine.Setting;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Grayjay.ClientServer.Settings
{
    public class GrayjaySettings : SettingsInstanced<GrayjaySettings>
    {
        public override string FileName => "settings.json";



        //[SettingsField("Language", SettingsField.DROPDOWN, "", 0)]
        public LanguageSettings Language { get; set; } = new LanguageSettings();
        public class LanguageSettings
        {

            [SettingsField("App Language", SettingsField.DROPDOWN, "", 0)]
            [SettingsDropdownOptions("")]
            public int AppLanguage { get; set; } = 0;
        }

        //Home
        [SettingsField("Home", SettingsField.GROUP, "Configure how your Home tab works and feels", 1)]
        public HomeSettings Home { get; set; } = new HomeSettings();
        public class HomeSettings
        {
            [SettingsField("Preview Feed Items", SettingsField.TOGGLE, "When the preview feedstyle is used, if items should auto-preview when scrolling over them", 6)]
            public bool PreviewFeedItems { get; set; } = true;

            [SettingsField("Progress Bar", SettingsField.TOGGLE, "If a historical progress bar should be shown", 7)]
            public bool ProgressBar { get; set; } = true;

        }

        //Search
        [SettingsField("Search", "group", "", 2)]
        public SearchSettings Search { get; set; } = new SearchSettings();
        public class SearchSettings
        {
            [SettingsField("Search history", SettingsField.TOGGLE, "May require restart", 3)]
            public bool SearchHistory { get; set; } = true;

            [SettingsField("Preview Feed Items", SettingsField.TOGGLE, "When the previouw feedstyle is used, if items should automatically preview.", 5)]
            public bool PreviewFeedItems { get; set; } = true;

            [SettingsField("Progress Bar", SettingsField.TOGGLE, "If a historical progress bar should be shown", 6)]
            public bool ProgressBar { get; set; } = true;
        }

        [SettingsField("Channel", "group", "", 3)]
        public ChannelSettings Channel { get; set; } = new ChannelSettings();
        public class ChannelSettings
        {
            [SettingsField("Progress Bar", SettingsField.TOGGLE, "If a historical progress bar should be shown", 6)]
            public bool ProgressBar { get; set; } = true;
        }

        [SettingsField("Subscriptions", "group", "Configure how your subscriptions works and feels", 4)]
        public SubscriptionsSettings Subscriptions { get; set; } = new SubscriptionsSettings();
        public class SubscriptionsSettings
        {
            [SettingsField("Show Subscriptions Group", SettingsField.TOGGLE, "If subscription groups should be shown above your subscriptions", 5)]
            public bool ShowSubscriptionGroups { get; set; } = true;

            [SettingsField("Preview Feed Items", SettingsField.TOGGLE, "When the preview feedstyle is used, if items should automatically preview", 6)]
            public bool PreviewFeedItems { get; set; } = true;

            [SettingsField("Progress Bar", SettingsField.TOGGLE, "If a historical progress bar should be shown", 7)]
            public bool ProgressBar { get; set; } = true;

            [SettingsField("Fetch on app boot", SettingsField.TOGGLE, "Shortly after opening the app, start fetching subscriptions", 8)]
            public bool FetchOnAppBoot { get; set; } = true;

            [SettingsField("Fetch on tab open", SettingsField.TOGGLE, "Fetch new results when the tab is opened (if no results are present)", 9)]
            public bool FetchOnTabOpen { get; set; } = true;

            /*
            [SettingsField("Background Update", SettingsField.DROPDOWN, "Experimental background update for subscriptions cache", 10, "background_update")]
            [SettingsDropdownOptions("")]
            public int SubscriptionsBackgroundUpdateInterval { get; set; } = 0;

            public int GetSubscriptionsBackgroundIntervalMinutes()
            {
                return SubscriptionsBackgroundUpdateInterval switch
                {
                    0 => 0,
                    1 => 15,
                    2 => 60,
                    3 => 60 * 3,
                    4 => 60 * 6,
                    5 => 60 * 12,
                    6 => 60 * 24,
                    _ => 0
                };
            }
            */

            [SettingsField("Subscription concurrency", SettingsField.DROPDOWN, "Specify how many threads are used to fetch channels", 11)]
            [SettingsDropdownOptions("2", "4", "8", "12", "16", "20", "30")]
            public int SubscriptionConcurrency { get; set; } = 6;
            public int GetSubscriptionConcurrency()
            {
                return ThreadIndexToCount(SubscriptionConcurrency) * 2;
            }

            [SettingsField("Show Watch Metrics", SettingsField.TOGGLE, "Shows the watch time and views of each creator in the creators tab", 12)]
            public bool ShowWatchMetrics { get; set; } = false;

            [SettingsField("Track Playtime Locally", SettingsField.TOGGLE, "Locally track playtime of subscriptions, used for subscriptions.", 13)]
            public bool AllowPlaytimeTracking { get; set; } = true;

            [SettingsField("Always Reload From Cache", SettingsField.TOGGLE, "This is not recommended, but possible workaround for subscription issues", 14)]
            public bool AlwaysReloadFromCache { get; set; } = false;
        }

        [SettingsField("Player", "group", "Change behavior of the player", 5)]
        public PlaybackSettings Playback { get; set; } = new PlaybackSettings();
        public class PlaybackSettings
        {
            [SettingsField("Primary Language", SettingsField.DROPDOWN, "", 0)]
            [SettingsDropdownOptions("English")]
            public int PrimaryLanguage { get; set; } = 0;

            public string GetPrimaryLanguage()
            {
                return PrimaryLanguage switch
                {
                    0 => "en",
                    1 => "es",
                    2 => "de",
                    3 => "fr",
                    4 => "ja",
                    5 => "ko",
                    6 => "th",
                    7 => "vi",
                    8 => "id",
                    9 => "hi",
                    10 => "ar",
                    11 => "tu",
                    12 => "ru",
                    13 => "pt",
                    14 => "zh",
                    _ => null
                };
            }

            [SettingsField("Default Playback Speed", SettingsField.DROPDOWN, "", 1)]
            [SettingsDropdownOptions("0.25", "0.5", "0.75", "1.0", "1.25", "1.5", "1.75", "2.0", "2.25")]
            public int DefaultPlaybackSpeed { get; set; } = 3;

            public float GetDefaultPlaybackSpeed()
            {
                return DefaultPlaybackSpeed switch
                {
                    0 => 0.25f,
                    1 => 0.5f,
                    2 => 0.75f,
                    3 => 1.0f,
                    4 => 1.25f,
                    5 => 1.5f,
                    6 => 1.75f,
                    7 => 2.0f,
                    8 => 2.25f,
                    _ => 1.0f
                };
            }

            [SettingsField("Preferred Quality", SettingsField.DROPDOWN, "Default quality for watching a video", 2)]
            [SettingsDropdownOptions("Automatic (1080p)", "2160p", "1440p", "1080p", "720p", "480p", "360p", "240p", "144p")]
            public int PreferredQuality { get; set; } = 0;
            public int GetPreferredQualityPixelCount()
            {
                int height = QualityIndexToHeight(PreferredQuality);
                return (int)(height * (16 / (double)9)) * height;
            }


            /*
            [SettingsField("Preferred Preview Quality", SettingsField.DROPDOWN, "Default qaulity while previewing a video in a feed", 4)]
            [SettingsDropdownOptions("Automatic", "2160p", "1440p", "1080p", "720p", "480p", "360p", "240p", "144p")]
            public int PreferredPreviewQuality { get; set; } = 5;

            
            [SettingsField("Resume After Preview", SettingsField.DROPDOWN, "When watching a video in preview mode, resume at the position when opening the video.", 7)]
            [SettingsDropdownOptions("Start at Beginning", "Resume after 10s", "Always Resume")]
            public int ResumeAfterPreview { get; set; } = 1;

            public bool ShouldResumePreview(long previewedPosition)
            {
                if (ResumeAfterPreview == 2)
                    return true;
                if (ResumeAfterPreview == 1 && previewedPosition > 10)
                    return true;
                return false;
            }*/


            /*
            [SettingsField("Live Chat Webview", SettingsField.TOGGLE, "Use the live chat web window when available over the native window", 9)]
            public bool UseLiveChatWindow { get; set; } = true;
            */
        }

        /*
        [SettingsField("", "group", "", 6)]
        public CommentSettings Comments { get; set; } = new CommentSettings();
        public class CommentSettings
        {
            [SettingsField("Default Comment Section", SettingsField.DROPDOWN, "", 0)]
            [SettingsDropdownOptions("")]
            public int DefaultCommentSection { get; set; } = 0;

            [SettingsField("Bad Reputation Comments Fading", SettingsField.TOGGLE, "If a comment with a very bad reputation should be faded or not", 0)]
            public bool BadReputationCommentsFading { get; set; } = true;
        }
        */

        [SettingsField("Downloads", "group", "Configure downloading of videos", 7)]
        public DownloadSettings Downloads { get; set; } = new DownloadSettings();
        public class DownloadSettings
        {

            [SettingsField("Default Video Quality", SettingsField.DROPDOWN, "", 2)]
            [SettingsDropdownOptions("Automatic", "2160p", "1440p", "1080p", "720p", "480p", "360p", "240p", "144p")]
            public int PreferredVideoQuality { get; set; } = 3;


            [SettingsField("Default Audio Quality", SettingsField.DROPDOWN, "", 3)]
            [SettingsDropdownOptions("Low Bitrate", "High Bitrate")]
            public int PreferredAudioQuality { get; set; } = 1;

            public bool IsHighBitrateDefault() => PreferredAudioQuality > 0;

            [SettingsField("Byte Range Download", SettingsField.TOGGLE, "Attempt to utilize byte ranges", 4)]
            public bool ByteRangeDownload { get; set; } = true;

            [SettingsField("ByteRange Concurrency", SettingsField.DROPDOWN, "Number of concurrent threads to multiple download speed", 5)]
            [SettingsDropdownOptions("1", "2", "4", "6", "8", "10", "15")]
            public int ByteRangeConcurrency { get; set; } = 3;


            public int GetByteRangeThreadCount()
            {
                return ThreadIndexToCount(ByteRangeConcurrency);
            }
        }

        [SettingsField("Browsing", "group", "Configure browsing behavior", 8)]
        public BrowsingSettings Browsing { get; set; } = new BrowsingSettings();
        public class BrowsingSettings
        {
            [SettingsField("Enable Video Cache", SettingsField.TOGGLE, "Cache to quickly load previously fetched videos", 0)]
            public bool VideoCache { get; set; } = true;
        }

        [SettingsField("Casting", "group", "Configure casting", 9)]
        public CastingSettings Casting { get; set; } = new CastingSettings();
        public class CastingSettings
        {
            [SettingsField("Enabled", SettingsField.TOGGLE, "Enable casting", 0)]
            public bool Enabled { get; set; } = true;
        }
        

        [SettingsField("Logging", SettingsField.GROUP, "", 10)]
        public LoggingSettings Logging { get; set; } = new LoggingSettings();
        public class LoggingSettings
        {
            [SettingsField("Log Level", SettingsField.DROPDOWN, "", 0)]
            [SettingsDropdownOptions("None", "Error", "Warning", "Information", "Verbose")]
            public int LogLevel { get; set; } = 0;

        }

        [SettingsField("Synchronization", "group", "Configure synchronization", 11)]
        public SynchronizationSettings Synchronization { get; set; } = new SynchronizationSettings();
        public class SynchronizationSettings
        {
            [SettingsField("Enabled", SettingsField.TOGGLE, "Enable synchronization", 0)]
            public bool Enabled { get; set; } = true;

            [SettingsField("Broadcast", SettingsField.TOGGLE, "Allow device to broadcast presence", 1)]
            public bool Broadcast { get; set; } = true;

            [SettingsField("Connect Discovered", SettingsField.TOGGLE, "Allow device to search for and initiate connection with known paired devices", 2)]
            public bool ConnectDiscovered { get; set; } = true;

            [SettingsField("Connect Last", SettingsField.TOGGLE, "Allow device to automatically connect to last known", 3)]
            public bool ConnectLast { get; set; } = true;
        }

        [SettingsField("Info", SettingsField.GROUP, "", 13)]
        public InfoData Info { get; } = new InfoData();

        public class InfoData
        {
            [SettingsField("Version Code", SettingsField.READONLY, "", 1, "code")]
            public string versionCode { get; } = "2";

            [SettingsField("Verson Name", SettingsField.READONLY, "", 2)]
            public string versionName { get; } = "Desktop";

            [SettingsField("Version Type", SettingsField.READONLY, "", 3)]
            public string versionType { get; } = "stable";

            [SettingsField("Mode", SettingsField.READONLY, "", 9)]
            public string mode => (GrayjayServer.Instance?.ServerMode ?? false) ? "Server" : (GrayjayServer.Instance?.HeadlessMode ?? false) ? "Headless" : "UI";
        }



        private static int QualityIndexToHeight(int index)
        {
            return index switch
            {
                0 => 1080,
                1 => 2160,
                2 => 1440,
                3 => 1080,
                4 => 720,
                5 => 480,
                6 => 360,
                7 => 240,
                8 => 144,
                _ => 1080
            };
        }
        private static int ThreadIndexToCount(int index)
        {
            switch (index)
            {
                case 0: return 1;
                case 1: return 2;
                case 2: return 4;
                case 3: return 6;
                case 4: return 8;
                case 5: return 10;
                case 6: return 15;
                default: return 1;
            }
        }
    }

}
