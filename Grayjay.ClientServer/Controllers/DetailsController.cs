using Grayjay.ClientServer.Browser;
using Grayjay.ClientServer.Database.Indexes;
using Grayjay.ClientServer.Exceptions;
using Grayjay.ClientServer.Helpers;
using Grayjay.ClientServer.LiveChat;
using Grayjay.ClientServer.Models;
using Grayjay.ClientServer.Models.Downloads;
using Grayjay.ClientServer.Pagers;
using Grayjay.ClientServer.Proxy;
using Grayjay.ClientServer.Settings;
using Grayjay.ClientServer.States;
using Grayjay.ClientServer.Subscriptions;
using Grayjay.Desktop.POC;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine.Dash;
using Grayjay.Engine.Exceptions;
using Grayjay.Engine.Models.Comments;
using Grayjay.Engine.Models.Detail;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Models.Live;
using Grayjay.Engine.Models.Playback;
using Grayjay.Engine.Models.Subtitles;
using Grayjay.Engine.Models.Video;
using Grayjay.Engine.Models.Video.Additions;
using Grayjay.Engine.Models.Video.Sources;
using Grayjay.Engine.Pagers;
using Grayjay.Engine.V8;
using Grayjay.Engine.Web;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml.Linq;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class DetailsController : ControllerBase
    {
        public class DetailsState : IDisposable
        {
            public PlatformPostDetails PostLoaded { get; set; }
            public PlatformVideoDetails VideoLoaded { get; set; }
            public VideoLocal VideoLocal { get; set; }
            public Subscription VideoSubscription { get; set; }
            public DBHistoryIndex VideoHistoryIndex { get; set; }
            public PlaybackTracker VideoPlaybackTracker { get; set; }

            public HttpProxyRegistryEntry _liveChatProxy = null;

            public RequestExecutor _videoRequestExecutor = null;
            public RequestExecutor _audioRequestExecutor = null;

            public long _lastWatchPosition = 0;
            public DateTime _lastWatchPositionChange = DateTime.MinValue;
            public LiveChatManager? LiveChatManager { get; set; }
            private object _cachedDashLockObject = new object();
            public int CachedDashVideoIndex = -1;
            public int CachedDashAudioIndex = -1;
            public int CachedDashSubtitleIndex = -1;
            public ProxySettings? CachedDashProxySettings = null;
            public Task<string>? CachedDashTask = null;

            //TODO: Either remove static or include window id somehow for cleanup.
            public static ConcurrentDictionary<string, IRequestModifier> Modifiers = new ConcurrentDictionary<string, IRequestModifier>();

            public RefPager<PlatformComment> CommentPager { get; set; }
            public ConcurrentDictionary<string, RefPager<PlatformComment>> RepliesPagers { get; set; } = new ConcurrentDictionary<string, RefPager<PlatformComment>>();


            public IPager<PlatformContent> RecommendationPager { get; set; }

            public string RegisterModifier(IRequestModifier modifier)
            {
                var id = Guid.NewGuid().ToString();
                Modifiers.AddOrUpdate(id, modifier, (a,b)=>modifier);
                return id;
            }

            public void ClearCachedDash()
            {
                lock (_cachedDashLockObject)
                {
                    CachedDashAudioIndex = -1;
                    CachedDashVideoIndex = 1;
                    CachedDashTask = null;
                    CachedDashProxySettings = null;
                }
            }

            public Task<string>? GetCachedDashTask(int videoIndex, int audioIndex, int subtitleIndex, ProxySettings? proxySettings)
            {
                lock (_cachedDashLockObject)
                {
                    if (CachedDashVideoIndex == videoIndex && CachedDashAudioIndex == audioIndex && CachedDashSubtitleIndex == subtitleIndex && Equals(CachedDashProxySettings, proxySettings))
                        return CachedDashTask;
                    return null;
                }
            }

            public void SetCachedDash(int videoIndex, int audioIndex, int subtitleIndex, ProxySettings? proxySettings, Task<string> dash)
            {
                lock (_cachedDashLockObject)
                {
                    CachedDashVideoIndex = videoIndex;
                    CachedDashAudioIndex = audioIndex;
                    CachedDashSubtitleIndex = subtitleIndex;
                    CachedDashTask = dash;
                    CachedDashProxySettings = proxySettings;
                }
            }

            public void Dispose()
            {
                LiveChatManager?.Stop();
                LiveChatManager = null;
            }
        }

        static ManagedHttpClient _qualityClient = new ManagedHttpClient();

        private void ChangeVideo(PlatformVideoDetails video, VideoLocal videoLocal)
        {
            var state = this.State().DetailsState;
            video = video ?? videoLocal;
            state.ClearCachedDash();
            state.VideoLoaded = video;
            state.VideoLocal = videoLocal;
            state.VideoSubscription = StateSubscriptions.GetSubscription(video?.Author?.Url ?? videoLocal?.Author?.Url);
            state.VideoHistoryIndex = video != null ? StateHistory.GetHistoryByVideo(video, true) : null;
            state.VideoPlaybackTracker?.onConcluded();
            try
            {
                state.VideoPlaybackTracker = video != null ? StatePlatform.GetPlaybackTracker(video.Url) : null;
            }
            catch (Exception ex)
            {
                state.VideoPlaybackTracker = null;
                Logger.e(nameof(DetailsController), "Failed to get Playback tracker", ex);
            }
            state._lastWatchPositionChange = DateTime.MinValue;
            state._lastWatchPosition = 0;

            state.LiveChatManager?.Stop();
            state.LiveChatManager = null;
            StateWebsocket.LiveEventsClear();
            if (video != null)
            {
                if (video.IsLive)
                {
                    try
                    {
                        var livePager = StatePlatform.GetLiveEvents(video.Url);
                        if (livePager != null)
                        {
                            state.LiveChatManager = new LiveChatManager(livePager);
                            state.LiveChatManager?.Follow(this, (liveEvents) =>
                            {
                                if (liveEvents.Count > 0)
                                    StateWebsocket.LiveEvents(liveEvents);
                            });
                            state.LiveChatManager?.Start();
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error<DetailsController>("Failed to retrieve live chat events", e);
                        state.LiveChatManager?.Stop();
                        state.LiveChatManager = null;
                        StateWebsocket.LiveEvents(new List<PlatformLiveEvent>()
                    {
                        new LiveEventComment()
                        {
                            ColorName = "#FF0000",
                            Message = "Failed to load live stream because of an error: " + e.Message,
                            Name = "SYSTEM"
                        }
                    });
                    }
                }
                else if(video != null && video.HasVODEvents())
                {
                    try
                    {
                        var livePager = video.GetVODEvents();
                        if (livePager != null)
                        {
                            state.LiveChatManager = new LiveChatManager(livePager);
                            state.LiveChatManager?.Follow(this, (liveEvents) =>
                            {
                                if (liveEvents.Count > 0)
                                    StateWebsocket.LiveEvents(liveEvents);
                            });
                            state.LiveChatManager?.Start();
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error<DetailsController>("Failed to retrieve vod chat events", e);
                        state.LiveChatManager?.Stop();
                        state.LiveChatManager = null;
                        StateWebsocket.LiveEvents(new List<PlatformLiveEvent>()
                    {
                        new LiveEventComment()
                        {
                            ColorName = "#FF0000",
                            Message = "Failed to load vod chat because of an error: " + e.Message,
                            Name = "SYSTEM"
                        }
                    });
                    }
                }
            }

        }

        private void ChangePost(PlatformPostDetails post)
        {
            var state = this.State().DetailsState;
            state.PostLoaded = post;
        }

        public static PlatformPostDetails EnsurePost(WindowState state)
            => state.DetailsState.PostLoaded ?? throw new BadHttpRequestException("No post loaded");
        public static PlatformVideoDetails EnsureVideo(WindowState state)
            => state.DetailsState.VideoLoaded ?? throw new BadHttpRequestException("No video loaded");
        private static void AssertNotBlocked(PlatformVideoDetails video)
        {
            if (video != null && StateBlockedChannels.Instance.IsBlocked(video.Author))
                throw new DialogException(new ExceptionModel()
                {
                    Type = ExceptionModel.EXCEPTION_GENERAL,
                    Title = "Channel blocked",
                    Message = $"The channel [{(string.IsNullOrEmpty(video.Author?.Name) ? video.Author?.Url : video.Author?.Name)}] is blocked, so this video cannot be played.",
                    CanRetry = false,
                    TypeName = nameof(DialogException)
                });
        }
        public static VideoLocal EnsureLocal(WindowState state) => state.DetailsState.VideoLocal ?? throw new BadHttpRequestException("No offline video loaded");
        private RefPager<PlatformComment> EnsureComments()
            => this.State().DetailsState.CommentPager ?? throw new BadHttpRequestException("No comments loaded");
        private RefPager<PlatformComment> EnsureReplies(string reply)
            => (this.State().DetailsState.RepliesPagers.ContainsKey(reply) ? this.State().DetailsState.RepliesPagers[reply] : null) ?? throw new BadHttpRequestException("No replies loaded");
        private IPager<PlatformContent> EnsureRecommendations()
            => this.State().DetailsState.RecommendationPager ?? throw new BadHttpRequestException("No recommendations loaded");



        [HttpGet]
        public PostLoadResult PostLoad(string url)
        {
            Logger.i(nameof(DetailsController), "Loading: " + url);
            IPlatformContentDetails contentDetails = null;
            Exception contentDetailsException = null;
            try
            {
                contentDetails = StatePlatform.GetContentDetails(url);
            }
            catch(ScriptUnavailableException unex)
            {
                throw new DialogException(new ExceptionModel()
                {
                    Type = ExceptionModel.EXCEPTION_SCRIPT,
                    Title = "post was not available",
                    Message = unex?.Message ?? "Could not find the post, or it was otherwise not available",
                    CanRetry = true,
                    TypeName = nameof(ScriptUnavailableException)
                }, unex);
            }
            catch(ScriptCaptchaRequiredException captchaEx)
            {
                throw new NotImplementedException("Captcha");
            }
            catch(Exception ex)
            {
                contentDetailsException = ex;
            }
            if(contentDetails is PlatformPostDetails post)
            {
                ChangePost(post);
            }
            else if(contentDetails == null)
            {
                ChangePost(null);
                if(contentDetailsException != null)
                {
                    throw new DialogException(ExceptionModel.FromException(contentDetailsException));
                }
            }
            else
            {
                ChangePost(null);
                throw new DialogException(new ExceptionModel()
                {
                    Type = ExceptionModel.EXCEPTION_GENERAL,
                    Title = "Unsupported Type",
                    Message = $"Unsupported content type [{contentDetails.GetType().Name}]",
                    CanRetry = false
                });
            }
            var state = this.State().DetailsState;
            return new PostLoadResult()
            {
                Post = state.PostLoaded
            };
        }
        [HttpGet]
        public PlatformPostDetails PostCurrent()
        {
            return this.State().DetailsState.PostLoaded;
        }

        [HttpGet]
        public VideoLoadResult VideoLoad(string url)
        {
            Logger.i(nameof(DetailsController), "Loading: " + url);
            VideoLocal local = StateDownloads.GetDownloadedVideo(url);
            IPlatformContentDetails contentDetails = null;
            Exception contentDetailsException = null;
            try
            {
                contentDetails = StatePlatform.GetContentDetails(url);
            }
            catch(ScriptUnavailableException unex)
            {
                throw new DialogException(new ExceptionModel()
                {
                    Type = ExceptionModel.EXCEPTION_SCRIPT,
                    Title = "Video was not available",
                    Message = unex?.Message ?? "Could not find the video, or it was otherwise not available",
                    CanRetry = true,
                    TypeName = nameof(ScriptUnavailableException)
                }, unex);
            }
            catch(ScriptCaptchaRequiredException captchaEx)
            {
                throw new NotImplementedException("Captcha");
            }
            catch(Exception ex)
            {
                if (local != null)
                    ;// StateUI.Toast("Failed to get live video:\n" + ex.Message);
                contentDetailsException = ex;
            }
            if (local != null)
                StateUI.Toast("Offline video loaded");

            if (contentDetails is PlatformVideoDetails video)
            {
                AssertNotBlocked(video);
                ChangeVideo(video, local);
            }
            else if (local != null)
            {
                ChangeVideo(null, local);
            }
            else if (contentDetails == null)
            {
                ChangeVideo(null, null);
                Logger.e(nameof(DetailsController), "Failed to load video", contentDetailsException);
                if (contentDetailsException is TargetInvocationException targetInvocationException && targetInvocationException.InnerException != null)
                    contentDetailsException = targetInvocationException.InnerException;
                throw new DialogException(ExceptionModel.FromException("Video could not load", contentDetailsException));
            }
            else
            {
                ChangeVideo(null, null);
                throw new DialogException(new ExceptionModel()
                {
                    Type = ExceptionModel.EXCEPTION_GENERAL,
                    Title = "Unsupported Type",
                    Message = $"Unsupported content type [{contentDetails.GetType().Name}]",
                    CanRetry = false
                });
            }

            var state = this.State().DetailsState;
            return new VideoLoadResult()
            {
                Video = state.VideoLoaded,
                Local = state.VideoLocal
            };
        }

        [HttpGet]
        public PlatformVideoDetails VideoCurrent()
            => EnsureVideo(this.State());

        [HttpGet]
        public PagerResult<PlatformContent> RecommendationsLoad(string url)
        {
            var state = this.State().DetailsState;
            if(state.VideoLoaded != null)
            {
                state.RecommendationPager = state.VideoLoaded?.GetContentRecommendations();
                return state.RecommendationPager?.AsPagerResult();
            }
            return null;
        }
        [HttpGet]
        public PagerResult<PlatformContent> RecommendationsNextPage()
        {
            var recommend = EnsureRecommendations();
            recommend.NextPage();
            return recommend.AsPagerResult();
        }


        [HttpGet]
        public PagerResult<RefItem<PlatformComment>> CommentsLoad(string url)
        {
            try
            {
                var state = this.State().DetailsState;
                var pager = StatePlatform.GetComments(EnsureVideo(this.State()));
                state.CommentPager = new RefPager<PlatformComment>(pager);
                state.RepliesPagers = new ConcurrentDictionary<string, RefPager<PlatformComment>>();
                return state.CommentPager.AsPagerResult();
            }
            catch(Exception ex)
            {
                return new PagerResult<RefItem<PlatformComment>>()
                {
                    Exception = ex.Message
                };
            }
        }
        [HttpGet]
        public PagerResult<RefItem<PlatformComment>> CommentsNextPage()
        {
            var comments = EnsureComments();
            comments.NextPage();
            return comments.AsPagerResult();
        }
        [HttpGet]
        public PagerResult<RefItem<PlatformComment>> RepliesLoad(string commentId, string replyId = null)
        {
            var state = this.State().DetailsState;
            if (replyId == "undefined")
                replyId = null;
            var comments = (replyId == null) ? EnsureComments() : EnsureReplies(replyId);
            var comment = comments.FindRef(commentId, true);
            state.RepliesPagers[comment.RefID] = new RefPager<PlatformComment>(StatePlatform.GetSubComments(comment.Object));
            return state.RepliesPagers[comment.RefID].AsPagerResult();
        }
        [HttpGet]
        public PagerResult<RefItem<PlatformComment>> RepliesNextPage(string replyId)
        {
            var replies = EnsureReplies(replyId);
            replies.NextPage();
            return replies.AsPagerResult();
        }


        [HttpGet]
        public async Task<LiveChatWindowDescriptor?> GetLiveChatWindow()
        {
            var video = EnsureVideo(this.State());
            if (!video.IsLive)
                return null;

            var window = StatePlatform.GetLiveChatWindow(video.Url);
            if(window == null || string.IsNullOrEmpty(window.Url) || !string.IsNullOrEmpty(window.Error))
            {
                return window;
            }
            var httpProxy = HttpProxy.Get(true);
            var liveChatProxyEntry = new HttpProxyRegistryEntry()
            {
                Url = window.Url,
                FollowRedirects = false,
                SupportRelativeProxy = true,
                RequestHeaderOptions = new RequestHeaderOptions()
                {
                    HeadersToInject = new Dictionary<string, string>()
                    {
                        { "user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36" }
                    }
                },
                ResponseHeaderOptions = new ResponseHeaderOptions()
                {
                    HeadersToInject = new Dictionary<string, string>()
                    {
                        { "x-frame-options", "ALLOWALL" },
                    },
                }
            };
            //TODO: Fix ModifyResponse
            string iframeId = Guid.NewGuid().ToString();
            liveChatProxyEntry.WithModifyResponseString((resp, str) =>
            {
                return ModifyLiveChatResponse(window, str);
            });
            var state = this.State().DetailsState;

            var oldProxy = state._liveChatProxy;
            if (oldProxy != null)
                httpProxy.Remove(oldProxy.Id);
            state._liveChatProxy = liveChatProxyEntry;
            //TODO: Proper urls

            var uiWindow = StateApp.MainWindow;
            if(uiWindow != null)
            {
                await uiWindow.SetRequestProxyAsync(window.Url, async (req) =>
                {
                    using (HttpClient client = new HttpClient())
                    {
                        foreach (var header in req.Headers)
                            client.DefaultRequestHeaders.Add(header.Key, header.Value);
                        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");

                        var request = new HttpRequestMessage(HttpMethod.Get, window.Url);
                        request.Version = HttpVersion.Version11;
                        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
                        var resp = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead);
                        var resultHeaders = resp.Headers.ToDictionary(x => x.Key, y => y.Value.ToList(), StringComparer.OrdinalIgnoreCase);
                        if (resp.Content != null)
                        {
                            foreach (var pair in resp.Content.Headers)
                            {
                                if (resultHeaders.TryGetValue(pair.Key, out var v))
                                    v.AddRange(pair.Value);
                                else
                                    resultHeaders[pair.Key] = pair.Value.ToList();
                            }
                        }
                        if (resultHeaders.ContainsKey("x-frame-options"))
                            resultHeaders["x-frame-options"] = new List<string>(["ALLOWALL"]);
                        else
                            resultHeaders.Add("x-frame-options", new List<string>(["ALLOWALL"]));


                        string data = await resp.Content!.ReadAsStringAsync();
                        data = ModifyLiveChatResponse(window, data);

                        var bytes = Encoding.UTF8.GetBytes(data);
                        resultHeaders["Content-Length"] = new List<string>([bytes.Length.ToString()]);
                        resultHeaders["Content-Type"] = new List<string>(["text/html"]);

                        return new WindowResponse()
                        {
                            Headers = resultHeaders,
                            StatusCode = (int)resp.StatusCode,
                            StatusText = resp.StatusCode.ToString(),
                            BodyStream = new MemoryStream(bytes)
                        };
                    }
                });
            }
            else
            {
                window.Url = httpProxy.Add(liveChatProxyEntry)!.Replace("127.0.0.1", "localhost");
            }

                return window;
        }

        private string ModifyLiveChatResponse(LiveChatWindowDescriptor window, string str)
        {

            if (!str.Contains("</body>"))
                return str;
            List<string> js = new List<string>();
            if (window.RemoveElements != null)
            {
                foreach (var element in window.RemoveElements)
                {
                    js.Add($"console.log('Removing [' + {JsonConvert.SerializeObject(element)} + ']')");
                    js.Add($"document.querySelectorAll({JsonConvert.SerializeObject(element)}).forEach(x=>x.remove())");
                }
            }
            if (window.RemoveElementsInterval != null)
            {
                StringBuilder builder = new StringBuilder();
                foreach (var element in window.RemoveElementsInterval)
                {
                    builder.AppendLine($"document.querySelectorAll({JsonConvert.SerializeObject(element)}).forEach(x=>x.remove())");
                }
                js.Add("setInterval(()=>{\n" + builder.ToString() + "}, 1000)");
            }

            if (js.Count == 0)
                return str;

            string toInject = string.Join("\n", js);

            str = new BrowserSimulatorBuilder()
                //.WithLocation(window.Url)
                .WithNavigatorValue("webdriver", "false")
                .HideGetOwnProptyDescriptos("webdriver")
                .InjectHtml(str);

            return str
                .Replace("</body>", "<script>(()=>{\n"
                    + toInject
                    + "\n})()</script></body>");
        }


        [HttpGet]
        public List<Chapter> GetVideoChapters(string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;
            return StatePlatform.GetContentChapters(url);
        }


        [HttpGet]
        public IActionResult Download(string url, int videoIndex, int audioIndex)
        {
            var video = EnsureVideo(this.State());
            AssertNotBlocked(video);
            var sourceVideo = (videoIndex >= 0) ? video.Video.VideoSources[videoIndex] : null;
            var sourceAudio = (audioIndex >= 0 && video.Video is UnMuxedVideoDescriptor unmuxed) ? unmuxed.AudioSources[audioIndex] : null;

            VideoDownload existing = StateDownloads.GetDownloadingVideo(video.ID);
            
            //TODO: Edgecases
            if (existing != null)
                return BadRequest("Already downloaded");

            var download = StateDownloads.StartDownload(video, sourceVideo, sourceAudio);

            StateDownloads.StartDownloadCycle();

            return Ok(download);
        }

        [HttpGet]
        public List<VideoQuality> VideoQualities(int videoIndex)
        {
            var loaded = EnsureVideo(this.State());
            AssertNotBlocked(loaded);
            var video = (videoIndex == -999) ? loaded.Live :
                loaded.Video.VideoSources[videoIndex];
            if(video is HLSManifestSource hlsVideo)
            {
                var hlsResponse = _qualityClient.GET(hlsVideo.Url, new Engine.Models.HttpHeaders());
                if (!hlsResponse.IsOk)
                    return new List<VideoQuality>();
                string hlsContent = hlsResponse.Body.AsString();
                var hlsManifest = Parsers.HLS.ParseMasterPlaylist(hlsContent, hlsVideo.Url);
                return hlsManifest.GetVideoSources().Select(x => new VideoQuality()
                {
                    Name = $"({x.Width}x{x.Height}) " + x.Name,
                    Width = x.Width,
                    Height = x.Height
                }).ToList();

            }
            return new List<VideoQuality>();
        }
        public class VideoQuality
        {
            public string Name { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
        }

        public static (IVideoSource? Video, IAudioSource? Audio, ISubtitleSource? Subtitle) GetSources(WindowState state, int videoIndex, int audioIndex, int subtitleIndex, bool videoIsLocal, bool audioIsLocal, bool subtitleIsLocal)
            => (
                (!videoIsLocal) ?
                    ((videoIndex == -999) ? EnsureVideo(state).Live : 
                    ((videoIndex >= 0) ? EnsureVideo(state).Video.VideoSources[videoIndex] : null)) :
                    ((videoIndex >= 0) ? EnsureLocal(state).VideoSources[videoIndex] : null),
                (!audioIsLocal) ?
                    ((audioIndex >= 0 && EnsureVideo(state).Video is UnMuxedVideoDescriptor unmuxed) ? unmuxed.AudioSources[audioIndex] : null) :
                    ((audioIndex >= 0) ? EnsureLocal(state).AudioSources[audioIndex] : null),
                (!subtitleIsLocal) ?
                    ((subtitleIndex >= 0) ? (ISubtitleSource)EnsureVideo(state).Subtitles[subtitleIndex] : null) :
                    ((subtitleIndex >= 0) ? (ISubtitleSource)EnsureLocal(state).SubtitleSources[subtitleIndex] : null)
            );

        public (int VideoIndex, int AudioIndex) GetSourceIndexes(WindowState state, IVideoSource video, IAudioSource audio, bool videoIsLocal, bool audioIsLocal)
            => (
                (!videoIsLocal) ?
                    ((video != null) ? Array.IndexOf(EnsureVideo(state).Video.VideoSources, video) : -1) :
                    ((video != null) ? EnsureLocal(state).VideoSources.IndexOf((LocalVideoSource)video) : -1),
                (!audioIsLocal) ?
                    ((audio != null && EnsureVideo(state).Video is UnMuxedVideoDescriptor unmuxed) ? Array.IndexOf(unmuxed.AudioSources, audio) : -1) :
                    ((audio != null) ? EnsureLocal(state).AudioSources.IndexOf((LocalAudioSource)audio) : -1)
            );
        [HttpGet]
        public async Task<IActionResult> SourceDash(int videoIndex, int audioIndex, int subtitleIndex, bool videoIsLocal = false, bool audioIsLocal = false, bool subtitleIsLocal = false, bool isLoopback = true, string? tag = null)
        {
            var state = this.State();
            AssertNotBlocked(EnsureVideo(state));
            try
            {
                (var taskGenerateSourceDash, var promiseMetadata) = GenerateSourceDash(state, videoIndex, audioIndex, subtitleIndex, videoIsLocal, audioIsLocal, subtitleIsLocal, new ProxySettings(isLoopback));
                if(!taskGenerateSourceDash.IsCompleted && promiseMetadata != null)
                    StateWebsocket.VideoLoader("", promiseMetadata.EstimateDuration, state.WindowID, tag);
                var dash = await taskGenerateSourceDash;
                StateWebsocket.VideoLoaderFinish(state.WindowID, tag);
                return Content(dash, "application/dash+xml");
            }
            catch (ScriptReloadRequiredException reloadEx)
            {
                await StatePlatform.HandleReloadRequired(reloadEx);
                this.VideoLoad(state.DetailsState.VideoLoaded.Url);
                return await SourceDash(videoIndex, audioIndex, subtitleIndex, videoIsLocal, audioIsLocal, subtitleIsLocal, isLoopback);
            }
            catch (Exception ex)
            {
                throw;
            }
        }
        public static (Task<string>, V8PromiseMetadata?) GenerateSourceDash(WindowState state, int videoIndex, int audioIndex, int subtitleIndex, bool videoIsLocal = false, bool audioIsLocal = false, bool subtitleIsLocal = false, ProxySettings? proxySettings = null)
        {
            var cachedDashTask = state.DetailsState.GetCachedDashTask(videoIndex, audioIndex, subtitleIndex, proxySettings);
            if (cachedDashTask != null)
            {
                Logger.w<DetailsController>("Using cached DASH.");
                return (cachedDashTask, null);
            }

            (var sourceVideo, var sourceAudio, var sourceSubtitle) = GetSources(state, videoIndex, audioIndex, subtitleIndex, videoIsLocal, audioIsLocal, subtitleIsLocal);
            if (sourceVideo is DashManifestRawSource videoRawSource && sourceAudio is DashManifestRawAudioSource audioRawSource)
            {
                if(sourceSubtitle != null)
                    sourceSubtitle = SubtitleToProxied(state, sourceSubtitle, subtitleIsLocal, subtitleIndex, proxySettings);

                V8PromiseMetadata? metadata = null;
                var task = GenerateSourceDashRaw(state, videoRawSource, audioRawSource, sourceSubtitle, proxySettings, out metadata);
                state.DetailsState.SetCachedDash(videoIndex, audioIndex, subtitleIndex, proxySettings, task);
                return (task, metadata);
            }
            else if(sourceVideo is DashManifestRawSource videoRawSource2)
            {
                if (sourceSubtitle != null)
                    sourceSubtitle = SubtitleToProxied(state, sourceSubtitle, subtitleIsLocal, subtitleIndex, proxySettings);

                V8PromiseMetadata? metadata = null;
                var task = GenerateSourceDashRaw(state, videoRawSource2, null, sourceSubtitle, proxySettings, out metadata);
                state.DetailsState.SetCachedDash(videoIndex, audioIndex, subtitleIndex, proxySettings, task);
                return (task, metadata);
            }


            if (sourceVideo != null && !(sourceVideo is VideoUrlSource || sourceVideo is LocalVideoSource))
                throw new NotImplementedException();

            if (sourceAudio != null && !(sourceAudio is AudioUrlSource || sourceAudio is LocalAudioSource))
                throw new NotImplementedException();

            string? videoUrl;
            if (videoIsLocal)
            {
                if (proxySettings != null && proxySettings.Value.ExposeLocalAsAny && proxySettings.Value.ProxyAddress != null && sourceVideo != null)
                    videoUrl = $"http://{proxySettings.Value.ProxyAddress.ToUrlAddress()}:{GrayjayCastingServer.Instance.BaseUri!.Port}/Details/StreamLocalVideoSource?index={videoIndex}&windowId={state.WindowID}";
                else if (sourceVideo != null)
                    videoUrl = $"{GrayjayServer.Instance.BaseUrl}/Details/StreamLocalVideoSource?index={videoIndex}&windowId={state.WindowID}";
                else
                    videoUrl = null;
            }
            else if (proxySettings != null && proxySettings.Value.ShouldProxySources(sourceVideo, sourceAudio))
            {
                var modifier = (sourceVideo is JSSource jsS) ? jsS.GetRequestModifier() : null;
                var executor = (sourceVideo is JSSource jsS2) ? jsS2.GetRequestExecutor() : null;

                videoUrl = sourceVideo != null
                    ? WebUtility.HtmlEncode(HttpProxy.Get(proxySettings.Value.IsLoopback).Add(new HttpProxyRegistryEntry() {
                        RequestModifier = modifier?.ToProxyFunc(),
                        Url = (sourceVideo as VideoUrlSource)!.Url 
                    }, proxySettings?.ProxyAddress))
                    : null;
            }
            else
            {
                videoUrl = sourceVideo != null
                    ? (sourceVideo as VideoUrlSource)!.Url
                    : null;
            }

            string? audioUrl;
            if (audioIsLocal)
            {
                if (proxySettings != null && proxySettings.Value.ExposeLocalAsAny && proxySettings.Value.ProxyAddress != null && sourceAudio != null)
                    audioUrl = $"http://{proxySettings.Value.ProxyAddress.ToUrlAddress()}:{GrayjayCastingServer.Instance.BaseUri!.Port}/Details/StreamLocalAudioSource?index={audioIndex}&windowId={state.WindowID}";
                else if (sourceAudio != null)
                    audioUrl = $"{GrayjayServer.Instance.BaseUrl}/Details/StreamLocalAudioSource?index={audioIndex}&windowId={state.WindowID}";
                else
                    audioUrl = null;
            }
            else if (proxySettings != null && proxySettings.Value.ShouldProxySources(sourceVideo, sourceAudio))
            {
                var modifier = (sourceAudio is JSSource jsS) ? jsS.GetRequestModifier() : null;
                var executor = (sourceAudio is JSSource jsS2) ? jsS2.GetRequestExecutor() : null;
                audioUrl = sourceAudio != null ? WebUtility.HtmlEncode(HttpProxy.Get(proxySettings.Value.IsLoopback).Add(new HttpProxyRegistryEntry()
                {
                    RequestModifier = modifier?.ToProxyFunc(),
                    Url = (sourceAudio as AudioUrlSource)!.Url
                }, proxySettings?.ProxyAddress)) : null;
            }
            else
            {
                audioUrl = sourceAudio != null
                    ? (sourceAudio as AudioUrlSource)!.Url
                    : null;
            }

            string? subtitleUrl;
            if (sourceSubtitle != null)
            {
                if (subtitleIsLocal)
                {
                    if (proxySettings != null && proxySettings.Value.ExposeLocalAsAny && proxySettings.Value.ProxyAddress != null && sourceSubtitle != null)
                        subtitleUrl = $"http://{proxySettings.Value.ProxyAddress.ToUrlAddress()}:{GrayjayCastingServer.Instance.BaseUri!.Port}/Details/StreamLocalSubtitleSource?index={subtitleIndex}&windowId={state.WindowID}";
                    else
                        subtitleUrl = $"{GrayjayServer.Instance.BaseUrl}/Details/StreamLocalSubtitleSource?index={subtitleIndex}&windowId={state.WindowID}";
                }
                else
                {
                    var uri = sourceSubtitle.GetSubtitlesUri()!;
                    if (uri.Scheme == "file")
                    {
                        if (proxySettings != null && proxySettings.Value.ExposeLocalAsAny && proxySettings.Value.ProxyAddress != null)
                        {
                            subtitleUrl = sourceSubtitle != null
                                ? WebUtility.HtmlEncode(HttpProxy.Get(proxySettings.Value.IsLoopback).Add(new HttpProxyRegistryEntry() { Url = $"http://{proxySettings.Value.ProxyAddress.ToUrlAddress()}:{GrayjayCastingServer.Instance.BaseUri!.Port}/Details/StreamSubtitleFile?index={subtitleIndex}&windowId={state.WindowID}" }, proxySettings?.ProxyAddress))
                                : null;
                        }
                        else
                            subtitleUrl = $"{GrayjayServer.Instance.BaseUrl}/Details/StreamSubtitleFile?index={subtitleIndex}&windowId={state.WindowID}";
                    }
                    else
                    {
                        if (proxySettings != null && proxySettings.Value.ShouldProxySources(sourceVideo, sourceAudio))
                        {
                            subtitleUrl = sourceSubtitle != null
                                ? WebUtility.HtmlEncode(HttpProxy.Get(proxySettings.Value.IsLoopback).Add(new HttpProxyRegistryEntry() { Url = uri.ToString() }, proxySettings?.ProxyAddress))
                                : null;
                        }
                        else
                        {
                            subtitleUrl = sourceSubtitle != null
                                ? sourceSubtitle.Url
                                : null;
                        }
                    }
                }
            }
            else
                subtitleUrl = null;

            var dash = DashBuilder.GenerateOnDemandDash(sourceVideo, videoUrl, sourceAudio, audioUrl, sourceSubtitle, subtitleUrl);
            var dashTask = Task.FromResult(dash);
            state.DetailsState.SetCachedDash(videoIndex, audioIndex, subtitleIndex, proxySettings, dashTask);
            return (dashTask, null);
        }

        private static ISubtitleSource SubtitleToProxied(WindowState state, ISubtitleSource sourceSubtitle, bool subtitleIsLocal, int subtitleIndex, ProxySettings? proxySettings, string? modifierId = null)
        {
            if (sourceSubtitle == null)
                return null;

            var subtitleUrl = BuildSubtitleUrl(state, subtitleIndex, subtitleIsLocal, proxySettings, modifierId);

            return new SubtitleSource()
            {
                Name = "Served",
                HasFetch = false,
                Format = sourceSubtitle.Format,
                Url = subtitleUrl
            };
        }

        public static Task<string> GenerateSourceDashRaw(WindowState state, DashManifestRawSource videoSource, DashManifestRawAudioSource audioSource, ISubtitleSource subtitleSource, ProxySettings? proxySettings, out V8PromiseMetadata promiseMeta)
        {
            var merging = (audioSource == null) ? videoSource : new DashManifestMergingRawSource(videoSource, audioSource, subtitleSource);

            var dashTask = merging.GenerateAsync(out promiseMeta);

            return dashTask.ContinueWith((t) =>
            {
                var dash = dashTask.Result;
                var oldVReqEx = state.DetailsState._videoRequestExecutor;
                var oldAReqEx = state.DetailsState._audioRequestExecutor;
                oldVReqEx?.Cleanup();
                state.DetailsState._videoRequestExecutor = null;
                oldAReqEx?.Cleanup();
                state.DetailsState._audioRequestExecutor = null;

                string videoUrl = null;
                string audioUrl = null;

                if (merging is DashManifestMergingRawSource mergingM)
                {
                    if (mergingM.Video.HasRequestExecutor)
                        videoUrl = getRequestExecutorProxy("https://grayjay.app/internal/video", mergingM.Video.GetRequestExecutor(), proxySettings);
                    else
                        throw new NotImplementedException();
                    if (mergingM.Audio.HasRequestExecutor)
                        audioUrl = getRequestExecutorProxy("https://grayjay.app/internal/audio", mergingM.Audio.GetRequestExecutor(), proxySettings);
                    else
                        throw new NotImplementedException();
                }
                else
                {
                    if(merging.HasRequestExecutor)
                    {
                        videoUrl = getRequestExecutorProxy("https://grayjay.app/internal/video", merging.GetRequestExecutor(), proxySettings);
                        audioUrl = videoUrl;
                    }
                }


                foreach (Match representation in DashBuilder.REGEX_REPRESENTATION.Matches(dash))
                {
                    var mediaType = representation.Groups[1].Value ?? throw new InvalidDataException("Media type not found for dash representation");
                    dash = DashBuilder.REGEX_MEDIA_INITIALIZATION.Replace(dash, new MatchEvaluator((m) =>
                    {
                        if (m.Index < representation.Index || (m.Index + m.Length) > (representation.Index + representation.Length))
                            return m.Value;

                        if (mediaType.StartsWith("video/"))
                            return $"{m.Groups[1].Value}=\"{videoUrl}?url={HttpUtility.UrlEncode(m.Groups[2].Value).Replace("%24Number%24", "$Number$")}&amp;mediaType={HttpUtility.UrlEncode(mediaType)}\"";
                        else if (mediaType.StartsWith("audio/"))
                            return $"{m.Groups[1].Value}=\"{audioUrl}?url={HttpUtility.UrlEncode(m.Groups[2].Value).Replace("%24Number%24", "$Number$")}&amp;mediaType={HttpUtility.UrlEncode(mediaType)}\"";
                        else
                            throw new InvalidDataException("Expected video or audio? got: " + mediaType);
                    }));
                }

                var subUrl = subtitleSource?.Url;
                if (!string.IsNullOrWhiteSpace(subUrl))
                    dash = InjectDashSubtitle(dash, subUrl, subtitleSource?.Format);

                return dash;
            });
        }

        private static string getRequestExecutorProxy(string registerUrl, RequestExecutor reqExecutor, ProxySettings? proxySettings)
        {
            if (reqExecutor != null && !reqExecutor.DidCleanup)
                return HttpProxy.Get(proxySettings?.IsLoopback ?? true).Add(new HttpProxyRegistryEntry()
                {
                    Url = "https://internal.grayjay.app/",
                    IsRelative = true,
                    RequestExecutor = (req) =>
                    {
                        var queryParams = HttpUtility.ParseQueryString((req.Path.Contains("?")) ? req.Path.Substring(req.Path.IndexOf("?")) : "");
                        string mediaType = HttpUtility.UrlDecode(queryParams["mediaType"]);
                        if (queryParams["url"] != null)
                        {
                            string url = HttpUtility.UrlDecode(queryParams["url"]);
                            if (reqExecutor.DidCleanup)
                                return null;
                            var result = reqExecutor.ExecuteRequest(url, new Dictionary<string, string>());
                            return new HttpProxyResponse()
                            {
                                StatusCode = 200,
                                Headers = new Engine.Models.HttpHeaders(new Dictionary<string, string>()
                                {
                                    { "Content-Type", mediaType },
                                    { "Content-Length", result.Length.ToString() }
                                }),
                                Version = "HTTP/1.1",
                                Data = result
                            };
                        }
                        else
                            return null;
                    },
                    ResponseHeaderOptions = new ResponseHeaderOptions()
                    {
                        InjectPermissiveCORS = true
                    }
                }, proxySettings?.ProxyAddress);
            else
                throw new NotImplementedException();
        }

        [HttpGet]
        public async Task<IActionResult> SourceHLS(int videoIndex = -1, int audioIndex = -1, int subtitleIndex = -1, bool subtitleIsLocal = false, bool isLoopback = true, string? modifierId = null)
        {
            AssertNotBlocked(EnsureVideo(this.State()));
            return Content(await GenerateSourceHLS(this.State(), videoIndex, audioIndex, subtitleIndex, subtitleIsLocal, new ProxySettings(isLoopback), modifierId), "application/x-mpegurl");
        }

        public static async Task<string> GenerateSourceHLS(WindowState state, int videoManifest, int audioManifest, int subtitleIndex, bool subtitleIsLocal, ProxySettings? proxySettings = null, string? modifierId = null)
        {
            if (videoManifest < 0 && audioManifest < 0 && videoManifest != -999)
                throw new Exception("No manifest index provided");

            (var sourceVideo, var sourceAudio, _) = GetSources(state, videoManifest, audioManifest, -1, false, false, false);

            string hlsUrl;
            IRequestModifier? modifier = null;

            if (sourceVideo is HLSManifestSource hv)
            {
                hlsUrl = hv.Url;
                modifier = hv.GetRequestModifier();
            }
            else if (sourceAudio is HLSManifestAudioSource ha)
            {
                hlsUrl = ha.Url;
                modifier = ha.GetRequestModifier();
            }
            else
                throw new Exception("Expected HLS video or audio manifest source.");

            var headers = new Grayjay.Engine.Models.HttpHeaders();
            var res = ModifierHttp.GetBytes(new ManagedHttpClient(), hlsUrl, modifier, headers);
            if (!res.IsOk)
                throw new InvalidDataException($"Failed to fetch manifest [{res.Code}]");

            var finalUrl = res.FinalUrl;
            var body = Encoding.UTF8.GetString(res.Bytes);
            var baseUri = GetBaseUri(state, proxySettings);
            if (string.IsNullOrEmpty(modifierId) && modifier != null)
                modifierId = ProxyController.GetOrCreateModifierId(state, modifier, finalUrl);

            try
            {
                var master = Parsers.HLS.ParseMasterPlaylist(body, finalUrl);
                ProxyController.ProxyHLSMasterPlaylist(baseUri, master, proxyMedia: true, modifierId: modifierId, windowId: state.WindowID);
                if (subtitleIndex < 0)
                    return master.GenerateM3U8();

                var existingGroup = master.VariantPlaylistsRefs
                    .Select(v => v.StreamInfo?.Subtitles)
                    .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s) && !s.Equals("NONE", StringComparison.OrdinalIgnoreCase));

                var subsGroupId = string.IsNullOrWhiteSpace(existingGroup) ? "gj-subs" : existingGroup;
                var subsPlaylist =
                    $"{baseUri}/Details/SubtitleHLS?subtitleIndex={subtitleIndex}"
                    + $"&subtitleIsLocal={subtitleIsLocal}"
                    + $"&windowId={state.WindowID}"
                    + (!string.IsNullOrEmpty(modifierId) ? $"&modifierId={Uri.EscapeDataString(modifierId)}" : "");

                master.MediaRenditions.Add(new Parsers.HLS.MediaRendition(
                    type: "SUBTITLES",
                    uri: subsPlaylist,
                    groupId: subsGroupId,
                    language: null,
                    name: "Subtitles",
                    isDefault: true,
                    isAutoSelect: true,
                    isForced: false
                ));

                foreach (var vref in master.VariantPlaylistsRefs)
                {
                    if (vref.StreamInfo == null)
                        continue;

                    if (string.IsNullOrWhiteSpace(vref.StreamInfo.Subtitles) || vref.StreamInfo.Subtitles.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                        vref.StreamInfo.Subtitles = subsGroupId;
                }

                return master.GenerateM3U8();
            }
            catch
            {
                if (subtitleIndex < 0)
                {
                    return (await ProxyController.GenerateProxiedHLS(finalUrl, proxyMedia: true, baseUri: baseUri, state: state, modifier: modifier, modifierId: modifierId)).GenerateM3U8();
                }

                var subsGroupId = "gj-subs";
                var subsPlaylist =
                    $"{baseUri}/Details/SubtitleHLS?subtitleIndex={subtitleIndex}"
                    + $"&subtitleIsLocal={subtitleIsLocal}"
                    + $"&windowId={state.WindowID}"
                    + (!string.IsNullOrEmpty(modifierId) ? $"&modifierId={Uri.EscapeDataString(modifierId)}" : "");

                var proxiedVariant =
                    $"{baseUri}/proxy/HLS?url={HttpUtility.UrlEncode(finalUrl)}&proxyMedia=true"
                    + (!string.IsNullOrEmpty(modifierId) ? $"&modifierId={Uri.EscapeDataString(modifierId)}" : "")
                    + $"&windowId={state.WindowID}";

                int? bw = null;
                string? reso = null;
                string? codecs = null;

                if (sourceVideo != null)
                {
                    var t = sourceVideo.GetType();
                    if (t.GetProperty("Bitrate")?.GetValue(sourceVideo) is int bwi) bw = bwi;
                    if (t.GetProperty("Codec")?.GetValue(sourceVideo) is string c) codecs = c;

                    var w = t.GetProperty("Width")?.GetValue(sourceVideo) as int?;
                    var h = t.GetProperty("Height")?.GetValue(sourceVideo) as int?;
                    if (w.HasValue && h.HasValue && w.Value > 0 && h.Value > 0)
                        reso = $"{w.Value}x{h.Value}";
                }

                var wrapped = new Parsers.HLS.MasterPlaylist(
                    version: 3,
                    variantPlaylistsRefs: new List<Parsers.HLS.VariantPlaylistReference>
                    {
                        new Parsers.HLS.VariantPlaylistReference(
                            proxiedVariant,
                            new Parsers.HLS.StreamInfo(
                                bandwidth: bw,
                                resolution: reso,
                                codecs: codecs,
                                frameRate: null,
                                videoRange: null,
                                audio: null,
                                video: null,
                                subtitles: subsGroupId,
                                closedCaptions: null
                            )
                        )
                    },
                    mediaRenditions: new List<Parsers.HLS.MediaRendition>
                    {
                        new Parsers.HLS.MediaRendition(
                            type: "SUBTITLES",
                            uri: subsPlaylist,
                            groupId: subsGroupId,
                            language: null,
                            name: "Subtitles",
                            isDefault: true,
                            isAutoSelect: true,
                            isForced: false
                        )
                    },
                    sessionDataList: new List<Parsers.HLS.SessionData>(),
                    independentSegments: true
                );

                return wrapped.GenerateM3U8();
            }
        }

        [HttpGet]
        public async Task<IActionResult> SourceAuto()
        {
            var state = this.State().DetailsState;
            if(state.VideoLocal != null)
            {
                var local = EnsureLocal(this.State());
                var bestVideoSourceIndex = VideoHelper.SelectBestVideoSourceIndex(local.VideoSources.Cast<IVideoSource>().ToList(), 9999*9999, new List<string>() { "video/mp4" });
                var bestAudioSourceIndex = VideoHelper.SelectBestAudioSourceIndex(local.AudioSources.Cast<IAudioSource>().ToList(), new List<string>() { "audio/mp4" }, GrayjaySettings.Instance.Playback.GetPrimaryLanguage(), 9999 * 9999);
                var bestSubtitleSourceIndex = local.SubtitleSources.Count > 0 ? 0 : -1;
                return await SourceProxy(bestVideoSourceIndex, bestAudioSourceIndex, bestSubtitleSourceIndex, true, true, true);
            }
            else
            {
                var video = EnsureVideo(this.State());
                AssertNotBlocked(video);
                var bestVideoSourceIndex = VideoHelper.SelectBestVideoSourceIndex(video.Video.VideoSources.Cast<IVideoSource>().ToList(), GrayjaySettings.Instance.Playback.GetPreferredQualityPixelCount(), new List<string>() { "video/mp4" });
                var bestAudioSourceIndex = (video.Video is UnMuxedVideoDescriptor unmuxed) ? 
                    VideoHelper.SelectBestAudioSourceIndex(unmuxed.AudioSources.Cast<IAudioSource>().ToList(), new List<string>() { "audio/mp4" }, GrayjaySettings.Instance.Playback.GetPrimaryLanguage(), 9999 * 9999) : 
                    -1;

                if (bestVideoSourceIndex == -1 && bestAudioSourceIndex == -1 && video.DateTime > DateTime.Now)
                    throw new DialogException(new ExceptionModel()
                    {
                        Type = ExceptionModel.EXCEPTION_GENERAL,
                        Title = "Video unavailable",
                        Message = "The video is not yet available, auto-reload is TODO."
                    });

                if (bestVideoSourceIndex == -1 && bestAudioSourceIndex == -1 && video.Live != null)
                    return await SourceProxy(-999, -1, -1, false, false, false);

                if (bestVideoSourceIndex >= 0 && bestAudioSourceIndex >= 0)
                {
                    (var videoSources, var audioSource, _) = GetSources(this.State(), bestVideoSourceIndex, bestAudioSourceIndex, -1, false, false, false);

                    if(videoSources is DashManifestRawSource && audioSource is DashManifestRawAudioSource)
                    {
                        return await SourceProxy(bestVideoSourceIndex, bestAudioSourceIndex, -1, false, false, false);
                    }
                    else if (!(videoSources is IStreamMetaDataSource) || !(audioSource is IStreamMetaDataSource))
                        throw DialogException.FromException("Cannot play this source",
                            new Exception("Unmuxed sources require IStreamMetaDataSource info to translate to dash"));
                }
                return await SourceProxy(bestVideoSourceIndex, bestAudioSourceIndex, -1, false, false, false);
            }
        }

        [HttpGet]
        public async Task<IActionResult> SourceProxy(int videoIndex, int audioIndex, int subtitleIndex, bool videoIsLocal = false, bool audioIsLocal = false, bool subtitleIsLocal = false, string? tag = null)
        {
            return Ok(await GenerateSourceProxy(this.State(), videoIndex, audioIndex, subtitleIndex, videoIsLocal, audioIsLocal, subtitleIsLocal, null, tag));
        }
        public static async Task<SourceDescriptor> GenerateSourceProxy(WindowState state, int videoIndex, int audioIndex, int subtitleIndex, bool videoIsLocal = false, bool audioIsLocal = false, bool subtitleIsLocal = false, ProxySettings? proxySettings = null, string? tag = null, bool forceReady = false)
        {
            var video = EnsureVideo(state);
            AssertNotBlocked(video);

            if (videoIndex == -999)
            {
                if (video.Live == null)
                    throw new DialogException(new ExceptionModel()
                    {
                        Title = "Livestream not available",
                        Message = "The video's livestream source could not be found",
                        CanRetry = false
                    });
                if (video.Live is HLSManifestSource)
                    return DirectHLSUrlSource(state, videoIndex, -1, subtitleIndex, subtitleIsLocal, proxySettings ?? new ProxySettings(true), null);
                else throw new DialogException(new ExceptionModel()
                {
                    Title = "Livestream type not supported",
                    Message = $"Livestream type not supported [{video.Live.GetType().Name}]",
                    CanRetry = false
                });
            }

            (var sourceVideo, var sourceAudio, var sourceSubtitle) = GetSources(state, videoIndex, audioIndex, subtitleIndex, videoIsLocal, audioIsLocal, subtitleIsLocal);
            if (subtitleIndex >= 0 && sourceVideo is HLSManifestSource)
                return DirectHLSUrlSource(state, videoIndex, -1, subtitleIndex, subtitleIsLocal, proxySettings ?? new ProxySettings(true), null);

            if (subtitleIndex >= 0 && sourceAudio is HLSManifestAudioSource)
                return DirectHLSUrlSource(state, -1, audioIndex, subtitleIndex, subtitleIsLocal, proxySettings ?? new ProxySettings(true), null);
                
            if (sourceVideo != null && (sourceAudio != null || sourceSubtitle != null))
            {
                if (sourceAudio != null && !(sourceVideo is DashManifestRawSource && sourceAudio is DashManifestRawAudioSource) && (!(sourceVideo is IStreamMetaDataSource) || !(sourceAudio is IStreamMetaDataSource)))
                    throw DialogException.FromException("Cannot play this source", new Exception("Unmuxed sources require IStreamMetaDataSource info to translate to dash"));

                if (forceReady)
                {
                    //Preload the DASH
                    (var taskGenerateSourceDash, var promiseMetadata) = GenerateSourceDash(state, videoIndex, audioIndex, subtitleIndex, videoIsLocal, audioIsLocal, subtitleIsLocal, proxySettings);
                    if (!taskGenerateSourceDash.IsCompleted && promiseMetadata != null)
                        StateWebsocket.VideoLoader("", promiseMetadata.EstimateDuration, state.WindowID, tag);
                    await taskGenerateSourceDash;
                    StateWebsocket.VideoLoaderFinish(state.WindowID, tag);
                }

                return new SourceDescriptor($"/details/SourceDash?videoIndex={videoIndex}&audioIndex={audioIndex}&subtitleIndex={subtitleIndex}&videoIsLocal={videoIsLocal}&audioIsLocal={audioIsLocal}&subtitleIsLocal={subtitleIsLocal}&isLoopback={proxySettings?.IsLoopback ?? true}&windowId={state.WindowID}&tag={tag}", "application/dash+xml", videoIndex, audioIndex, subtitleIndex, videoIsLocal, audioIsLocal, subtitleIsLocal);
            }
            else if (sourceVideo != null)
            {
                if (sourceVideo is VideoUrlSource vus)
                    return DirectVideoUrlSource(vus, videoIndex, videoIsLocal, proxySettings);
                else if (sourceVideo is HLSManifestSource)
                    return DirectHLSUrlSource(state, videoIndex, -1, subtitleIndex, subtitleIsLocal, proxySettings ?? new ProxySettings(true), null);
                else if (sourceVideo is LocalVideoSource lvs)
                    return LocalVideoSource(state, lvs);
                else if (sourceVideo is DashManifestRawSource das)
                {
                    if (!(sourceVideo is IStreamMetaDataSource))
                        throw DialogException.FromException("Source doesn't provide enough playback info for unmuxed playback (IStreamMetaData)",
                            new Exception("Unmuxed sources require IStreamMetaDataSource info to translate to dash"));

                    if (forceReady)
                    {
                        //Preload the DASH
                        (var taskGenerateSourceDash, var promiseMetadata) = GenerateSourceDash(state, videoIndex, audioIndex, subtitleIndex, videoIsLocal, audioIsLocal, subtitleIsLocal, proxySettings);
                        if (!taskGenerateSourceDash.IsCompleted && promiseMetadata != null)
                            StateWebsocket.VideoLoader("", promiseMetadata.EstimateDuration, state.WindowID, tag);
                        await taskGenerateSourceDash;
                        StateWebsocket.VideoLoaderFinish(state.WindowID, tag);
                    }

                    return new SourceDescriptor($"/details/SourceDash?videoIndex={videoIndex}&audioIndex={audioIndex}&subtitleIndex={subtitleIndex}&videoIsLocal={videoIsLocal}&audioIsLocal={audioIsLocal}&subtitleIsLocal={subtitleIsLocal}&isLoopback={proxySettings?.IsLoopback ?? true}&windowId={state.WindowID}&tag={tag}", "application/dash+xml", videoIndex, audioIndex, subtitleIndex, videoIsLocal, audioIsLocal, subtitleIsLocal);

                }
                else
                    throw new Exception($"Not implemented type {sourceVideo.GetType().Name}");
            }
            else if (sourceAudio != null)
            {
                if (sourceAudio is AudioUrlSource aus)
                    return DirectAudioUrlSource(aus, audioIndex, audioIsLocal, proxySettings);
                else if (sourceAudio is HLSManifestAudioSource)
                    return DirectHLSUrlSource(state, -1, audioIndex, subtitleIndex, subtitleIsLocal, proxySettings ?? new ProxySettings(true), null);
                else if (sourceAudio is LocalAudioSource las)
                    return LocalAudioSource(state, las);
                else
                    throw new Exception($"Not implemented type {sourceAudio.GetType().Name}");
            }
            else
                throw new DialogException(new ExceptionModel()
                {
                    Title = "No sources available on this video",
                    Message = $"Missing video and/or audio stream for [{video?.Name}]\nLivestreams sometimes take some time to become available after it finishes.",
                    CanRetry = false
                });
            //throw new Exception("Select either a videoIndex or audioIndex");
        }

        private static readonly Regex _repIdRegex = new Regex("Representation\\s+id=\"(\\d+)\"", RegexOptions.Compiled);
        private static int NextNumericRepresentationId(string mpd)
        {
            int max = 0;
            foreach (Match m in _repIdRegex.Matches(mpd))
            {
                if (int.TryParse(m.Groups[1].Value, out var v) && v > max)
                    max = v;
            }
            return max + 1;
        }

        private static string XmlEscape(string s)
        {
            var raw = s.Replace("&amp;", "&").Trim();
            return SecurityElement.Escape(raw) ?? raw;
        }

        private static string InjectDashSubtitle(string mpd, string subtitleUrl, string lang = "en")
        {
            if (string.IsNullOrWhiteSpace(mpd) || string.IsNullOrWhiteSpace(subtitleUrl))
                return mpd;

            if (mpd.IndexOf("mimeType=\"text/vtt\"", StringComparison.OrdinalIgnoreCase) >= 0)
                return mpd;

            int repId = NextNumericRepresentationId(mpd);
            string url = XmlEscape(subtitleUrl);
            string block =
                $"    <AdaptationSet mimeType=\"text/vtt\" lang=\"{lang}\">\n" +
                $"      <Representation id=\"{repId}\" bandwidth=\"256\">\n" +
                $"        <BaseURL>{url}</BaseURL>\n" +
                $"      </Representation>\n" +
                $"    </AdaptationSet>\n";

            int insertAt = mpd.LastIndexOf("</Period>", StringComparison.OrdinalIgnoreCase);
            if (insertAt < 0)
                return mpd;

            return mpd.Insert(insertAt, block);
        }

        [HttpGet]
        public IActionResult StreamLocalVideoSource(int index)
        {
            var local = EnsureLocal(this.State());
            var source = local.VideoSources[index];
            return File(new FileStream(source.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read), source.Container, true);
        }
        [HttpGet]
        public IActionResult StreamLocalAudioSource(int index)
        {
            var local = EnsureLocal(this.State());
            var source = local.AudioSources[index];
            return File(new FileStream(source.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read), source.Container, true);
        }
        [HttpGet]
        public IActionResult StreamLocalSubtitleSource(int index)
        {
            var local = EnsureLocal(this.State());
            var source = local.SubtitleSources[index];
            var stream = new FileStream(source.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, source.Format!, enableRangeProcessing: true);
        }
        [HttpGet]
        public IActionResult StreamSubtitleFile(int index)
        {
            var video = EnsureVideo(this.State());
            var source = video.Subtitles[index];
            var uri = source.GetSubtitlesUri()!;
            if (uri.Scheme != "file")
                throw new InvalidOperationException("Must be a file URI.");
            var stream = new FileStream(uri.AbsolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, source.Format, enableRangeProcessing: true);
        }


        private static SourceDescriptor DashRawSources(int vindex, int aindex)
        {
            string url = $"/details/SourceDashRaw?videoManifest={vindex}&audioManifest=-1";
            return new SourceDescriptor(url, "application/dash+xml", vindex, -1, -1, false, false, false);
        }

        private static SourceDescriptor LocalVideoSource(WindowState state, LocalVideoSource sourceVideo)
        {
            var local = EnsureLocal(state);
            int index = local.VideoSources.IndexOf(sourceVideo);
            return new SourceDescriptor($"/Details/StreamLocalVideoSource?index={index}&windowId={state.WindowID}", sourceVideo.Container, index, -1, -1, true, true, false);
        }
        private static SourceDescriptor LocalAudioSource(WindowState state, LocalAudioSource sourceAudio)
        {
            var local = EnsureLocal(state);
            int index = local.AudioSources.IndexOf(sourceAudio);
            return new SourceDescriptor($"/Details/StreamLocalAudioSource?index={index}&windowId={state.WindowID}", sourceAudio.Container, -1, index, -1, true, true, false);
        }
        private static SourceDescriptor DirectVideoUrlSource(VideoUrlSource sourceVideo, int index, bool isLocal, ProxySettings? proxySettings = null)
        {
            var modifier = (sourceVideo.HasRequestModifier) ? sourceVideo.GetRequestModifier() : null;
            var executor = (sourceVideo.HasRequestExecutor) ? sourceVideo.GetRequestExecutor() : null;

            var videoUrl = proxySettings != null && proxySettings.Value.ShouldProxySources(sourceVideo, null) ? WebUtility.HtmlEncode(HttpProxy.Get(proxySettings.Value.IsLoopback).Add(new HttpProxyRegistryEntry()
            {   
                RequestModifier = modifier?.ToProxyFunc(),
                Url = (sourceVideo as VideoUrlSource).Url
            })) : sourceVideo.Url;
            return new SourceDescriptor(videoUrl, sourceVideo.Container)
            {
                VideoIndex = index,
                VideoIsLocal = isLocal
            };
        }
        private static SourceDescriptor DirectAudioUrlSource(AudioUrlSource sourceAudio, int index, bool isLocal, ProxySettings? proxySettings = null)
        {
            var modifier = (sourceAudio.HasRequestModifier) ? sourceAudio.GetRequestModifier() : null;
            var executor = (sourceAudio.HasRequestExecutor) ? sourceAudio.GetRequestExecutor() : null;

            var audioUrl = proxySettings != null && proxySettings.Value.ShouldProxySources(null, sourceAudio) ? WebUtility.HtmlEncode(HttpProxy.Get(proxySettings.Value.IsLoopback).Add(new HttpProxyRegistryEntry()
            {
                RequestModifier = modifier?.ToProxyFunc(),
                Url = (sourceAudio as AudioUrlSource).Url
            })) : sourceAudio.Url;
            return new SourceDescriptor(audioUrl, sourceAudio.Container)
            {
                AudioIndex = index,
                AudioIsLocal = isLocal
            };
        }

        private static SourceDescriptor DirectHLSUrlSource(WindowState state, int videoIndex = -1, int audioIndex = -1, int subtitleIndex = -1, bool subtitleIsLocal = false, ProxySettings? proxySettings = null, string? modifierId = null)
        {
            (var sourceVideo, var sourceAudio, _) = GetSources(state, videoIndex, audioIndex, -1, false, false, false);
            if (subtitleIndex >= 0)
            {
                return new SourceDescriptor(
                    $"/details/SourceHLS?videoIndex={videoIndex}&audioIndex={audioIndex}"
                    + $"&subtitleIndex={subtitleIndex}&subtitleIsLocal={subtitleIsLocal}"
                    + $"&isLoopback={proxySettings?.IsLoopback ?? true}"
                    + $"&windowId={state.WindowID}"
                    + (!string.IsNullOrEmpty(modifierId) ? $"&modifierId={Uri.EscapeDataString(modifierId)}" : ""),
                    "application/vnd.apple.mpegurl",
                    videoIndex, audioIndex, subtitleIndex,
                    false, false, subtitleIsLocal
                );
            }

            if (sourceVideo is HLSManifestSource hlsm)
            {
                if (proxySettings != null && proxySettings.Value.ShouldProxySources(sourceVideo, sourceAudio))
                {
                    return new SourceDescriptor(
                        $"/details/SourceHLS?videoIndex={videoIndex}&isLoopback={proxySettings?.IsLoopback ?? true}&windowId={state.WindowID}",
                        "application/vnd.apple.mpegurl",
                        videoIndex, -1, -1, false, false, false
                    );
                }
                return new SourceDescriptor(hlsm.Url, "application/vnd.apple.mpegurl", videoIndex, -1, -1, false, false, false);
            }

            if (sourceAudio is HLSManifestAudioSource hlsa)
            {
                if (proxySettings != null && proxySettings.Value.ShouldProxySources(sourceVideo, sourceAudio))
                {
                    return new SourceDescriptor(
                        $"/details/SourceHLS?audioIndex={audioIndex}&videoIndex=-1&isLoopback={proxySettings?.IsLoopback ?? true}&windowId={state.WindowID}",
                        "application/vnd.apple.mpegurl",
                        -1, audioIndex, -1, false, false, false
                    );
                }
                return new SourceDescriptor(hlsa.Url, "application/vnd.apple.mpegurl", -1, audioIndex, -1, false, false, false);
            }

            throw new Exception("Expected either HLS audio or video source.");
        }

        [HttpGet]
        public bool WatchProgress(string url, long position)
        {
            var state = this.State().DetailsState;
            if (url == null)
                return false;
            if (url == state.VideoLoaded?.Url && state.VideoHistoryIndex.Url == url)
            {
                state.LiveChatManager?.SetVideoPosition(position);
                try
                {
                    if (state.VideoPlaybackTracker?.ShouldUpdate() ?? false)
                        state.VideoPlaybackTracker?.OnProgress((double)position / 1000, true);
                }
                catch(Exception ex)
                {
                    Logger.w(nameof(DetailsController), $"Failed to call onProgress on PlaybackTracker: " + ex.Message, ex);
                }

                var video = state.VideoLoaded;
                var history = state.VideoHistoryIndex;
                if(state._lastWatchPositionChange == DateTime.MinValue)
                {
                    state._lastWatchPosition = position;
                    state._lastWatchPositionChange = DateTime.Now;
                    return false;
                }
                if (DateTime.Now.Subtract(state._lastWatchPositionChange) > TimeSpan.FromSeconds(1))
                {
                    long delta = position - state._lastWatchPosition;
                    if (delta < 0)
                        return false;
                    state._lastWatchPosition = position;
                    state._lastWatchPositionChange = DateTime.Now;

                    Logger.v(nameof(DetailsController), $"Progress {url} - {position} - {delta} (PlaybackTracker: " + (state.VideoPlaybackTracker != null).ToString() + ")");

                    StateHistory.UpdateHistory(video, history, position / 1000, delta);
                    if (state.VideoSubscription != null)// && GrayjaySettings.Instance.Subscriptions.AllowPlaytimeTracking)
                        state.VideoSubscription.UpdateWatchTime((int)(delta / 1000));
                    return true;
                }
            }
            return false;
        }



        public class VideoLoadResult
        {
            public PlatformVideoDetails Video { get; set; }
            public VideoLocal Local { get; set; }
        }

        public class PostLoadResult
        {
            public PlatformPostDetails Post { get; set; }
        }

        public class SourceDescriptor
        {
            public string Url { get; set; }
            public string Type { get; set; }

            public int VideoIndex { get; set; }
            public bool VideoIsLocal { get; set; }
            public int AudioIndex { get; set; }
            public bool AudioIsLocal { get; set; }
            public int SubtitleIndex { get; set; }
            public bool SubtitleIsLocal { get; set; }

            public SourceDescriptor() { }
            public SourceDescriptor(string url, string type, int videoIndex = -1, int audioIndex = -1, int subtitleIndex = -1, bool videoIsLocal = false, bool audioIsLocal = false, bool subtitleIsLocal = false)
            {
                Url = url; 
                Type = type; 
                VideoIndex = videoIndex;
                AudioIndex = audioIndex;
                SubtitleIndex = subtitleIndex;
                VideoIsLocal = videoIsLocal;
                AudioIsLocal = audioIsLocal;
                SubtitleIsLocal = subtitleIsLocal;
            }
        }

        private static string GetBaseUri(WindowState state, ProxySettings? proxySettings)
        {
            if (proxySettings != null && proxySettings.Value.ExposeLocalAsAny && proxySettings.Value.ProxyAddress != null)
                return $"http://{proxySettings.Value.ProxyAddress.ToUrlAddress()}:{GrayjayCastingServer.Instance.BaseUri!.Port}";

            return GrayjayServer.Instance.BaseUrl;
        }

        private static string BuildSubtitleUrl(WindowState state, int subtitleIndex, bool subtitleIsLocal, ProxySettings? proxySettings, string? modifierId = null)
        {
            var baseUri = GetBaseUri(state, proxySettings);
            var url = $"{baseUri}/Details/Subtitle?subtitleIndex={subtitleIndex}"
                + $"&subtitleIsLocal={subtitleIsLocal}"
                + $"&windowId={state.WindowID}";

            if (!string.IsNullOrEmpty(modifierId))
                url += $"&modifierId={Uri.EscapeDataString(modifierId)}";

            return url;
        }

        public static async Task<(byte[] Bytes, string ContentType)> GetSubtitleBytesAsync(WindowState state, int subtitleIndex, bool subtitleIsLocal, string? modifierId = null)
        {
            if (subtitleIsLocal)
            {
                var local = EnsureLocal(state);
                var src = local.SubtitleSources[subtitleIndex];
                var ct = string.IsNullOrWhiteSpace(src.Format) ? "text/vtt" : src.Format!;
                var bytes = await System.IO.File.ReadAllBytesAsync(src.FilePath);
                return (bytes, ct);
            }

            var video = EnsureVideo(state);
            var srcRemote = video.Subtitles[subtitleIndex];
            var contentType = string.IsNullOrWhiteSpace(srcRemote.Format) ? "text/vtt" : srcRemote.Format!;

            var uri = srcRemote.GetSubtitlesUri();
            if (uri == null)
                uri = new Uri(srcRemote.Url);

            if (uri.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = await System.IO.File.ReadAllBytesAsync(uri.LocalPath);
                return (bytes, contentType);
            }

            IRequestModifier? modifier = null;
            if (!string.IsNullOrEmpty(modifierId))
                DetailsState.Modifiers.TryGetValue(modifierId, out modifier);

            if (modifier != null)
            {
                var headers = new Grayjay.Engine.Models.HttpHeaders();
                var res = ModifierHttp.GetBytes(new ManagedHttpClient(), uri.ToString(), modifier, headers);
                if (!res.IsOk)
                    throw new InvalidDataException($"Failed to fetch subtitle [{res.Code}]");

                return (res.Bytes, contentType);
            }

            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
            var b = await client.GetByteArrayAsync(uri);
            return (b, contentType);
        }

        private static double? TryGetVideoDurationSeconds(WindowState state)
        {
            var v = state.DetailsState.VideoLoaded;
            if (v == null)
                return null;

            object? videoObj = v;
            foreach (var name in new[] { "DurationSeconds", "Duration" })
            {
                var p = videoObj.GetType().GetProperty(name);
                if (p != null)
                {
                    var val = p.GetValue(videoObj);
                    if (val is int i) return i;
                    if (val is long l) return l;
                    if (val is double d) return d;
                    if (val is float f) return f;
                }
            }

            var innerVideo = v.GetType().GetProperty("Video")?.GetValue(v);
            if (innerVideo != null)
            {
                foreach (var name in new[] { "DurationSeconds", "Duration" })
                {
                    var p = innerVideo.GetType().GetProperty(name);
                    if (p != null)
                    {
                        var val = p.GetValue(innerVideo);
                        if (val is int i) return i;
                        if (val is long l) return l;
                        if (val is double d) return d;
                        if (val is float f) return f;
                    }
                }
            }

            return null;
        }

        private static string GenerateSubtitleMediaPlaylist(string subtitleUrl, double durationSeconds)
        {
            if (durationSeconds <= 0)
                durationSeconds = 60;

            var targetDuration = Math.Max(1, (int)Math.Ceiling(durationSeconds));
            var dur = durationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return
                "#EXTM3U\n" +
                "#EXT-X-VERSION:3\n" +
                $"#EXT-X-TARGETDURATION:{targetDuration}\n" +
                "#EXT-X-MEDIA-SEQUENCE:0\n" +
                $"#EXTINF:{dur},\n" +
                subtitleUrl + "\n" +
                "#EXT-X-ENDLIST\n";
        }

        [HttpGet]
        public async Task<IActionResult> Subtitle(int subtitleIndex, bool subtitleIsLocal = false, string? modifierId = null)
        {
            Response.Headers["Access-Control-Allow-Origin"] = "*";

            var state = this.State();
            var (bytes, ct) = await GetSubtitleBytesAsync(state, subtitleIndex, subtitleIsLocal, modifierId);
            return File(bytes, ct);
        }

        [HttpGet]
        public IActionResult SubtitleHLS(int subtitleIndex, bool subtitleIsLocal = false, string? modifierId = null)
        {
            Response.Headers["Access-Control-Allow-Origin"] = "*";

            var state = this.State();

            var baseUri = $"{Request.Scheme}://{Request.Host.Value}";
            var subtitleUrl =
                $"{baseUri}/Details/Subtitle?subtitleIndex={subtitleIndex}"
                + $"&subtitleIsLocal={subtitleIsLocal}"
                + $"&windowId={state.WindowID}"
                + (!string.IsNullOrEmpty(modifierId) ? $"&modifierId={Uri.EscapeDataString(modifierId)}" : "");

            var dur = TryGetVideoDurationSeconds(state) ?? 86400.0;
            var m3u8 = GenerateSubtitleMediaPlaylist(subtitleUrl, dur);

            return Content(m3u8, "application/x-mpegurl");
        }
    }
}
