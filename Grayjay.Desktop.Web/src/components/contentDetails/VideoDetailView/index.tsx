import { Accessor, Component, For, Match, Show, Switch, batch, catchError, createEffect, createMemo, createResource, createSignal, on, onCleanup, onMount, untrack } from "solid-js";
import styles from './index.module.css';

import ic_minimize from '../../../assets/icons/icon24_chevron_down.svg';
import add from '../../../assets/icons/icon24_add.svg';
import ic_sync from '../../../assets/icons/ic_sync.svg';
import error from '../../../assets/icons/icon_error.svg';
import iconDownload from '../../../assets/icons/icon24_download.svg';
import share from '../../../assets/icons/icon24_Share.svg';
import ic_chevron_down from '../../../assets/icons/icon_chrevron_down.svg';
import ic_close from '../../../assets/icons/icon24_close.svg';
import store from '../../../assets/icons/icon24_store.svg';
import more from '../../../assets/icons/icon_button_more.svg';
import donate from '../../../assets/icons/icon24_donate.svg';
import VideoPlayerView from "../../player/VideoPlayerView";
import { VideoMode, VideoState, useVideo } from "../../../contexts/VideoProvider";
import ScrollContainer from "../../containers/ScrollContainer";
import VirtualFlexibleArrayList from "../../containers/VirtualFlexibleArrayList";
import StickyShrinkOnScrollContainer from "../../containers/StickyShrinkOnScrollContainer";
import PillButton from "../../buttons/PillButton";
import IconButton from "../../buttons/IconButton";
import CustomButton from "../../buttons/CustomButton";
import CommentView from "../../CommentView";
import { catchDialogExceptions, createResourceDefault, getBestThumbnail, preventDragDrop, proxyImage, sanitzeHtml, toHumanNowDiffString, toHumanNowDiffStringMinDay, toHumanNumber, formatAudioSourceName } from "../../../utility";
import { DetailsBackend } from "../../../backend/DetailsBackend";
import { useNavigate, useSearchParams } from "@solidjs/router";
import SubscribeButton from "../../buttons/SubscribeButton";
import SettingsMenu, { Menu, MenuItem, IMenuItemGroup, IMenuItemOption, MenuItemButton } from "../../menus/Overlays/SettingsMenu";
import ExceptionModel from "../../../backend/exceptions/ExceptionModel";
import UIOverlay from "../../../state/UIOverlay";
import Loader from "../../basics/loaders/Loader";
import Anchor, { AnchorStyle } from "../../../utility/Anchor";
import DragArea from "../../basics/DragArea";
import ResizeHandle from "../../basics/ResizeHandle";
import { IVideoLocal } from "../../../backend/models/downloads/IVideoLocal";
import { DateTime, Duration } from "luxon";
import { decode } from "html-entities";
import NavigationBar from "../../topbars/NavigationBar";
import PlaybackQueue from "../../PlaybackQueue";
import { WatchLaterBackend } from "../../../backend/WatchLaterBackend";
import { Event0 } from "../../../utility/Event";
import ToggleButtonGroup from "../../ToggleButtonGroup";
import { Pager } from "../../../backend/models/pagers/Pager";
import { RefItem } from "../../../backend/models/RefItem";
import { ISerializedComment } from "../../../backend/models/comments/ISerializedComment";
import icon_back from '../../../assets/icons/icon24_back.svg';
import icon_close from '../../../assets/icons/icon24_close.svg';
import pinned_fill from '../../../assets/icons/pinned-fill.svg';
import pinned from '../../../assets/icons/pinned.svg';
import loop_inactive from '../../../assets/icons/icon_loop_inactive.svg';
import loop_active from '../../../assets/icons/icon_loop_active.svg';
import { RatingTypes } from "../../../backend/models/IRating";
import RatingView from "../../RatingView";
import { IChapter } from "../../../backend/models/contentDetails/IChapter";
import TransparentIconButton from "../../buttons/TransparentIconButton";
import SearchBar from "../../topbars/SearchBar";
import { ContentType } from "../../../backend/models/ContentType";
import DOMPurify from 'dompurify';
import { LocalBackend } from "../../../backend/LocalBackend";
import { ILiveChatWindowDescriptor } from "../../../backend/models/comments/ILiveChatWindowDescriptor";
import LiveChatRemoteWindow from "../../LiveChatRemoteWindow";
import { HistoryBackend } from "../../../backend/HistoryBackend";
import StateGlobal from "../../../state/StateGlobal";
import StateSync from "../../../state/StateSync";
import { SyncDevice } from "../../../backend/models/sync/SyncDevice";
import { SyncBackend } from "../../../backend/SyncBackend";
import { SettingsBackend } from "../../../backend/SettingsBackend";
import FlexibleArrayList from "../../containers/FlexibleArrayList";
import { IPlatformContent } from "../../../backend/models/content/IPlatformContent";
import VideoThumbnailView from "../../content/VideoThumbnailView";
import { IPlatformVideo } from "../../../backend/models/content/IPlatformVideo";
import HorizontalScrollContainer from "../../containers/HorizontalScrollContainer";
import HorizontalFlexibleArrayList from "../../containers/HorizontalFlexibleArrayList";
import SideBar from "../../menus/SideBar";

export interface SourceSelected {
    url: string;
    video: number;
    videoIsLocal: boolean;
    audio: number;
    audioIsLocal: boolean;
    subtitle: number;
    subtitleIsLocal: boolean;
    videoSourceUrl?: string;
    thumbnailUrl: string;
    isLive: boolean;
    shouldResume?: boolean;
    time?: Duration;
}

export interface VideoDetailsProps {
};

const VideoDetailView: Component<VideoDetailsProps> = (props) => {
    let scrollContainerRef: HTMLDivElement | undefined;
    let horizontalScrollRecommendContainerRef: HTMLDivElement | undefined;
    let descriptionContainerRef: HTMLDivElement | undefined;
    let containerRef: HTMLDivElement | undefined;
    let videoContainer: HTMLDivElement | undefined;
    let isScrubbing = false;
    let position: Duration | undefined = undefined;
    let errorCounter: number = 0;
    const video = useVideo();

    const [videoLocal$, setVideoLocal] = createSignal<IVideoLocal | undefined>();
    const currentVideo$ = createMemo(() => {
        const queue = video?.queue();
        const index = video?.index();
        if (!queue || index === undefined) {
            return undefined;
        }

        if (index < 0 || index >= queue.length) {
            return undefined;
        }

        return queue[index];
    });
    const currentVideoUrl$ = createMemo(() => {
        return currentVideo$()?.backendUrl ?? currentVideo$()?.url;
    });
    const [videoLoaded$, videoLoadedResource] = createResourceDefault(() => currentVideoUrl$(), async (url) => {
        if (!url || !url.length) {
            console.info("set video", {url});
            return undefined;
        }

        try {
            return await UIOverlay.catchDialogExceptions(async ()=>{

                const result = (!url) ? null : (await DetailsBackend.videoLoad(url));
                setVideoLocal(result?.local);
                console.info("set video", { url, video: result?.video, local: result?.local });
                return result?.video;
            }, ()=>{
                video?.actions?.closeVideo();
            }, ()=>{
                videoLoadedResource.refetch();
            });
        }
        catch (error: any) {
            throw error;
        }
    });

    const [commentsPager$] = createResource<Pager<RefItem<ISerializedComment>>>(() => videoLoaded$(), async (videoLoaded: any) => (!videoLoaded) ? undefined : await DetailsBackend.commentsPager());
    const [videoChapters$, videoChaptersResource] = createResourceDefault(()=> currentVideo$()?.url, async (url)=>{
        const result = (!url) ? undefined : (await DetailsBackend.getVideoChapters(url));
        console.log("Video chapters:", result);
        return result;
    });
    const [liveChatWindow$] = createResource<ILiveChatWindowDescriptor | undefined>(() => videoLoaded$(), async (videoLoaded: any) => (!videoLoaded || !videoLoaded.isLive) ? undefined : await DetailsBackend.liveChatWindow());
    const [recomPager$] = createResource<Pager<IPlatformContent>>(() => videoLoaded$(), async (videoLoaded: any) => {
        if(!videoLoaded)
            return undefined;
        const result =  await DetailsBackend.recommendationsPager(videoLoaded.url);
        console.log("Recommendation Results:", result);
        return result;
    });


    const [videoSource$, setVideoSource] = createSignal<SourceSelected>();
    const [videoQuality$, setVideoQuality] = createSignal<number>(-1);
    const [playerQuality$, setPlayerQuality] = createSignal<number>(-1);
    createEffect(()=>{
        const newPlayerLevel = playerQuality$();
        updateQualityMenu();
    });

    createEffect(on(currentVideoUrl$, (url) => {
        console.info("Reset error counter because video source changed", { url, errorCounter });
        errorCounter = 0;
    }));

    const [videoSourceQualities$] = createResource<any | undefined>(()=> videoSource$()?.video && !videoSource$()?.videoIsLocal, async () => {
        if(videoSource$()?.video < 0 && videoSource$()?.video != -999) {
            setVideoQuality(-1);
            return undefined;
        }
        else {
            var result = await DetailsBackend.sourceVideoQualities(videoSource$()?.video ?? 0);
            setVideoQuality(-1);
            return result;
        }
    });

    const COMMENT_SECTION_POLYCENTRIC = 0;
    const COMMENT_SECTION_PLATFORM = 1;
    const [activeCommentSection$, setActiveCommentSection] = createSignal(COMMENT_SECTION_PLATFORM);

    const localVideoSources$ = createMemo(() => {
        return videoLocal$()?.videoSources as any[] ?? [];
    });
    const localAudioSources$ = createMemo(() => {
        return videoLocal$()?.audioSources as any[] ?? [];
    });
    const localSubtitleSources$ = createMemo(() => {
        return videoLocal$()?.subtitleSources as any[] ?? [];
    });
    const videoSources$ = createMemo(() => {
        return videoLoaded$()?.video?.videoSources as any[] ?? [];
    });
    const audioSources$ = createMemo(() => {
        return videoLoaded$()?.video?.audioSources as any[] ?? [];
    });
    const subtitleSources$ = createMemo(() => {
        const subs = videoLoaded$()?.subtitles;
        console.info("subtitle sources", subs);
        return subs as any[] ?? [];
    });
    const [fullDescriptionVisible$, setFullDescriptionVisible] = createSignal<boolean>(false);
    const [repliesVisible$, setRepliesVisible] = createSignal<boolean>(false);
    const [repliesPager$, setRepliesPager] = createSignal<Pager<RefItem<ISerializedComment>>>();
    const [repliesParents$, setRepliesParents] = createSignal<ISerializedComment[]>();
    const hideReplies = () => {
        batch(() => {
            setRepliesVisible(false);
            setRepliesPager(undefined);
            setRepliesParents(undefined);
        })
    };

    let repliesOverlayScrollContainerRef: HTMLDivElement | undefined;

    const nextVideoIndex = () => {
        const index = video?.index();
        const queue = video?.queue();
        const repeat = video?.repeat();
        const shuffle = video?.shuffle();

        console.info("next video index", {currentIndex: index, queue, repeat, shuffle});

        if (index === undefined || !queue || queue.length === 0) {
            return undefined;
        }

        if (shuffle) {
            if (repeat) {
                return Math.floor(Math.random() * (queue.length - 1));
            } else {
                //TODO: Don't repeat if repeat not enabled, track played
                return Math.floor(Math.random() * (queue.length - 1));
            }
        } else if (index + 1 >= queue.length) {
            if (repeat) {
                return 0;
            } else {
                return undefined;
            }
        } else {
            return index + 1;
        }
    };

    const handleEnded = async () => {
        const currentIndex = video?.index();
        if (currentIndex === undefined) {
            return;
        }

        /*TODO: Track played if shuffle enabled but repeat is not*/

        const currentVideo = videoLoaded$();
        const nextIndex = nextVideoIndex();
        if (nextIndex !== undefined) {
            if (nextIndex === currentIndex) {
                eventRestart.invoke();
            } else {
                video?.actions?.setIndex(nextIndex);
            }
        }

        if (currentVideo) {
            await WatchLaterBackend.remove(currentVideo.url);
            await video?.actions?.refetchWatchLater();
        }
    };

    const handleError = (error: string, fatal: boolean) => {
        console.info("Error occurred", { fatal, error });

        if (!fatal) {
            return;
        }

        errorCounter++;
        console.info("Error counter", { errorCounter });

        const reloadMedia = () => {
            video?.actions.setStartTime(position);
            videoLoadedResource.mutate(undefined);
            videoLoadedResource.refetch();
        };

        const nvi = nextVideoIndex();
        if (nvi === undefined) {
            console.error("Playback error: " + error, { errorCounter });
            UIOverlay.overlayConfirm({
                yes: () => {
                    reloadMedia();
                }
            }, "An error occurred while playing the video, do you want to reload?");
        } else {
            if (errorCounter < 2) {
                const waitTime_ms = Math.max((errorCounter - 1) * 1000, 0);
                console.info("Attempting automatic error recovery since in playlist: " + error, { errorCounter, waitTime_ms });
    
                if (waitTime_ms > 0) {
                    setTimeout(() => {
                        reloadMedia();
                    }, waitTime_ms);
                } else {
                    reloadMedia();
                }
            } else {
                video?.actions?.setIndex(nvi);
            }
        }
    };

    async function getDefaultPlaybackSpeed() {
        const value = (await SettingsBackend.settings())?.object?.playback?.defaultPlaybackSpeed;
        switch (value) {
            case 0: return 0.25;
            case 1: return 0.5;
            case 2: return 0.75;
            case 3: return 1.0;
            case 4: return 1.25;
            case 5: return 1.5;
            case 6: return 1.75;
            case 7: return 2.0;
            case 8: return 2.25;
            default: return 1.0;
        }
    }

    createEffect(async () => {
        if (videoSource$()?.shouldResume !== true) {
            const playbackSpeed = await getDefaultPlaybackSpeed();
            console.log("video changed, resetting playback speed", playbackSpeed);
            setPlaybackSpeed(playbackSpeed);
        }
    });

    createEffect(async () => {
        const videoObj = videoLoaded$();
        if (!videoLoadedIsValid$()) {
            setVideoSource();
            return;
        }

        console.info("set source", { videoObj });
        if (!videoObj || !videoObj.video)
            return;

        let tryFetchSourceAuto = async ()=>{
            await UIOverlay.catchDialogExceptions(async ()=>{
                    let sourceAuto = await DetailsBackend.sourceAuto(videoObj?.url)
                    console.info("source auto", sourceAuto);
                    setVideoSource({
                        url: videoObj?.url,
                        video: sourceAuto.videoIndex,
                        audio: sourceAuto.audioIndex,
                        subtitle: sourceAuto.subtitleIndex,
                        videoIsLocal: sourceAuto.videoIsLocal,
                        audioIsLocal: sourceAuto.audioIsLocal,
                        subtitleIsLocal: sourceAuto.subtitleIsLocal,
                        thumbnailUrl: getBestThumbnail(videoObj?.thumbnails)?.url,
                        isLive: videoObj.isLive,
                        time: video ? untrack(video.startTime) : undefined
                    } as SourceSelected);
                },
                ()=> video?.actions?.closeVideo(),
                ()=> {
                    tryFetchSourceAuto();
                });
        };
        tryFetchSourceAuto();

        /*
        const targetPixelCount = 1280 * 720;
        const targetBitrate = 99999999;
        const hasAudio = !!(videoObj?.video?.audioSources)

        const videosWithCodec = videoObj.video.videoSources.filter((x: any) => !hasAudio || x.container === "video/mp4");
        const audioWithCondec = (videoObj?.video?.audioSources ?? []).filter((x: any) => x.container == "audio/mp4");
        const sourceVideo = videosWithCodec.sort((a: any, b: any) => (Math.abs(targetPixelCount - a.height * a.width)))[0];
        const sourceAudio = audioWithCondec.sort((a: any, b: any) => (Math.abs(a.bitrate - targetBitrate)))?.find((z: any)=>true);
        
        setVideoSource$({
            url: videoObj?.url,
            video: videoObj?.video.videoSources.indexOf(sourceVideo),
            audio: (sourceAudio) ? videoObj?.video.audioSources.indexOf(sourceAudio) : -1,
        } as SourceSelected);*/

    });
    createEffect(() => {
        const videoObj = videoLoaded$();
        updateQualityMenu();
    });

    let lastProgressUrl: string = "";
    let lastProgressPosition = 0;
    function handleVideoProgress(progress: number) {
        if (isScrubbing) {
            return;
        }
        
        const currentUrl = currentVideo$()?.url;
        if(currentUrl) {
            let delta = 0;
            if(lastProgressUrl == currentUrl) {
                delta = progress - lastProgressPosition;
                if(delta > 3000 || delta < 0)
                    delta = 0; //scrub
            }
            lastProgressUrl = currentUrl;
            lastProgressPosition = progress;
            DetailsBackend.watchProgress(currentUrl, progress);
        }
    }

    const handlePositionChanged = (p: Duration) => {
        position = p;
    };
    
    const [currentPlayerHeight$, setCurrentPlayerHeight] = createSignal<number>();
    const [minimizedHeight, setMinimizedHeight] = createSignal(500);
    const [minimizedPosition, setMinimizedPosition] = createSignal({ x: 0, y: 0 });
    const [isFullscreen, setIsFullscreen] = createSignal(false);
    const [isDragging, setIsDragging] = createSignal(false);
    const [dimensions, setDimensions] = createSignal({ width: window.innerWidth, height: window.innerHeight });
    const [videoDimensions, setVideoDimensions] = createSignal({ width: 1920, height: 1080 });
    const desiredMaximizedHeight = createMemo(() => {
        const vd = videoDimensions();
        const desiredMaximumHeight = dimensions().width / vd.width * vd.height;
        return desiredMaximumHeight;
    });
    const [playbackSpeed$, setPlaybackSpeed] = createSignal(1);

    const repositionMinimize = () => {
        const vd = videoDimensions();
        const aspectRatio = vd.width / vd.height;
        let minimizedWidth, minimizedHeight;
    
        if (window.innerWidth < window.innerHeight) {
            minimizedWidth = 0.4 * window.innerWidth;
            minimizedHeight = minimizedWidth / aspectRatio;
        } else {
            minimizedHeight = 0.25 * window.innerHeight;
            minimizedWidth = minimizedHeight * aspectRatio;
        }

        const maxHeight = 500;
        if (minimizedHeight > maxHeight) {
            minimizedHeight = maxHeight;
            minimizedWidth = minimizedHeight * aspectRatio;
        }
        
        setMinimizedPosition({
            x: window.innerWidth - minimizedWidth,
            y: window.innerHeight - minimizedHeight
        });
        setMinimizedHeight(minimizedHeight);

        console.log("repositionMinimize", {minimizedWidth, minimizedHeight, windowInnerWidth: window.innerWidth, windowOuterWidth: window.outerWidth, aspectRatio});
    };

    createEffect((prev) => {
        const updated = video?.state();
        if (updated === VideoState.Minimized && prev !== updated) {
            repositionMinimize();
        }
    });

    const minimize = () => {
        batch(() => {
            if (isMinimized())
                video?.actions.maximizeVideo();
            else {
                if (isFullscreen())
                    document.exitFullscreen();
                video?.actions.minimizeVideo();
            }
        });
    };

    const onMinimize = (e: MouseEvent) => {
        minimize();
        e.stopPropagation();
    };

    const close = () => {
        batch(() => {
            if (isFullscreen())
                document.exitFullscreen();
            video?.actions.closeVideo();
        });
    }

    function onClose(e: MouseEvent) {
        close();
        e.stopPropagation();
    };

    const handleVideoDimensions = (width: number, height: number) => {
        setVideoDimensions({ width: width, height: height });
    };

    const handleIsPlayingChanged = (isPlaying: boolean) => {
        if (isPlaying) {
            console.info("Error counter reset because video is playing", errorCounter);
            errorCounter = 0;
        }
    };

    const handleResize = () => {
        if (!scrollContainerRef) {
            return;
        }

        const dim = { width: scrollContainerRef.clientWidth, height: scrollContainerRef.clientHeight };
        setDimensions(dim);
    };

    const isMinimized = createMemo(() => {
        const res = video?.state() === VideoState.Minimized && !isFullscreen();
        console.log("isMinimized", res);
        return res;
    });

    const shouldShowQueue = createMemo(() => {
        const queueLength = video?.queue()?.length;
        return queueLength !== undefined && queueLength > 1 ? true : false;
    });

    const videoLoadedIsValid$ = createMemo(() => videoLoaded$()?.url === currentVideo$()?.url);
    const recommendationsVisible$ = createMemo(() => videoLoadedIsValid$() && recomPager$.state == "ready" && recomPager$()?.data && recomPager$()?.data.length);
    const shouldHideSideBar = createMemo(() => {
        //TODO: Expand these conditions
        const sideBarVisible = shouldShowQueue() || liveChatWindow$() || recommendationsVisible$();
        return !sideBarVisible || dimensions().width < 1400;
    });

    const mode = createMemo(() => isMinimized() ? VideoMode.Theatre : (video?.desiredMode() ?? VideoMode.Theatre));
    const eventMoved = new Event0();
    const eventRestart = new Event0();
    const maximumColumnWidth = createMemo(() => {
        return shouldHideSideBar() ? 1260 : 1800;
    })
    const standardDimensions = createMemo(() => {
        const d = dimensions();
        //TODO: Reduce number of events?
        //if (mode() === MODE_STANDARD && d.width > maximumColumnWidth())
            eventMoved.invoke();

        const width = (shouldHideSideBar() ? 1.0 : 0.7) * Math.min(maximumColumnWidth(), d.width) - 80;
        const height = Math.min(0.6 * d.height, width / videoDimensions().width * videoDimensions().height);
        return { width, height };
    });

    const minimumMaximumHeight = createMemo(() => {
        if (mode() === VideoMode.Theatre) {
            const isMaximized = video?.state() === VideoState.Maximized;
            const minHeight = minimizedHeight();
            const maxHeight = Math.min(dimensions().height * 0.8, desiredMaximizedHeight());
    
            if (video?.theatrePinned()) {
                return isMaximized
                    ? { minimum: Math.max(200, dimensions().height * 0.35), maximum: maxHeight }
                    : { minimum: minHeight, maximum: minHeight };
            } else {
                return {
                    minimum: isMaximized ? maxHeight : minHeight,
                    maximum: isMaximized ? maxHeight : minHeight
                };
            }
        } else {
            const standardHeight = standardDimensions().height;
            return { minimum: standardHeight, maximum: standardHeight };
        }
    });    

    const minimizedWidth = createMemo(() => {
        const d = videoDimensions();
        return d.width / d.height * minimizedHeight();
    });

    const resizeObserver = new ResizeObserver(entries => {
        handleResize();
    });

    const handleWindowResize = () => {
        repositionMinimize();
    };

    onMount(() => {
        resizeObserver.observe(scrollContainerRef!);
        window.addEventListener('resize', handleWindowResize);
        setDimensions({ width: scrollContainerRef!.clientWidth, height: scrollContainerRef!.clientHeight });
    });

    onCleanup(() => {
        console.log("Cleaning up VideoDetailView")
        resizeObserver.unobserve(scrollContainerRef!);
        window.removeEventListener('resize', handleWindowResize);
        video?.actions.closeVideo();
        resizeObserver.disconnect();
    });

    const name$ = createMemo(() => videoLoadedIsValid$() ? (videoLoaded$()?.name ?? currentVideo$()?.name) : currentVideo$()?.name);
    const viewCount$ = createMemo(() => videoLoadedIsValid$() ? (videoLoaded$()?.viewCount ?? currentVideo$()?.viewCount) : currentVideo$()?.viewCount);
    const dateTime$ = createMemo(() => videoLoadedIsValid$() ? (videoLoaded$()?.dateTime ?? currentVideo$()?.dateTime) : currentVideo$()?.dateTime);
    const url$ = createMemo(() => videoLoadedIsValid$() ? (videoLoaded$()?.url ?? currentVideo$()?.url) : currentVideo$()?.url);
    const shareUrl$ = createMemo(() => videoLoadedIsValid$() ? (videoLoaded$()?.shareUrl ?? videoLoaded$()?.url ?? currentVideo$()?.shareUrl ?? currentVideo$()?.url ?? "") : (currentVideo$()?.shareUrl ?? currentVideo$()?.url));
    const author$ = createMemo(() => videoLoadedIsValid$() ? (videoLoaded$()?.author ?? currentVideo$()?.author) : currentVideo$()?.author);

    const navigate = useNavigate();
    function onClickAuthor() {
        const author = author$();
        if (author) {
            navigate("/web/channel?url=" + encodeURIComponent(author.url), { state: { author } });
            minimize();
        }
    }

    let isLoading = false;
    async function onScrollEnd() {
        if (!isLoading && commentsPager$()?.hasMore) {
            isLoading = true;
            console.log("Fetching next page");
            await commentsPager$()?.nextPage();
            isLoading = false;
        }
    }
    let isLoadingRecom = true;
    async function onScrollEndRecommendations() {
        return;
        if (isLoading || !recomPager$()?.hasMore) {
            return;
        }
        
        isLoadingRecom = true;
        console.log("Fetching next page recom");
        await recomPager$()?.nextPage();
        isLoadingRecom = false;
    }

    let areRepliesLoading = false;
    async function onRepliesScrollEnd() {
        const repliesPager = repliesPager$();
        if (!areRepliesLoading && repliesPager && repliesPager.hasMore) {
            areRepliesLoading = true;
            console.log("Fetching next replies page");
            await repliesPager.nextPage();
            areRepliesLoading = false;
        }
    }

    const [showSettings$, setShowSettings] = createSignal<boolean>(false);
    const [anchor$, setAnchor] = createSignal<Anchor>();
    let lastHideSettingsTime = (new Date()).getTime();
    function onShowSettings() {
        console.log("hideDiff", ((new Date()).getTime() - lastHideSettingsTime))
        if(((new Date()).getTime() - lastHideSettingsTime) > 500)
            setShowSettings(true);
    }
    function onHideSettings() {
        if(showSettings$()) {
            setShowSettings(false);
            lastHideSettingsTime = (new Date()).getTime();
        }
    }
    function setVideoPlayerContainerRef(el: HTMLDivElement) {
        setAnchor(new Anchor(el, showSettings$, AnchorStyle.BottomRight));
    }

    function handleFullscreenChange(isFullscreen: boolean) {
        if (isFullscreen)
            video?.actions.maximizeVideo();
        setIsFullscreen(isFullscreen);
    }

    const handleDrag = (dx: number, dy: number) => {
        setMinimizedPosition(prev => ({ x: prev.x + dx, y: prev.y + dy }));
    };

    const handleResizeHandleDrag = (dx: number, dy: number) => {
        const aspectRatio = videoDimensions().width / videoDimensions().height;
        const currentWidth = minimizedHeight() * aspectRatio;
        const intendedWidthChange = currentWidth + dx - currentWidth;
        const heightAdjustmentForDx = intendedWidthChange / aspectRatio;
        const totalHeightAdjustment = dy + heightAdjustmentForDx;
        setMinimizedHeight(minimizedHeight() + totalHeightAdjustment);
    };

    const handleIsDraggingChanged = (isDragging: boolean) => {
        console.log("isDragging", isDragging);
        setIsDragging(isDragging);
    };

    function getAutoQualityLabel() {
        return ((playerQuality$() && playerQuality$() >= 0 && videoSourceQualities$().length > playerQuality$()) ? 
                            "Auto (" + videoSourceQualities$()[playerQuality$()].width + "x" + videoSourceQualities$()[playerQuality$()].height +")" : "Auto");
    }

    const [settingsDialogMenu$, setSettingsDialogMenu] = createSignal<Menu>({ title: "", items: [] });
    function updateQualityMenu() {
        setSettingsDialogMenu({
            title: "Playback settings",
            items: [/*
                {
                    key: "Playback speed",
                    value: "1x",
                    type: "group"
                } as MenuItem,
                (localVideoSources$() && localVideoSources$().length > 0 && videoSource$()) ? {
                    type: "seperator"
                } as MenuItem : undefined,*/
                (localVideoSources$() && localVideoSources$().length > 0 && videoSource$()) ? {
                    key: "Offline Video",
                    value: (videoSource$() && videoSource$()?.videoIsLocal) ? localVideoSources$()[videoSource$()!.video]?.name : undefined,
                    type: "group",
                    subMenu: {
                        title: "Video sources",
                        items: localVideoSources$().map(x => {
                            return {
                                name: x.name,
                                value: x,
                                type: "option",
                                onSelected: (val: any) => {
                                    const videoObj = videoLoaded$();
                                    const originalSource = videoSource$();
                                    setVideoSource({
                                        url: videoObj?.url,
                                        video: localVideoSources$().indexOf(x),
                                        videoIsLocal: true,
                                        audio: originalSource!.audio,
                                        audioIsLocal: originalSource!.audioIsLocal,
                                        subtitle: originalSource?.subtitle,
                                        subtitleIsLocal: originalSource?.subtitleIsLocal,
                                        isLocal: true,
                                        thumbnailUrl: getBestThumbnail(videoObj?.thumbnails)?.url,
                                        isLive: videoObj?.isLive ?? false,
                                        shouldResume: true
                                    } as SourceSelected);
                                },
                                isSelected: videoSource$()?.videoIsLocal && localVideoSources$().indexOf(x) == videoSource$()?.video
                            } as IMenuItemOption
                        })
                    }
                } as IMenuItemGroup : undefined,
                (localAudioSources$() && localAudioSources$().length > 0 && audioSources$()) ? {
                    key: "Offline Audio",
                    value: (videoSource$() && videoSource$()?.audioIsLocal) ? localAudioSources$()[videoSource$()!.audio]?.name : undefined,
                    type: "group",
                    subMenu: {
                        title: "Audio sources",
                        items: localAudioSources$().map(x => {
                            return {
                                name: x.name,
                                value: x,
                                type: "option",
                                onSelected: (val: any) => {
                                    const videoObj = videoLoaded$();
                                    const originalSource = videoSource$();
                                    setVideoSource({
                                        url: videoObj?.url,
                                        video: originalSource?.video,
                                        videoIsLocal: originalSource?.videoIsLocal,
                                        audio: localAudioSources$().indexOf(x),
                                        audioIsLocal: true,
                                        subtitle: originalSource?.subtitle,
                                        subtitleIsLocal: originalSource?.subtitleIsLocal,
                                        thumbnailUrl: getBestThumbnail(videoObj?.thumbnails)?.url,
                                        isLive: videoObj?.isLive ?? false,
                                        shouldResume: true
                                    } as SourceSelected);
                                },
                                isSelected: videoSource$()?.audioIsLocal && localAudioSources$().indexOf(x) == videoSource$()?.audio
                            } as IMenuItemOption
                        })
                    }
                } as MenuItem : undefined,
                (localSubtitleSources$() && localSubtitleSources$().length > 0 && subtitleSources$()) ? {
                    key: "Offline Subtitle",
                    value: (videoSource$() && videoSource$()?.subtitleIsLocal && videoSource$()!.subtitle >= 0) ? localSubtitleSources$()[videoSource$()!.subtitle]?.name : "None",
                    type: "group",
                    subMenu: {
                        title: "Subtitle sources",
                        items: [
                            {
                                name: "None",
                                value: -1,
                                type: "option",
                                onSelected: (val: any) => {
                                    const videoObj = videoLoaded$();
                                    const originalSource = videoSource$();
                                    setVideoSource({
                                        url: videoObj?.url,
                                        video: originalSource?.video,
                                        videoIsLocal: originalSource?.videoIsLocal,
                                        audio: originalSource?.audio,
                                        audioIsLocal: originalSource?.audioIsLocal,
                                        subtitle: -1,
                                        subtitleIsLocal: false,
                                        thumbnailUrl: getBestThumbnail(videoObj?.thumbnails)?.url,
                                        isLive: videoObj?.isLive ?? false,
                                        shouldResume: true
                                    } as SourceSelected);
                                },
                                isSelected: videoSource$()?.audio == -1
                            } as IMenuItemOption,
                            ... localSubtitleSources$().map(x => {
                                return {
                                    name: x.name,
                                    value: x,
                                    type: "option",
                                    onSelected: (val: any) => {
                                        const videoObj = videoLoaded$();
                                        const originalSource = videoSource$();
                                        setVideoSource({
                                            url: videoObj?.url,
                                            video: originalSource?.video,
                                            videoIsLocal: originalSource?.videoIsLocal,
                                            audio: originalSource?.audio,
                                            audioIsLocal: originalSource?.audioIsLocal,
                                            subtitle: localSubtitleSources$().indexOf(x),
                                            subtitleIsLocal: true,
                                            thumbnailUrl: getBestThumbnail(videoObj?.thumbnails)?.url,
                                            isLive: videoObj?.isLive ?? false,
                                            shouldResume: true
                                        } as SourceSelected);
                                    },
                                    isSelected: videoSource$()?.subtitleIsLocal && localSubtitleSources$().indexOf(x) == videoSource$()?.subtitle
                                } as IMenuItemOption
                            })
                        ]
                    }
                } as MenuItem : undefined,
                {
                    type: "seperator"
                } as MenuItem,
                (videoSources$() && videoSources$().length > 0 && videoSource$()) ? {
                    key: "Video Sources (" + (videoSources$().length) + ")",
                    value: (videoSource$() && !videoSource$()?.videoIsLocal) ? videoSources$()[videoSource$()!.video]?.name : undefined,
                    type: "group",
                    subMenu: {
                        title: "Video sources",
                        items: videoSources$().map(x => {
                            return {
                                name: x.name,
                                value: x,
                                type: "option",
                                onSelected: (val: any) => {
                                    const videoObj = videoLoaded$();
                                    const originalSource = videoSource$();
                                    setVideoSource({
                                        url: videoObj?.url,
                                        video: videoObj?.video.videoSources.indexOf(x),
                                        videoIsLocal: false,
                                        audio: originalSource?.audio,
                                        audioIsLocal: originalSource?.audioIsLocal,
                                        subtitle: originalSource?.subtitle,
                                        subtitleIsLocal: originalSource?.subtitleIsLocal,
                                        thumbnailUrl: getBestThumbnail(videoObj?.thumbnails)?.url,
                                        isLive: videoObj?.isLive ?? false,
                                        shouldResume: true
                                    } as SourceSelected);
                                },
                                isSelected: !videoSource$()?.videoIsLocal && videoSources$().indexOf(x) == videoSource$()?.video
                            } as IMenuItemOption
                        })
                    }
                } as IMenuItemGroup : undefined,
                (videoSourceQualities$() && videoSourceQualities$().length > 0) ? {
                    key: "Video Quality (" + (videoSourceQualities$().length) + ")",
                    value: (videoQuality$() == -1) ? "Auto" : 
                        ((videoQuality$()) ? (videoSourceQualities$()[videoQuality$()].width + "x" + videoSourceQualities$()[videoQuality$()].height) : undefined),
                    type: "group",
                    subMenu: {
                        title: "Stream qualities",
                        items: [{
                            name: "Auto",
                            value: "Auto",
                            type: "option",
                            onSelected: (val: any) => {
                                setVideoQuality(-1);
                            },
                            isSelected: videoQuality$() == -1
                        } as IMenuItemOption].concat( 
                        (videoSourceQualities$().map((x: any) => {
                            const index = videoSourceQualities$().indexOf(x);
                            return {
                                name: x.name,
                                value: x.width + "x" + x.height,
                                type: "option",
                                onSelected: (val: any) => {
                                    setVideoQuality(index);
                                },
                                isSelected: videoQuality$() == index
                            } as IMenuItemOption
                        })))
                    }
                } : undefined,
                (audioSources$() && audioSources$().length > 0 && videoSource$()) ? {
                    key: "Audio Sources (" + (audioSources$().length) + ")",
                    value: (videoSource$() && !videoSource$()?.audioIsLocal) ? audioSources$()[videoSource$()!.audio]?.name : undefined,
                    type: "group",
                    subMenu: {
                        title: "Audio sources",
                        items: audioSources$().map(x => {
                            return {
                                name: formatAudioSourceName(x),
                                value: x,
                                type: "option",
                                onSelected: (val: any) => {
                                    const videoObj = videoLoaded$();
                                    const originalSource = videoSource$();
                                    setVideoSource({
                                        url: videoObj?.url,
                                        video: originalSource?.video,
                                        videoIsLocal: originalSource?.videoIsLocal,
                                        audio: videoObj?.video.audioSources.indexOf(x),
                                        audioIsLocal: false,
                                        subtitle: originalSource?.subtitle,
                                        subtitleIsLocal: originalSource?.subtitleIsLocal,
                                        thumbnailUrl: getBestThumbnail(videoObj?.thumbnails)?.url,
                                        isLive: videoObj?.isLive ?? false,
                                        shouldResume: true
                                    } as SourceSelected);
                                },
                                isSelected: !videoSource$()?.audioIsLocal && audioSources$().indexOf(x) == videoSource$()?.audio
                            } as IMenuItemOption
                        })
                    }
                } as MenuItem : undefined,
                (subtitleSources$() && subtitleSources$().length > 0 && videoSource$()) ? {
                    key: "Subtitle Sources (" + (subtitleSources$().length) + ")",
                    value: (videoSource$() && !videoSource$()?.subtitleIsLocal && videoSource$()!.subtitle >= 0) ? subtitleSources$()[videoSource$()!.subtitle]?.name : "None",
                    type: "group",
                    subMenu: {
                        title: "Subtitle sources",
                        items: [
                            {
                                name: "None",
                                value: -1,
                                type: "option",
                                onSelected: (val: any) => {
                                    const videoObj = videoLoaded$();
                                    const originalSource = videoSource$();
                                    setVideoSource({
                                        url: videoObj?.url,
                                        video: originalSource?.video,
                                        videoIsLocal: originalSource?.videoIsLocal,
                                        audio: originalSource?.audio,
                                        audioIsLocal: originalSource?.audioIsLocal,
                                        subtitle: -1,
                                        subtitleIsLocal: false,
                                        thumbnailUrl: getBestThumbnail(videoObj?.thumbnails)?.url,
                                        isLive: videoObj?.isLive ?? false,
                                        shouldResume: true
                                    } as SourceSelected);
                                },
                                isSelected: videoSource$()?.subtitle == -1
                            } as IMenuItemOption,
                            ... subtitleSources$().map(x => {
                                return {
                                    name: x.name,
                                    value: x,
                                    type: "option",
                                    onSelected: (val: any) => {
                                        const videoObj = videoLoaded$();
                                        const originalSource = videoSource$();
                                        setVideoSource({
                                            url: videoObj?.url,
                                            video: originalSource?.video,
                                            videoIsLocal: originalSource?.videoIsLocal,
                                            audio: originalSource?.audio,
                                            audioIsLocal: originalSource?.audioIsLocal,
                                            subtitle: videoObj?.subtitles.indexOf(x),
                                            subtitleIsLocal: false,
                                            thumbnailUrl: getBestThumbnail(videoObj?.thumbnails)?.url,
                                            isLive: videoObj?.isLive ?? false,
                                            shouldResume: true
                                        } as SourceSelected);
                                    },
                                    isSelected: !videoSource$()?.subtitleIsLocal && subtitleSources$().indexOf(x) == videoSource$()?.subtitle
                                } as IMenuItemOption
                            })
                        ]
                    }
                } as MenuItem : undefined,
                {
                    key: "Playback Speed",
                    value: `${playbackSpeed$().toFixed(2)}x`,
                    type: "group",
                    subMenu: {
                        title: "Playback Speed",
                        items: [0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 2.25].map(s => {
                            return {
                                name: `${s.toFixed(2)}x`,
                                value: s,
                                type: "option",
                                onSelected: () => {
                                    setPlaybackSpeed(s);
                                },
                                isSelected: playbackSpeed$() == s
                            } as IMenuItemOption;
                        })
                    }
                } as MenuItem
            ]
        } as Menu);
    }

    const mat = 0.15; //minimize animation time
    /*const transition = createMemo(() => {
        const dragging = isDragging();
        return dragging
            ? undefined
            : `width ${mat}s ease, height ${mat}s ease, top ${mat}s ease, left ${mat}s ease`;
    });*/

    const metadata$ = createMemo(() => {
        const tokens = [];

        const viewCount = viewCount$();
        if (viewCount && viewCount > 0) {
            tokens.push(toHumanNumber(viewCount) + " views");
        }

        if (tokens.length < 1) {
            return undefined;
        }

        const date = dateTime$();
        if (date) {
            tokens.push(toHumanNowDiffString(date));
            //tokens.push(toHumanNowDiffStringMinDay(date));
        }

        return tokens.join(" • ")
    });

    async function download() {
        UIOverlay.overlayDownload(url$());
    }

    const handleDescriptionClick = (ev: MouseEvent) => {
        setFullDescriptionVisible(!fullDescriptionVisible$());
        if (!fullDescriptionVisible$()) {
            descriptionContainerRef?.scrollIntoView();
        }
        ev.stopImmediatePropagation();
        ev.preventDefault();
    };

    const handleDescriptionLinkClick = async (ev: MouseEvent) => {
        ev.preventDefault();
        const url = (ev.target as HTMLLinkElement)?.href;
        if (!url) {
          return;
        }
    
        console.log(`Open URL: ${url}`);
        // TODO: Check if should be opened within the app
        await LocalBackend.open(url);
      };
    
    const handleTimestampClick = (timestamp: string) => {
        console.log(`Timestamp clicked: ${timestamp}`);
        // TODO: Implement your timestamp handling logic here

        const source = videoSource$();
        if (!source) {
            return;
        }

        let time_s: number;
        const tokens = timestamp.split(':').map(v => Number.parseFloat(v.trim()));
        if (tokens.length === 1) {
            time_s = tokens[0];
        } else if (tokens.length === 2) {
            time_s = tokens[0] * 60 + tokens[1];
        } else if (tokens.length === 3) {
            time_s = tokens[0] * 60 * 60 + tokens[1] * 60 + tokens[2];
        } else {
            return;
        }

        setVideoSource({
            ... source,
            time: Duration.fromMillis(1000 * time_s)
        });
    };

    const description = createMemo(() => {
        return sanitzeHtml(videoLoadedIsValid$() ? (videoLoaded$()?.description ?? "") : "") ?? "";
    });

    const handleContainerClick = (event: MouseEvent) => {
        const target = event.target as HTMLElement;
        if (target.tagName === 'A') {
            const href = target.getAttribute('href');
            if (href === '#') {
                event.preventDefault();
                const timestamp = target.getAttribute('data-timestamp');
                if (timestamp) {
                    handleTimestampClick(timestamp);
                }
            } else {
                handleDescriptionLinkClick(event);
            }
        }
    };

    function verifyToggle() {
        return true;
    }

    const isThumbnailValid$ = createMemo(() => author$()?.thumbnail && author$()?.thumbnail.length);
    const [resumePosition$] = createResourceDefault(createMemo(() => {
        return { 
            currentVideo: currentVideo$(),
            videoLoadedIsValid: videoLoadedIsValid$() 
        };
    }), async (obj) => {
        const url = obj.currentVideo?.url;
        return obj.videoLoadedIsValid && url ? Duration.fromMillis(await HistoryBackend.getHistoricalPosition(url)) : undefined;
    });

    function sendToDevice(dev: SyncDevice) {
        const url = url$() ?? videoLoaded$()?.url;
        console.log("Send to device", {publicKey: dev.publicKey, url, lastProgressPosition});
        if(url)
            SyncBackend.sendToDevice(dev.publicKey, url, lastProgressPosition / 1000);
    }

    const [sideBarHidden$, setSideBarHidden] = createSignal(true);
    const [sideBarAutoHidden$, setSideBarAutoHidden] = createSignal(false);
    let showSideBarTimeout: NodeJS.Timeout | undefined = undefined;
    let hideSideBarTimeout: NodeJS.Timeout | undefined = undefined;
    const clearShowSideBarTimeout = () => {
        clearTimeout(showSideBarTimeout);
        showSideBarTimeout = undefined;
    };
    const clearHideSideBarTimeout = () => {
        clearTimeout(hideSideBarTimeout);
        hideSideBarTimeout = undefined;
    };
    const resetHideSideBarTimeout = () => {
        clearHideSideBarTimeout();
        hideSideBarTimeout = setTimeout(() => {
            setSideBarHidden(true);
            clearShowSideBarTimeout();
            setSideBarAutoHidden(true);
        }, 3000);
    };
    const handleSideBarMove = () => {
        if (!sideBarHidden$()) {
            resetHideSideBarTimeout();
        }

        if (showSideBarTimeout) {
            return;
        }

        showSideBarTimeout = setTimeout(() => {
            batch(() => {
                setSideBarHidden(false);
                clearShowSideBarTimeout();
                resetHideSideBarTimeout();
            });
        }, 350);
    };
    const handleSideBarMouseLeave = () => {
        batch(() => {
            setSideBarHidden(true);
            clearShowSideBarTimeout();
            clearHideSideBarTimeout();
            setSideBarAutoHidden(false);
        });
    };

    const videoState = video?.state;
    if (videoState) {
        createEffect(on(videoState, () => {
            setSideBarHidden(true);
        }));
    }

    const renderRecommendation = (item: Accessor<IPlatformVideo>) => {
        const bestThumbnail = createMemo(() => {
            const v = item();
            return (v && (v?.thumbnails?.sources?.length ?? 0) > 0) ? v!.thumbnails.sources[Math.max(0, v!.thumbnails.sources.length - 1)] : null;
        });

        return (
            <div style={{
                "margin-bottom": "8px",
                "margin-right": "12px",
                "display": "flex",
                "flex-direction": "row",
                "width": "calc(100% - 12px)",
                "align-items": "center",
                "overflow": "hidden",
                "gap": "8px",
                "border-radius": "8px"
            }} onClick={()=>{video?.actions.openVideo(item())}}>
                <img src={bestThumbnail()?.url} style="border-radius: 3px; height: 112px; width: 200px; cursor: pointer;" referrerPolicy='no-referrer' />
                <div style="display: flex; flex-direction: column; flex-grow: 1; overflow: hidden; cursor: pointer; margin-left: 10px;">
                    <div class={styles.recommendationItemTitle}>{item()?.name}</div>
                    <div class={styles.recommendationItemAuthor} style="margin-top: 6px">{item()?.author?.name}</div>
                    <div class={styles.recommendationItemAuthor} style="margin-top: 6px"><Show when={(item()?.viewCount ?? 0) > 0}>{toHumanNumber(item()?.viewCount)} views • </Show>{toHumanNowDiffString(item()?.dateTime)}</div>
                </div>
            </div>
        );
    };

    return (
        <div ref={containerRef} class={styles.container} style={{
            "top": isMinimized() ? `${minimizedPosition().y}px` : "0px",
            "left": isMinimized() ? `${minimizedPosition().x}px` : "0px",
            "height": isMinimized() ? `${minimizedHeight()}px` : undefined,
            "width": isMinimized() ? `${minimizedWidth()}px` : undefined,
            //"transition": transition(),
            "display": video?.state() === VideoState.Maximized || video?.state() === VideoState.Minimized ? "flex" : "none",
            "flex-direction": "row"
        }} classList={{ [styles.minimized]: isMinimized() }}>
            <Show when={video?.state() === VideoState.Maximized && video?.desiredMode() === VideoMode.Standard}>
                <SideBar alwaysMinimized={true} onNavigate={() => minimize()}></SideBar>
            </Show>
            <ScrollContainer ref={scrollContainerRef} scrollToTopButton={true} style={{ 
                "overflow-y": isMinimized() ? "hidden" : "scroll", 
                width: video?.state() === VideoState.Maximized ? "100vw" : undefined
            }}>
                <Show when={video?.state() !== VideoState.Minimized && mode() !== VideoMode.Theatre}>
                    <div style="display: flex; flex-direction: row; justify-content: center; align-items: center">
                        <div style={{
                            "width": "calc(100% - 80px)",
                            "display": "flex",
                            "flex-direction": "row",
                            "margin-left": "40px",
                            "margin-right": "40px",
                            "max-width": `${maximumColumnWidth() - 80}px`,
                            "margin-top": "16px",
                            "margin-bottom": "16px",
                            "align-items": "center"
                        }}>
                            <TransparentIconButton icon={ic_chevron_down} onClick={() => minimize()} style={{"flex-shrink":0, "width": "40px", "height": "40px"}} />
                            <Show when={false /*Search from video=>search seems fundamentally broken*/}>
                                <SearchBar onSearch={(q, c) => {navigate("/web/search?q=" + encodeURIComponent(q) + "&type=" + c);minimize(); }} style={{ "flex-grow": 1, "max-width": "700px", "margin-left": "24px" }} defaultSearchType={ContentType.MEDIA} />
                            </Show>
                            <div style="flex-grow: 1"></div>
                            <TransparentIconButton icon={ic_close} onClick={() => close()} style={{"flex-shrink":0, "width": "40px", "height": "40px"}} />
                        </div>
                    </div>

                </Show>
                <StickyShrinkOnScrollContainer outerContainerRef={scrollContainerRef}
                    minimumHeight={minimumMaximumHeight().minimum}
                    maximumHeight={minimumMaximumHeight().maximum}
                    heightChanged={(newHeight) => setCurrentPlayerHeight(newHeight)}
                    sticky={mode() === VideoMode.Theatre && video?.theatrePinned() || isMinimized()}>
                    <div style="height: 100%;" ref={videoContainer}>
                        <VideoPlayerView ref={setVideoPlayerContainerRef}
                            video={videoLoaded$()}
                            chapters={((!isMinimized()) ? videoChapters$() : undefined) ?? undefined}
                            eventMoved={eventMoved}
                            eventRestart={eventRestart}
                            resumePosition={resumePosition$()}
                            onProgress={handleVideoProgress}
                            onPositionChanged={handlePositionChanged}
                            onVideoDimensionsChanged={handleVideoDimensions} 
                            onIsPlayingChanged={handleIsPlayingChanged}
                            source={videoSource$()}
                            sourceQuality={videoQuality$()}
                            onPlayerQualityChanged={(number)=>{setPlayerQuality(number)}}
                            onSettingsDialog={(ev) => onShowSettings()} 
                            lockOverlay={showSettings$()} 
                            volume={video?.volume()}
                            playbackSpeed={playbackSpeed$()}
                            onVolumeChanged={(volume) => video?.actions?.setVolume?.(volume)}
                            onFullscreenChange={handleFullscreenChange}
                            onEnded={handleEnded}
                            onError={handleError}
                            onSetScrubbing={(scrubbing) => {
                                isScrubbing = scrubbing;
                            }}
                            onVerifyToggle={verifyToggle}
                            buttons={
                                <>
                                    <Show when={!isMinimized() && mode() === VideoMode.Theatre}>
                                        <img src={video?.theatrePinned() ? pinned_fill : pinned} class={styles.pinned} alt="pin theatre" onClick={() => video?.actions?.setTheatrePinned(!video?.theatrePinned())} />
                                    </Show>
                                    <Show when={!shouldShowQueue()}>
                                        <img src={video?.repeat() ? loop_active : loop_inactive} class={styles.loop} alt="loop" onClick={() => video?.actions?.setRepeat(!video?.repeat())} />
                                    </Show>
                                </>
                            }
                            handleTheatre={() => {
                                if (mode() === VideoMode.Standard) {
                                    video?.actions?.setDesiredMode(VideoMode.Theatre);
                                } else {
                                    video?.actions?.setDesiredMode(VideoMode.Standard);
                                }
                            }}
                            handleEscape={() => {
                                if (video?.state() === VideoState.Maximized) {
                                    minimize();
                                }
                            }}
                            handleMinimize={() => {
                                minimize();
                            }}
                            leftButtonContainerStyle={isMinimized() ? {
                                "width": "calc(100% - 48px)"
                            } : {}}
                            rightButtonContainerStyle={{
                                "display": (isMinimized() ? "none" : undefined)
                            }}
                            style={{
                                ... (mode() === VideoMode.Standard) ? {
                                    width: standardDimensions().width + "px",
                                    height: standardDimensions().height + "px",
                                    "margin-left": dimensions().width > maximumColumnWidth() ? `${(dimensions().width - maximumColumnWidth()) / 2 + 40}px` : "40px",
                                    "margin-right": "40px",
                                    "border-radius": "10px",
                                    "overflow": "hidden"
                                } : {},
                                //transition: "width 0.15s ease, height 0.15s ease, margin-left 0.15s ease, margin-right 0.15s ease, border-radius 0.15s ease"
                            }}
                            loaderUI={
                                <>
                                    <img src={ic_minimize} class={styles.minimize} alt="minimize" onClick={onMinimize} style={{ transform: isMinimized() ? "rotate(-180deg)" : undefined }} />
                                    <img src={ic_close} class={styles.close} alt="close" onClick={onClose} />
                                </>
                            }
                        >
                            <img src={ic_minimize} class={styles.minimize} alt="minimize" onClick={onMinimize} style={{ transform: isMinimized() ? "rotate(-180deg)" : undefined }} />
                            <img src={ic_close} class={styles.close} alt="close" onClick={onClose} />
                            <Show when={isMinimized()}>
                                <DragArea class={styles.draggable} onDrag={handleDrag} onIsDraggingChanged={handleIsDraggingChanged} />
                                <ResizeHandle class={styles.resizable} onResize={handleResizeHandleDrag} onIsResizingChanged={handleIsDraggingChanged} />
                            </Show>
                        <SettingsMenu
                            style={{ position: "absolute", right: "60px", bottom: "125px", "max-height": "calc(100% - 200px)" }}
                            menu={settingsDialogMenu$()}
                            show={showSettings$() ?? false}
                            onHide={onHideSettings} />
                        </VideoPlayerView>
                    </div>
                </StickyShrinkOnScrollContainer>
                <div style={{
                    "padding-bottom": "10px",
                    display: "flex",
                    "justify-content": "center"
                }}>
                    <div class={styles.containerContainer} style={{
                        "max-width": `${maximumColumnWidth()}px`
                    }}>
                        <div class={styles.containerLeft} style={{ width: shouldHideSideBar() ? "100%" : undefined }}>
                            <div class={styles.headerContainer}>
                                <div class={styles.containerTitle}>
                                    <div class={styles.title} ondragstart={(ev)=>preventDragDrop(ev)}>{name$()}</div>
                                    <div class={styles.metadata}>{metadata$()}</div>
                                </div>
                                <div class={styles.buttonList}>
                                    <div style="margin: 7px;;">
                                        <RatingView rating={videoLoaded$()?.rating} />
                                    </div>
                                    <Show when={(StateSync.devicesOnline$()?.length ?? 0) > 0}>
                                        <PillButton icon={ic_sync} text="Send To Device" onClick={() => { UIOverlay.overlaySelectOnlineSyncDevice("Send to Device", "Select a device to send the video to.", (dev) => sendToDevice(dev)) }} />
                                    </Show>
                                    <PillButton icon={share} text="Share" onClick={() => { UIOverlay.overlayShare(shareUrl$()) }} />
                                    <Show when={!videoLoaded$.loading && !(videoLoaded$()?.isLive === true)}>
                                        <PillButton icon={iconDownload} text="Download" onClick={() => { download() }} />
                                    </Show>
                                    <PillButton icon={add} text="Add to" onClick={() => { UIOverlay.overlayAddToPlaylist(videoLoaded$()!, ()=>{}) }} />
                                </div>
                            </div>
                            <div class={styles.authorContainer}>
                                <Show when={isThumbnailValid$()}>
                                    <img src={author$()?.thumbnail} class={styles.author} alt="author" onClick={onClickAuthor} referrerPolicy='no-referrer' />
                                </Show>
                                <div class={styles.authorDescription} style={{
                                    "margin-left": isThumbnailValid$() ? undefined : "40px"
                                }}>
                                    <div class={styles.authorName} onClick={onClickAuthor}>{author$()?.name}</div>
                                    <div style="flex-grow:1;"></div>
                                    <Show when={(author$()?.subscribers ?? 0) > 0}>
                                        <div class={styles.authorMetadata} onClick={onClickAuthor}>{toHumanNumber(author$()?.subscribers)} subscribers</div>
                                        <div style="flex-grow:1;"></div>
                                    </Show>
                                </div>

                                <SubscribeButton author={author$()?.url} style={{"margin-top": "29px"}} />

                                <div style="flex-grow: 1;">
                                </div>

                                <div class={styles.authorRightButtonsContainer}>
                                    <Show when={false}>
                                        <CustomButton
                                            text="Support"
                                            icon={donate}
                                            background="linear-gradient(331deg, rgba(219, 139, 19, 0.04) 3.28%, rgba(251, 198, 119, 0.04) 91.95%);"
                                            border="1px solid var(--Linear, #DB8B13)"
                                            style={{
                                                color: "#FBC677"
                                            }} />
                                        <CustomButton
                                            text="Visit Store"
                                            icon={store}
                                            background="linear-gradient(331deg, rgba(219, 139, 19, 0.04) 3.28%, rgba(251, 198, 119, 0.04) 91.95%);"
                                            border="1px solid var(--9-ae-151, #9AE151)"
                                            style={{
                                                color: "#9AE151"
                                            }} />
                                    </Show>
                                </div>
                            </div>
                            <div class={styles.descriptionContainer} ref={descriptionContainerRef}>
                                <div class={styles.description} style={{
                                    "max-height": fullDescriptionVisible$() ? undefined : "200px"
                                }} innerHTML={description()} onClick={(ev) => handleContainerClick(ev)} ondragstart={(ev)=>preventDragDrop(ev)} />
                                <div class={styles.showMoreShowLessFade} style={{
                                    "display": fullDescriptionVisible$() ? "none" : "block"
                                }}>
                                </div>
                                <div class={styles.showMoreShowLess} onClick={(ev) => handleDescriptionClick(ev)}>
                                    Show {fullDescriptionVisible$() ? "less" : "more"}
                                </div>
                            </div>

                            <Show when={shouldHideSideBar() && shouldShowQueue()}>
                                <PlaybackQueue index={video?.index() ?? 0}
                                    scrollContainerStyle={{
                                        "max-height": "300px"
                                    }}
                                    videos={video?.queue() ?? []}
                                    repeat={video?.repeat()}
                                    shuffle={video?.shuffle()}
                                    style={{
                                        "margin-right": "40px",
                                        "margin-left": "40px",
                                        "margin-top": "28px"
                                    }} onVideoClick={(v) => {
                                        const index = video?.queue()?.findIndex(x => x === v);
                                        if (index !== undefined) {
                                            video?.actions?.setIndex(index);
                                        }
                                    }} onShuffleClick={() => {
                                        video?.actions?.setShuffle(!video?.shuffle());
                                    }} onRepeatClick={() => {
                                        video?.actions?.setRepeat(!video?.repeat());
                                    }} onIndexMoved={(index1, index2) => {
                                        if (video?.index() === index1)
                                            video?.actions.setIndex(index2);
                                        else if (video?.index() === index2)
                                            video?.actions.setIndex(index1);
                                    }} onVideoRemoved={(index) => {
                                        const v = video;
                                        const i = v?.index();
                                        const q = v?.queue();
                                        if (!v || i === undefined || !q) {
                                            return;
                                        }

                                        video?.actions.setQueue(i > index ? i - 1 : i, q.slice(0, index).concat(q.slice(index + 1)), video?.repeat(), video?.shuffle());
                                    }} />
                            </Show>

                            <Show when={shouldHideSideBar() && videoLoadedIsValid$() && liveChatWindow$()}>
                                <Show when={liveChatWindow$()?.error}>
                                    <div class={styles.liveChatError}>
                                        {liveChatWindow$()?.error}
                                    </div>
                                </Show>
                                <Show when={!liveChatWindow$()?.error && liveChatWindow$()?.url}>
                                    <LiveChatRemoteWindow descriptor={liveChatWindow$()} style={{
                                        'margin-top': '30px',
                                        "width": "calc(100% - 80px)",
                                        "margin-right": "40px",
                                        "margin-left": "40px"
                                    }} />
                                </Show>
                            </Show>

                            <Show when={shouldHideSideBar() && recommendationsVisible$()}>
                                <div style={{
                                    "width": "calc(100% - 80px)",
                                    "margin-right": "40px",
                                    "margin-left": "40px",
                                    "margin-top": "28px"
                                }} class={styles.recommendations}>
                                    <HorizontalScrollContainer ref={horizontalScrollRecommendContainerRef} subtle={true}>
                                        <HorizontalFlexibleArrayList outerContainerRef={horizontalScrollRecommendContainerRef}
                                            onEnd={onScrollEndRecommendations}
                                            addedItems={recomPager$()?.addedItemsEvent}
                                            modifiedItems={recomPager$()?.modifiedItemsEvent}
                                            removedItems={recomPager$()?.removedItemsEvent}
                                            items={recomPager$()?.data}
                                            builder={(_, item) => {
                                                return (
                                                    <VideoThumbnailView 
                                                        style={{"margin-bottom": "10px", "width": "236px", "box-sizing": "border-box", "padding-right": "16px" }}
                                                        imageStyle={{"height": "124px", "width": "220px"}}
                                                        video={item()}
                                                        onClick={()=>{video?.actions.openVideo(item())}}
                                                    />
                                                );
                                            }} />
                                    </HorizontalScrollContainer>
                                </div>
                            </Show>

                            <Switch>
                                <Match when={!videoLoadedIsValid$() || commentsPager$.state !== "ready"}>
                                    <div style="width: 100%; height: 100px; margin: 30px; display: grid; justify-items: center;">
                                        <Loader />
                                    </div>
                                </Match>
                                <Match when={videoLoadedIsValid$() && commentsPager$.state == "ready" && !videoSource$()?.isLive}>
                                    <>
                                        <Show when={commentsPager$() && (commentsPager$().data?.length ?? 0) > 0}>
                                            <div class={styles.commentHeader}>
                                                <div class={styles.commentHeaderTitle}>Comments</div>
                                                <Show when={false}>
                                                    <ToggleButtonGroup items={[ "Polycentric", "Platform" ]} 
                                                        defaultSelectedItem={activeCommentSection$() === COMMENT_SECTION_POLYCENTRIC ? "Polycentric" : "Platform" } 
                                                        onItemChanged={(i) => setActiveCommentSection(i === "Polycentric" ? COMMENT_SECTION_POLYCENTRIC : COMMENT_SECTION_PLATFORM)} 
                                                        style={{ "margin-left": "24px" }} />
                                                </Show>
                                            </div>
                                        </Show>
                                        <Show when={activeCommentSection$() === COMMENT_SECTION_POLYCENTRIC}>
                                            <div class={styles.addComment}>
                                                Add a comment
                                            </div>
                                        </Show>
                                        <Show when={!commentsPager$()?.error}>
                                            <FlexibleArrayList outerContainerRef={scrollContainerRef}
                                                onEnd={onScrollEnd}
                                                addedItems={commentsPager$()?.addedItemsEvent}
                                                modifiedItems={commentsPager$()?.modifiedItemsEvent}
                                                removedItems={commentsPager$()?.removedItemsEvent}
                                                items={commentsPager$()?.data}
                                                builder={(_, item) =>
                                                    <CommentView
                                                        style={{
                                                            "padding-left": "40px",
                                                            "padding-right": "40px",
                                                            "padding-top": "32px"
                                                        }}
                                                        editable={activeCommentSection$() === COMMENT_SECTION_POLYCENTRIC}
                                                        comment={item()?.object}
                                                        onClick={(ev) => handleContainerClick(ev)}
                                                        onRepliesClicked={async () => {
                                                            const parent = item();
                                                            if (!parent) {
                                                                return;
                                                            }

                                                            batch(async () => {
                                                                setRepliesParents([ parent.object ]);
                                                                setRepliesVisible(true);
                                                            });

                                                            setRepliesPager(await DetailsBackend.repliesPager(parent.refID ?? ""));
                                                        }} />
                                                }
                                                /*overscan={10}*/ />
                                        </Show>
                                        <Show when={commentsPager$()?.error}>
                                            <div style="margin: 40px; text-align: center;">
                                                {commentsPager$()?.error.title}
                                            </div>
                                        </Show>
                                    </>
                                </Match>
                            </Switch>
                        </div>
                        <Show when={!shouldHideSideBar()}>
                            <div class={styles.containerRight} style={{
                                ... (mode() === VideoMode.Standard) ? {
                                    "margin-top": `-${standardDimensions().height}px`
                                } : {},
                                ... (mode() === VideoMode.Theatre) ? {
                                    "margin-top": "28px"
                                } : {}
                            }}>
                                <Show when={shouldShowQueue()}>
                                    <PlaybackQueue index={video?.index() ?? 0}
                                        videos={video?.queue() ?? []}
                                        repeat={video?.repeat()}
                                        shuffle={video?.shuffle()}
                                        style={{
                                            "margin-right": "40px",
                                            "width": "calc(100% - 40px)"
                                        }} onVideoClick={(v) => {
                                            const index = video?.queue()?.findIndex(x => x === v);
                                            if (index !== undefined) {
                                                video?.actions?.setIndex(index);
                                            }
                                        }} onShuffleClick={() => {
                                            video?.actions?.setShuffle(!video?.shuffle());
                                        }} onRepeatClick={() => {
                                            video?.actions?.setRepeat(!video?.repeat());
                                        }} onIndexMoved={(index1, index2) => {
                                            if (video?.index() === index1)
                                                video?.actions.setIndex(index2);
                                            else if (video?.index() === index2)
                                                video?.actions.setIndex(index1);
                                        }} onVideoRemoved={(index) => {
                                            const v = video;
                                            const i = v?.index();
                                            const q = v?.queue();
                                            if (!v || i === undefined || !q) {
                                                return;
                                            }
    
                                            video?.actions.setQueue(i > index ? i - 1 : i, q.slice(0, index).concat(q.slice(index + 1)), video?.repeat(), video?.shuffle());
                                        }} />
                                </Show>
                                    
                                <Show when={videoLoadedIsValid$() && liveChatWindow$()}>
                                    <Show when={liveChatWindow$()?.error}>
                                        <div class={styles.liveChatError}>
                                            {liveChatWindow$()?.error}
                                        </div>
                                    </Show>
                                    <Show when={!liveChatWindow$()?.error && liveChatWindow$()?.url}>
                                        <LiveChatRemoteWindow descriptor={liveChatWindow$()} style={{
                                            'height': '640px',
                                            "margin-right": "40px",
                                            "width": "calc(100% - 40px)"
                                        }} />
                                    </Show>
                                </Show>

                                <Show when={videoLoadedIsValid$() && recommendationsVisible$()}>
                                    <div style={{
                                        "margin-right": "20px",
                                        "width": "calc(100% - 20px)"
                                    }}>
                                        <FlexibleArrayList style={{
                                            display: "flex",
                                            gap: "8px",
                                            "flex-direction": "column"
                                        }}
                                            outerContainerRef={scrollContainerRef}
                                            onEnd={onScrollEndRecommendations}
                                            addedItems={recomPager$()?.addedItemsEvent}
                                            modifiedItems={recomPager$()?.modifiedItemsEvent}
                                            removedItems={recomPager$()?.removedItemsEvent}
                                            items={recomPager$()?.data}
                                            builder={(_, item) => renderRecommendation(item)} />
                                    </div>
                                </Show>
                            </div>
                        </Show>
                    </div>
                </div>
            </ScrollContainer>
            <Show when={repliesVisible$()}>
                <div style={{
                    "position": "absolute",
                    "left": 0,
                    "bottom": 0,
                    "width": "100vw",
                    "height": "100vh",
                    "display": "flex",
                    "background-color": "rgba(15, 15, 15, 0.86)",
                    "justify-content": "center",
                    "align-items": "center",
                    "z-index": mode() === VideoMode.Theatre ? 2 : undefined
                }} onClick={() => hideReplies()}>
                    <Show when={repliesPager$()} fallback={<Loader></Loader>}>
                        <div style="border-radius: 12px; border: 1px solid #2E2E2E; background: #141414; box-shadow: 0px 63px 80px 0px rgba(0, 0, 0, 0.31), 0px 40.833px 46.852px 0px rgba(0, 0, 0, 0.24), 0px 24.267px 25.481px 0px rgba(0, 0, 0, 0.19), 0px 12.6px 13px 0px rgba(0, 0, 0, 0.16), 0px 5.133px 6.519px 0px rgba(0, 0, 0, 0.12), 0px 1.167px 3.148px 0px rgba(0, 0, 0, 0.07); display: flex; flex-direction: column; width: 80vw; max-width: 700px; max-height: 80vh; overflow: hidden;" onClick={(e) => {
                            e.stopPropagation();
                            e.preventDefault();
                        }}>
                            <div style="display: flex; flex-direction: row; width: calc(100% - 40px); margin-top: 20px; margin-left: 20px; margin-right: 20px; margin-bottom: 20px;">
                                <IconButton icon={icon_back} onClick={(e) => {
                                    hideReplies();
                                    e.stopPropagation();
                                    e.preventDefault();
                                }} />
                                <div style="flex-grow: 1"></div>
                                <div onClick={(e) => {
                                    hideReplies();
                                    e.stopPropagation();
                                    e.preventDefault();
                                }} style="width: 32px; height: 32px; border-radius: 50%; background: #2E2E2E; display: flex; align-items: center; justify-content: center; cursor: pointer;">
                                    <img src={icon_close} style="width: 19px; height: 19px;" />
                                </div>
                            </div>
                            <ScrollContainer ref={repliesOverlayScrollContainerRef} scrollToTopButton={true}>
                                <For each={repliesParents$()}>
                                    {(item, i) => {
                                        return (<CommentView editable={activeCommentSection$() === COMMENT_SECTION_POLYCENTRIC}
                                            comment={item}
                                            onClick={(ev) => handleContainerClick(ev)}
                                            style={{
                                                "background-color": "#2E2E2E",
                                                "border-radius": "8px",
                                                "overflow": "hidden",
                                                "padding-left": "12px",
                                                "padding-right": "12px",
                                                "padding-top": "12px",
                                                "padding-bottom": "12px",
                                                "margin-left": "20px",
                                                "margin-right": "20px",
                                                "width": "calc(100% - 40px)"
                                            }} />);
                                    }}
                                </For>
                                <FlexibleArrayList outerContainerRef={repliesOverlayScrollContainerRef}
                                    addedItems={repliesPager$()?.addedItemsEvent}
                                    modifiedItems={repliesPager$()?.modifiedItemsEvent}
                                    removedItems={repliesPager$()?.removedItemsEvent}
                                    onEnd={onRepliesScrollEnd}
                                    items={repliesPager$()!.data}
                                    builder={(_, item) =>
                                        <CommentView
                                            style={{
                                                "margin-left": "20px",
                                                "margin-right": "20px",
                                                "padding-top": "32px",
                                                "width": "calc(100% - 40px)"
                                            }}
                                            editable={activeCommentSection$() === COMMENT_SECTION_POLYCENTRIC}
                                            comment={item()?.object}
                                            onClick={(ev) => handleContainerClick(ev)}
                                            onRepliesClicked={() => { 
                                                const parent = item();
                                                if (!parent) {
                                                    return;
                                                }

                                                const parents = repliesParents$() ?? [];
                                                batch(async () => {
                                                    setRepliesParents([ ... parents, parent?.object ]);
                                                    setRepliesPager(await DetailsBackend.repliesPager(parent.refID ?? ""));
                                                });
                                        }} />
                                    }
                                    /*overscan={10}*/ />
                                <div style="height: 20px"></div>
                            </ScrollContainer>
                        </div>
                    </Show>
                </div>
            </Show>
            <Show when={video?.state() === VideoState.Maximized && video?.desiredMode() === VideoMode.Theatre}>
                <div style={{ "position": "absolute", "left": "0px", "top": "0px", "z-index": 2, "width": sideBarHidden$() ? "10px" : undefined, "cursor": sideBarAutoHidden$() && sideBarHidden$() ? "none" : undefined }} onMouseMove={handleSideBarMove} onMouseLeave={handleSideBarMouseLeave}>
                    <SideBar alwaysMinimized={true} onNavigate={() => minimize()} style={{ "transition": "transform 0.3s ease-in-out" }} classList={{ [styles.sideBarHidden]: sideBarHidden$() }} onMoreOpened={() => setSideBarHidden(true)}></SideBar>
                </div>
            </Show>
        </div>
    );
};

export default VideoDetailView;