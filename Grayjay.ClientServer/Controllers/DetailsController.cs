using Grayjay.ClientServer.Browser;
using Grayjay.ClientServer.Database.Indexes;
using Grayjay.ClientServer.Exceptions;
using Grayjay.ClientServer.Helpers;
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
using Grayjay.Engine.Models.Playback;
using Grayjay.Engine.Models.Subtitles;
using Grayjay.Engine.Models.Video;
using Grayjay.Engine.Models.Video.Additions;
using Grayjay.Engine.Models.Video.Sources;
using Grayjay.Engine.Pagers;
using Grayjay.Engine.Web;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class DetailsController : ControllerBase
    {
        public class DetailsState
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



            public RefPager<PlatformComment> CommentPager { get; set; }
            public ConcurrentDictionary<string, RefPager<PlatformComment>> RepliesPagers { get; set; } = new ConcurrentDictionary<string, RefPager<PlatformComment>>();


            public IPager<PlatformContent> RecommendationPager { get; set; }

        }

        static ManagedHttpClient _qualityClient = new ManagedHttpClient();

        private void ChangeVideo(PlatformVideoDetails video, VideoLocal videoLocal)
        {
            var state = this.State().DetailsState;
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
                if(local != null)
                    StateUI.Toast("Failed to get live video:\n" + ex.Message);
                contentDetailsException = ex;
            }
            if (local != null)
                StateUI.Toast("Offline video loaded");

            if (contentDetails is PlatformVideoDetails video)
            {
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
            var state = this.State().DetailsState;
            var pager = StatePlatform.GetComments(EnsureVideo(this.State()));
            state.CommentPager = new RefPager<PlatformComment>(pager);
            state.RepliesPagers = new ConcurrentDictionary<string, RefPager<PlatformComment>>();
            return state.CommentPager.AsPagerResult();
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
            var video = (videoIndex == -999) ? EnsureVideo(this.State()).Live :
                EnsureVideo(this.State()).Video.VideoSources[videoIndex];
            if(video is HLSManifestSource hlsVideo)
            {
                var hlsResponse = _qualityClient.GET(hlsVideo.Url, new Dictionary<string, string>());
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
        public IActionResult SourceDash(int videoIndex, int audioIndex, int subtitleIndex, bool videoIsLocal = false, bool audioIsLocal = false, bool subtitleIsLocal = false, bool isLoopback = true)
            => Content(GenerateSourceDash(this.State(), videoIndex, audioIndex, subtitleIndex, videoIsLocal, audioIsLocal, subtitleIsLocal, new ProxySettings(isLoopback)), "application/dash+xml");
        public static string GenerateSourceDash(WindowState state, int videoIndex, int audioIndex, int subtitleIndex, bool videoIsLocal = false, bool audioIsLocal = false, bool subtitleIsLocal = false, ProxySettings? proxySettings = null)
        {
            (var sourceVideo, var sourceAudio, var sourceSubtitle) = GetSources(state, videoIndex, audioIndex, subtitleIndex, videoIsLocal, audioIsLocal, subtitleIsLocal);

            if (sourceVideo is DashManifestRawSource videoRawSource && sourceAudio is DashManifestRawAudioSource audioRawSource)
                return GenerateSourceDashRaw(state, videoRawSource, audioRawSource, proxySettings?.IsLoopback ?? true);

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
            else if (proxySettings != null && proxySettings.Value.ShouldProxy)
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
            else if (proxySettings != null && proxySettings.Value.ShouldProxy)
            {
                var modifier = (sourceVideo is JSSource jsS) ? jsS.GetRequestModifier() : null;
                var executor = (sourceVideo is JSSource jsS2) ? jsS2.GetRequestExecutor() : null;
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
                        if (proxySettings != null && proxySettings.Value.ShouldProxy)
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

            return DashBuilder.GenerateOnDemandDash(sourceVideo, videoUrl, sourceAudio, audioUrl, sourceSubtitle, subtitleUrl);
        }

        public static string GenerateSourceDashRaw(WindowState state, DashManifestRawSource videoSource, DashManifestRawAudioSource audioSource, bool loopback)
        {
            var merging = new DashManifestMergingRawSource(videoSource, audioSource);
            var dash = merging.Generate();

            var oldVReqEx = state.DetailsState._videoRequestExecutor;
            var oldAReqEx = state.DetailsState._audioRequestExecutor;
            oldVReqEx?.Cleanup();
            state.DetailsState._videoRequestExecutor = null;
            oldAReqEx?.Cleanup();
            state.DetailsState._audioRequestExecutor = null;

            string videoUrl = null;
            string audioUrl = null;

            if (merging.Video.HasRequestExecutor)
                videoUrl = getRequestExecutorProxy("https://grayjay.app/internal/video", merging.Video.GetRequestExecutor(), loopback);
            else
                throw new NotImplementedException();
            if (merging.Audio.HasRequestExecutor)
                audioUrl = getRequestExecutorProxy("https://grayjay.app/internal/audio", merging.Audio.GetRequestExecutor(), loopback);
            else
                throw new NotImplementedException();


            foreach(Match representation in DashBuilder.REGEX_REPRESENTATION.Matches(dash))
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

            return dash;
        }

        private static string getRequestExecutorProxy(string registerUrl, RequestExecutor reqExecutor, bool loopback)
        {
            if (reqExecutor != null && !reqExecutor.DidCleanup)
                return HttpProxy.Get(loopback).Add(new HttpProxyRegistryEntry()
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
                                Headers = new Dictionary<string, string>()
                                    {
                                        { "Content-Type", mediaType },
                                        { "Content-Length", result.Length.ToString() }
                                    },
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
                });
            else
                throw new NotImplementedException();
        }

        [HttpGet]
        public async Task<IActionResult> SourceHLS(int videoIndex = -1, int audioIndex = -1, bool isLoopback = true)
        {
            return Content(await GenerateSourceHLS(this.State(), videoIndex, audioIndex, new ProxySettings(isLoopback)), "application/x-mpegurl");
        }

        public static async Task<string> GenerateSourceHLS(WindowState state, int videoManifest, int audioManifest, ProxySettings? proxySettings = null)
        {
            if (videoManifest < 0 && audioManifest < 0 && videoManifest != -999)
                throw new Exception("No manifest index provided");

            Parsers.HLS.IHLSPlaylist? playlist = null;
            (var sourceVideo, var sourceAudio, _) = GetSources(state, videoManifest, audioManifest, -1, false, false, false);
            
            if(sourceVideo is HLSManifestSource hlsVideoSource)
            {
                if (proxySettings != null && proxySettings.Value.ProxyAddress != null && !proxySettings.Value.IsLoopback)
                    playlist = await ProxyController.GenerateProxiedHLS(hlsVideoSource.Url, true, $"http://{proxySettings.Value.ProxyAddress.ToUrlAddress()}:{GrayjayCastingServer.Instance.BaseUri!.Port}");
                else
                    playlist = await ProxyController.GenerateProxiedHLS(hlsVideoSource.Url, true, $"{GrayjayServer.Instance.BaseUrl}");            
            }
            else if(sourceAudio is HLSManifestAudioSource hlsAudioSource)
            {
                if (proxySettings != null && proxySettings.Value.ProxyAddress != null && !proxySettings.Value.IsLoopback)
                    playlist = await ProxyController.GenerateProxiedHLS(hlsAudioSource.Url, true, $"http://{proxySettings.Value.ProxyAddress.ToUrlAddress()}:{GrayjayCastingServer.Instance.BaseUri!.Port}");
                else
                    playlist = await ProxyController.GenerateProxiedHLS(hlsAudioSource.Url, true, $"{GrayjayServer.Instance.BaseUrl}");
            }

            if (playlist == null)
                throw new Exception("No manifest");


            return playlist.GenerateM3U8();
        }

        [HttpGet]
        public IActionResult SourceAuto()
        {
            var state = this.State().DetailsState;
            if(state.VideoLocal != null)
            {
                var local = EnsureLocal(this.State());
                var bestVideoSourceIndex = VideoHelper.SelectBestVideoSourceIndex(local.VideoSources.Cast<IVideoSource>().ToList(), 9999*9999, new List<string>() { "video/mp4" });
                var bestAudioSourceIndex = VideoHelper.SelectBestAudioSourceIndex(local.AudioSources.Cast<IAudioSource>().ToList(), new List<string>() { "audio/mp4" }, GrayjaySettings.Instance.Playback.GetPrimaryLanguage(), 9999 * 9999);
                var bestSubtitleSourceIndex = local.SubtitleSources.Count > 0 ? 0 : -1;
                return SourceProxy(bestVideoSourceIndex, bestAudioSourceIndex, bestSubtitleSourceIndex, true, true, true);
            }
            else
            {
                var video = EnsureVideo(this.State());
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
                    return SourceProxy(-999, -1, -1, false, false, false);

                if (bestVideoSourceIndex >= 0 && bestAudioSourceIndex >= 0)
                {
                    (var videoSources, var audioSource, _) = GetSources(this.State(), bestVideoSourceIndex, bestAudioSourceIndex, -1, false, false, false);

                    if(videoSources is DashManifestRawSource && audioSource is DashManifestRawAudioSource)
                    {
                        return SourceProxy(bestVideoSourceIndex, bestAudioSourceIndex, -1, false, false, false);
                    }
                    else if (!(videoSources is IStreamMetaDataSource) || !(audioSource is IStreamMetaDataSource))
                        throw DialogException.FromException("Cannot play this source",
                            new Exception("Unmuxed sources require IStreamMetaDataSource info to translate to dash"));
                }
                return SourceProxy(bestVideoSourceIndex, bestAudioSourceIndex, -1, false, false, false);
            }
        }

        [HttpGet]
        public IActionResult SourceProxy(int videoIndex, int audioIndex, int subtitleIndex, bool videoIsLocal = false, bool audioIsLocal = false, bool subtitleIsLocal = false)
        {
            return Ok(GenerateSourceProxy(this.State(), videoIndex, audioIndex, subtitleIndex, videoIsLocal, audioIsLocal, subtitleIsLocal, null));
        }
        public static SourceDescriptor GenerateSourceProxy(WindowState state, int videoIndex, int audioIndex, int subtitleIndex, bool videoIsLocal = false, bool audioIsLocal = false, bool subtitleIsLocal = false, ProxySettings? proxySettings = null)
        {
            var video = EnsureVideo(state);

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
                    return DirectHLSUrlSource(state, videoIndex, -1, new ProxySettings(true));
                else throw new DialogException(new ExceptionModel()
                {
                    Title = "Livestream type not supported",
                    Message = $"Livestream type not supported [{video.Live.GetType().Name}]",
                    CanRetry = false
                });
            }

            (var sourceVideo, var sourceAudio, var sourceSubtitle) = GetSources(state, videoIndex, audioIndex, subtitleIndex, videoIsLocal, audioIsLocal, subtitleIsLocal);

            if (sourceVideo != null && (sourceAudio != null || sourceSubtitle != null))
            {
                if (!(sourceVideo is DashManifestRawSource das && sourceAudio is DashManifestRawAudioSource daus))
                    if (!(sourceVideo is IStreamMetaDataSource) || !(sourceAudio is IStreamMetaDataSource))
                        throw DialogException.FromException("Source doesn't provide enough playback info for unmuxed playback (IStreamMetaData)",
                            new Exception("Unmuxed sources require IStreamMetaDataSource info to translate to dash"));
                return new SourceDescriptor($"/details/SourceDash?videoIndex={videoIndex}&audioIndex={audioIndex}&subtitleIndex={subtitleIndex}&videoIsLocal={videoIsLocal}&audioIsLocal={audioIsLocal}&subtitleIsLocal={subtitleIsLocal}&isLoopback={proxySettings?.IsLoopback ?? true}&windowId={state.WindowID}", "application/dash+xml", videoIndex, audioIndex, subtitleIndex, videoIsLocal, audioIsLocal, subtitleIsLocal);
            }
            else if (sourceVideo != null)
            {
                if (sourceVideo is VideoUrlSource vus)
                    return DirectVideoUrlSource(vus, videoIndex, videoIsLocal, proxySettings);
                else if (sourceVideo is HLSManifestSource)
                    return DirectHLSUrlSource(state, videoIndex, -1, new ProxySettings(true));
                else if (sourceVideo is LocalVideoSource lvs)
                    return LocalVideoSource(state, lvs);
                else
                    throw new Exception($"Not implemented type {sourceVideo.GetType().Name}");
            }
            else if (sourceAudio != null)
            {
                if (sourceAudio is AudioUrlSource aus)
                    return DirectAudioUrlSource(aus, audioIndex, audioIsLocal, proxySettings);
                else if (sourceAudio is HLSManifestAudioSource)
                    return DirectHLSUrlSource(state, -1, audioIndex, new ProxySettings(true));
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

            var videoUrl = proxySettings != null && proxySettings.Value.ShouldProxy ? WebUtility.HtmlEncode(HttpProxy.Get(proxySettings.Value.IsLoopback).Add(new HttpProxyRegistryEntry()
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

            var audioUrl = proxySettings != null && proxySettings.Value.ShouldProxy ? WebUtility.HtmlEncode(HttpProxy.Get(proxySettings.Value.IsLoopback).Add(new HttpProxyRegistryEntry()
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

        private static SourceDescriptor DirectHLSUrlSource(WindowState state, int videoIndex = -1, int audioIndex = -1, ProxySettings? proxySettings = null)
        {
            (var sourceVideo, var sourceAudio, _) = GetSources(state, videoIndex, audioIndex, -1, false, false, false);

            if (sourceVideo is HLSManifestSource hlsm)
            {
                if (proxySettings != null && proxySettings.Value.ShouldProxy)
                {
                    return new SourceDescriptor($"/details/SourceHLS?videoIndex={videoIndex}&isLoopback={proxySettings?.IsLoopback ?? true}&windowId={state.WindowID}", "application/vnd.apple.mpegurl", videoIndex, -1, -1, false, false, false);
                }
                else
                    return new SourceDescriptor(hlsm.Url, "application/vnd.apple.mpegurl", videoIndex, -1, -1, false, false, false);
            }
            else if (sourceAudio is HLSManifestAudioSource hlsa)
            {
                if (proxySettings != null && proxySettings.Value.ShouldProxy)
                {
                    return new SourceDescriptor($"/details/SourceHLS?audioIndex={audioIndex}&videoIndex=-1&isLoopback={proxySettings?.IsLoopback ?? true}&windowId={state.WindowID}", "application/vnd.apple.mpegurl", -1, audioIndex, -1, false, false, false);
                }
                else
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
                    if (state.VideoSubscription != null && GrayjaySettings.Instance.Subscriptions.AllowPlaytimeTracking)
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
    }
}
