import { Accessor, Component, ErrorBoundary, JSX, Show, batch, createEffect, createMemo, createSignal, on, onCleanup, onMount, untrack } from 'solid-js';
import styles from './index.module.css';
import PlayerControlsView from '../PlayerControlsView';
import { Duration } from 'luxon';
import { SourceSelected } from '../../contentDetails/VideoDetailView';
import { DetailsBackend } from '../../../backend/DetailsBackend';
import { CastConnectionState, useCasting } from '../../../contexts/Casting';
import { CastingBackend } from '../../../backend/CastingBackend';
import { Event0 } from "../../../utility/Event";
import * as dashjs from 'dashjs';
import Hls from 'hls.js';
import { ChapterType, IChapter } from '../../../backend/models/contentDetails/IChapter';
import { IPlatformVideoDetails } from '../../../backend/models/contentDetails/IPlatformVideoDetails';
import CircleLoader from '../../basics/loaders/CircleLoader';
import { formatDuration, uuidv4 } from '../../../utility';
import { LoaderGame, LoaderGameHandle } from '../../LoaderGame';
import StateWebsocket from '../../../state/StateWebsocket';
import Globals from '../../../globals';
import { clearLiveChatOnSeek } from '../../../state/StateLiveChat';
import { focusable } from '../../../focusable'; void focusable;
import { FocusableOptions, InputSource } from '../../../nav';

interface VideoProps {
    onVideoDimensionsChanged: (width: number, height: number) => void;
    children: JSX.Element;
    video?: IPlatformVideoDetails,
    source?: SourceSelected;
    sourceQuality?: number;
    onPlayerQualityChanged?: (level: number) => void;
    onSettingsDialog?: (event: HTMLElement|undefined) => void;
    onFullscreenChange?: (isFullscreen: boolean) => void;
    onToggleSubtitles?: () => void;
    onProgress?: (progress: number) => void;
    onEnded?: () => void;
    onError?: (message: string, fatal: boolean) => void;
    onPositionChanged?: (time: Duration) => void;
    onIncreasePlaybackSpeed?: () => void;
    onDecreasePlaybackSpeed?: () => void;
    onOpenSearch?: () => void;
    onIsPlayingChanged?: (isPlaying: boolean) => void;
    onPreviousVideo?: () => void;
    onNextVideo?: () => void;
    handleTheatre?: () => void;
    handleEscape?: () => void;
    handleMinimize?: () => void;
    onSetScrubbing?: (scrubbing: boolean) => void;
    lockOverlay: boolean;
    ref?: (el: HTMLDivElement) => void;
    style?: JSX.CSSProperties;
    eventRestart?: Event0;
    eventMoved?: Event0;
    buttons?: JSX.Element;
    chapters?: IChapter[];
    playbackSpeed?: number;
    volume?: number;
    onVolumeChanged?: (volume: number) => void;
    leftButtonContainerStyle?: JSX.CSSProperties;
    rightButtonContainerStyle?: JSX.CSSProperties;
    onVerifyToggle?: (arg: boolean)=>boolean;
    resumePosition?: Duration;
    loaderUI?: JSX.Element;
    fullscreen?: boolean;
    focusable?: boolean;
    onOptions?: (el: HTMLElement, inputSource: InputSource) => void;
    onReady?: (handle: VideoPlayerViewHandle) => void;
}

export type VideoPlayerViewHandle = {
    toggleMute: () => void;
    toggleFullscreen: () => void;
};

const VideoPlayerView: Component<VideoProps> = (props) => {
    const casting = useCasting()!;
    
    let videoCaptionsRef: HTMLDivElement | undefined;
    let videoElement: HTMLVideoElement | undefined;
    let containerRef: HTMLDivElement | undefined;
    let dashPlayer: dashjs.MediaPlayerClass | undefined;
    let hlsPlayer: Hls | undefined;
    let timeout: NodeJS.Timeout | undefined;
    let volumeBeforeMute: number | undefined = undefined;
    let subtitleMap: Map<string, HTMLParagraphElement> = new Map<string, HTMLParagraphElement>();
    const [areControlsVisible, setAreControlsVisible] = createSignal(false);
    const [duration, setDuration] = createSignal(Duration.fromMillis(0));
    const [videoDimensions, setVideoDimensions] = createSignal({ width: 1920, height: 1080 });
    const [thumbnailDimensions, setThumbnailDimensions] = createSignal({ width: 1920, height: 1080 });
    const [isPlaying, setIsPlaying] = createSignal(false);
    const [isScrubbing, setIsScrubbing] = createSignal(false);
    const [position, setPosition] = createSignal(Duration.fromMillis(0));
    let switchPosition = Duration.fromMillis(0);
    const [positionBuffered, setPositionBuffered] = createSignal(Duration.fromMillis(0));
    const [isFullscreen, setIsFullscreen] = createSignal(false);
    const [isCasting, setIsCasting] = createSignal(casting?.activeDevice.device() ? true : false);
    const [isAudioOnly, setIsAudioOnly] = createSignal(false);
    const [isLoading, setIsLoading] = createSignal(true);
    const [resumePositionVisible, setResumePositionVisible] = createSignal(false);
    const [endControlsVisible$, setEndControlsVisible] = createSignal(false);
    const [loaderGameVisible$, setLoaderGameVisible] = createSignal<number>();
    let frameRate: number | undefined = undefined; //TODO: Framerate is currently not accurate, not properly exposed by video,hlsjs,dashjs, would need to feed it in from sources
    let currentUrl: string | undefined;
    let castingEndedEmitted = false;
    let loader: LoaderGameHandle | undefined;
    let currentTag = uuidv4();

    createEffect(() => {
        if (isPlaying()) {
            setLoaderGameVisible(undefined);
        }
        props.onIsPlayingChanged?.(isPlaying());
    });

    createEffect(() => {
        props.onPositionChanged?.(position());
    });

    createEffect(() => {
        setCurrentVolume(props.volume ?? 1);
    });

    const currentChapter$ = createMemo(()=>{
        return props.chapters?.find(x=>x.timeStart < (position().milliseconds / 1000) && x.timeEnd > (position().milliseconds / 1000));
    });
    const previousChapter = () => {
        const chapters = props.chapters;
        const posSec = position().milliseconds / 1000;

        if (!chapters || chapters.length === 0) return undefined;
        const previous = chapters
            .filter(ch => ch.timeEnd <= posSec)
            .sort((a, b) => b.timeEnd - a.timeEnd)[0];

        return previous;
    };

    const nextChapter = () => {
        const chapters = props.chapters;
        const posSec = position().milliseconds / 1000;

        if (!chapters || chapters.length === 0) return undefined;
        const next = chapters
            .filter(ch => ch.timeStart >= posSec)
            .sort((a, b) => a.timeStart - b.timeStart)[0];

        return next;
    };

    let lastSkip: IChapter | undefined = undefined;
    let skippedOnce: IChapter[] = [];
    createEffect(()=>{
        console.log("Chapters changed: ", props.chapters);
        skippedOnce = [];
    });
    createEffect(()=>{
        const chapter = currentChapter$();
        console.log("Current Chapter changed: [" + chapter?.name + "]", chapter);
        if(chapter) {
            if(chapter.type == ChapterType.SKIP) {
                if(lastSkip != chapter) {
                    lastSkip = chapter;
                    setCurrentPosition(Duration.fromMillis(chapter.timeEnd * 1000));
                }
            }
            else if(chapter.type == ChapterType.SKIPONCE) {
                if(skippedOnce.indexOf(chapter) < 0) {
                    skippedOnce.push(chapter);
                    setCurrentPosition(Duration.fromMillis(chapter.timeEnd * 1000));
                }
            }
        }
    });
    function onSkip() {
        const chapter = currentChapter$();
        if(!chapter)
            return;
        setCurrentPosition(Duration.fromMillis(chapter.timeEnd * 1000));
    }

    let mouseDownOnVideo = false;

    let lastPositionMonitored = 0;
    const minPositionDelta = 1000;
    createEffect(() =>{
        const currentPosition = position().toMillis();
        if(Math.abs(currentPosition - lastPositionMonitored) > minPositionDelta) {
            lastPositionMonitored = currentPosition;
            
            if(props.onProgress)
                props.onProgress(currentPosition);
        }
    });

    createEffect(() => {
        console.log("video dimensions changed", videoDimensions());
    });

    createEffect(() => {
        console.log("thumbnail dimensions changed", thumbnailDimensions());
    });

    const isContainerFullscreen = () => (document.fullscreenElement === containerRef);
    const syncFullscreenToDom = async (shouldBeFs: boolean) => {
        console.info("syncFullscreenToDom", shouldBeFs);
        if (!containerRef) return;
        const currentlyFs = isContainerFullscreen();
        if (shouldBeFs === currentlyFs) return;

        try {
            if (shouldBeFs) {
                const req = containerRef.requestFullscreen;
                if (req) {
                    await req.call(containerRef);
                } else {
                    console.warn("Fullscreen API not available on container.");
                }
            } else {
                const exit = document.exitFullscreen;
                if (document.fullscreenElement) {
                    await exit.call(document);
                }
            }
        } catch (err) {
            console.warn("Failed to change fullscreen:", err);
        }
    };

    createEffect(() => {
        syncFullscreenToDom(!!props.fullscreen);
    });

    
    const getResumePosition = (shouldResume?: boolean, startTime?: Duration) => {
        if (shouldResume) {
            console.log("getResumePosition", {shouldResume, startTime: startTime?.toMillis(), switchPosition: switchPosition?.toMillis(), result: switchPosition.toMillis()});
            return switchPosition;
        } else if (startTime) {
            console.log("getResumePosition", {shouldResume, startTime: startTime?.toMillis(), switchPosition: switchPosition?.toMillis(), result: startTime.toMillis()});
            return startTime;
        }

        console.log("getResumePosition", {shouldResume, startTime: startTime, switchPosition: switchPosition?.toMillis(), result: 0});
        return Duration.fromMillis(0);
    };

    const startCastingIfApplicable = async (castConnectionState?: CastConnectionState, shouldResume?: boolean, startTime?: Duration, tag?: string) => {
        if (castConnectionState === CastConnectionState.Connected) {
            if (!props.source)
                return;

            console.log("start casting", switchPosition);
            try {
                await CastingBackend.mediaLoad({
                    streamType: props.source.isLive ? "LIVE" : "BUFFERED",
                    resumePosition: getResumePosition(shouldResume, startTime),
                    duration: untrack(duration),
                    sourceSelected: props.source,
                    speed: 1.0,
                    tag,
                    title: props.video?.name
                });
            } catch (e) {
                console.info("failed to start casting", e);
            }
        }
    };

    const stopCastingIfApplicable = async () => {
        await CastingBackend.mediaStop();
    };

    const changeSourceToSetSource = async (source: SourceSelected | undefined) => {
        currentTag = uuidv4();
        setLoaderGameVisible(undefined);

        console.info("source", source);
        if (!source || (source.video == -1 && source.audio == -1)) {
            if (untrack(isCasting)) {
                await CastingBackend.mediaStop();
            } else {
                try {
                    changeSource();
                } catch (e) {
                    console.error("Failed to unload source", e);
                }
            }
            console.warn("source null or video and audio unset", source);
            return;
        }

        const descriptor = await DetailsBackend.sourceProxy(source.url, source.video, source.videoIsLocal, source.audio, source.audioIsLocal, source.subtitle, source.subtitleIsLocal, currentTag);
        console.log("Direct url", descriptor.url, descriptor.type);

        if (untrack(isCasting)) {
            console.info("start casting because changeSourceToSetSource call and casting");
            await startCastingIfApplicable(untrack(casting.activeDevice.state), source.shouldResume, source.time, currentTag);
        } else {
            console.info("change source because changeSourceToSetSource call");

            try {
                changeSource(descriptor.url, descriptor.type, source.shouldResume, source.time);
            }
            catch(ex) {
                console.error("Failed to load source", ex);
            }
        }
    };

    createEffect(on(isCasting, async (isCurrentlyCasting) => {
        if (casting && isCurrentlyCasting) {
            console.info("start casting because isCasting change");
            await startCastingIfApplicable(untrack(casting.activeDevice.state), true, props.source?.time, currentTag);
            changeSource(undefined);
            stopHideControls();
        } else {
            console.info("stop casting because isCasting change");
            //TODO: playWhenReady = casting?.activeDevice.device()?.isPlaying() ?? false
            await changeSourceToSetSource(props.source ? { ... props.source, shouldResume: true } : undefined);
            await stopCastingIfApplicable();
            startHideControls();
        }

        volumeBeforeMute = undefined;
    }));

    const [currentVolume$, setCurrentVolume] = createSignal<number>(1);

    createEffect(() => {
        if (isCasting()) {
            setCurrentVolume(casting?.activeDevice.volume());
        }
    });

    createEffect(async () => {
        if (isCasting()) {
            const dim = thumbnailDimensions();
            props.onVideoDimensionsChanged(dim.width, dim.height);
        } else {
            const dim = videoDimensions();
            props.onVideoDimensionsChanged(dim.width, dim.height);
        }
    });

    createEffect(async () => {
        const castConnectionState = casting?.activeDevice.state();
        if (isCasting() && castConnectionState === CastConnectionState.Connected) {
            casting?.actions.close();
            console.info("start casting because casting?.activeDevice.state or isCasting change");
            await startCastingIfApplicable(castConnectionState, true, props.source?.time, currentTag);
        }
    });

    createEffect(() => {
        if (isScrubbing())
            stopHideControls();
        else if (!untrack(isCasting))
            startHideControls();
    })

    createEffect(on(casting.activeDevice.device, (device) => {
        const isCurrentlyCasting = device ? true : false;
        if (isCurrentlyCasting != isCasting()) {
            switchPosition = untrack(position);
            console.log("set switch position to", {switchPosition: switchPosition.toMillis(), isCasting});
            setIsCasting(isCurrentlyCasting);
        }
    }));

    const stopHideControls = () => {
        if (timeout != undefined) {
            clearTimeout(timeout);
        }
    };

    const startHideControls = () => {
        if (isCasting()) {
            return;
        }
        
        timeout = setTimeout(() => setAreControlsVisible(false), 3000);
    };

    const hideControls = () => {
        setAreControlsVisible(false);
        stopHideControls();
    };

    const showControls = () => {
        stopHideControls();
        setAreControlsVisible(true);
        startHideControls();
    };

    createEffect(() => {
        if (!casting) {
            return;
        }

        const time = casting.activeDevice.time();
        if (!isCasting() || untrack(isScrubbing)) {
            return;
        }

        setPosition(time);
        setPositionBuffered(time);

        const dur = duration();
        const timeLeft = duration().minus(time);
        console.log("Received position", {time_s: time.as('seconds'), timeLeft_s: timeLeft.as('seconds')});
        if (dur.as('seconds') > 1 && timeLeft.as('seconds') < 1) {
            if (!castingEndedEmitted) {
                props.onEnded?.();
                castingEndedEmitted = true;
                console.info("casting video ended");
            }
        } else {
            castingEndedEmitted = false;
        }
    });

    createEffect(on(position, () => {
        setEndControlsVisible(false);
    }));

    createEffect(() => {
        if (!casting || !isCasting()) {
            return;
        }

        const duration = casting.activeDevice.duration();
        setDuration(duration);
    });

    createEffect(() => {
        console.info("isScrubbing", isScrubbing());
    })

    createEffect(() => {
        if (!casting || !isCasting()) {
            return;
        }

        const isPlaying = casting.activeDevice.isPlaying();
        setIsPlaying(isPlaying);
    });

    const handleFullscreenChange = () => {
        const elem = document.fullscreenElement;
        const fsNow = (elem === containerRef);
        const wasFs = isFullscreen();
        if (fsNow || (wasFs && !elem)) {
            if (fsNow !== wasFs) setIsFullscreen(fsNow);
            props.onFullscreenChange?.(fsNow);
        }
    };

    const pause = () => {
        if (dashPlayer) {
            dashPlayer.pause();
        } else {
            videoElement?.pause();
        }
    };

    const setVolume = async (value: number) => {
        if (isCasting()) {
            await CastingBackend.changeVolume(value);
        } else {
            if (dashPlayer) {
                dashPlayer?.setVolume(value);
            } else if (videoElement) {
                videoElement.volume = value;
            }
        }
    };

    const paused = () => {
        if (dashPlayer) {
            return dashPlayer.isPaused();
        } else {
            return videoElement?.paused ?? true;
        }
    };

    const volume = () => {
        if (dashPlayer) {
            return dashPlayer.getVolume();
        } else {
            return videoElement?.volume ?? 0;
        }
    };

    const ended = () => {
        if (dashPlayer) {
            const duration = dashPlayer.duration();
            const currentTime = dashPlayer.time();
            return currentTime >= duration;
        } else {
            return videoElement?.ended ?? true;
        }
    };

    const play = () => {
        if (dashPlayer) {
            dashPlayer.play();
        } else {
            videoElement?.play();
        }
    };

    const setPlaybackSpeed = async (playbackSpeed: number) => {
        if (isCasting()) {
            await CastingBackend.changeSpeed(playbackSpeed);
        } else {
            if (dashPlayer) {
                dashPlayer.setPlaybackRate(playbackSpeed);
            } else if (videoElement) {
                videoElement.playbackRate = playbackSpeed;
            }
        }
    };

    const setCurrentPosition = (time: Duration) => {
        const seconds = time.as('seconds');
        if (dashPlayer) {
            dashPlayer.seek(seconds);
        } else if (videoElement) {
            videoElement.currentTime = seconds;
        }
    };

    const onReady = (shouldResume?: boolean, startTime?: Duration, shouldSetCurrentPosition: boolean = true) => {
        console.log("onPlayerReady", shouldResume, startTime);
        if (isCasting()) {
            return;
        }

        if (shouldSetCurrentPosition) {
            setCurrentPosition(getResumePosition(shouldResume, startTime));
        }
        play();
        setPlaybackSpeed(props.playbackSpeed ?? 1.0);
    };

    createEffect(() => {
        setPlaybackSpeed(props.playbackSpeed ?? 1.0);
    });

    const onVolumeChanged = (volume: number) => {
        if (isCasting()) {
            return;
        }

        setCurrentVolume(volume);
        props.onVolumeChanged?.(volume);
    };

    const onError = (error: string, fatal: boolean) => {
        props.onError?.(error, fatal);
        if (fatal) {
            setLoaderGameVisible(undefined);
            setIsPlaying(false);
        }
    };

    createEffect(() => {
        const resumePosition = props.resumePosition;
        const pos = position();
        const dur = duration();
        if (!resumePosition) {
            setResumePositionVisible(false);
            return;
        }

        const pos_ms = pos.as('milliseconds');
        const res_ms = resumePosition.as('milliseconds');
        const dur_ms = dur.as('milliseconds');
        const visible = res_ms > 60000 && dur_ms - res_ms > 5000 && res_ms - pos_ms > 5000 && pos_ms < 8000;
        setResumePositionVisible(visible);
    });

    const changeSource = (sourceUrl?: string, mediaType?: string, shouldResume?: boolean, startTime?: Duration) => {
        //TODO: Implement playWhenReady ?
        console.info("changeSource", {sourceUrl, mediaType, shouldResume, startTime});
        setIsAudioOnly(false);
        setIsPlaying(false);
        frameRate = undefined;
        
        if (currentUrl === sourceUrl) {
            if (startTime) {
                const startTime_ms = startTime.as('milliseconds');
                const currentTime_ms = position().as('milliseconds');
                if (Math.abs(startTime_ms - currentTime_ms) < 5000) {
                    console.warn("Skipped changing video URL because URL and time is (nearly) unchanged", {sourceUrl, currentUrl, shouldResume, startTime: startTime ? formatDuration(startTime) : undefined, switchPosition: switchPosition ? formatDuration(switchPosition) : undefined});
                } else {
                    console.info("Skipped changing video URL because URL is the same, but time was changed, seeking instead", {sourceUrl, currentUrl, shouldResume, startTime: startTime ? formatDuration(startTime) : undefined, switchPosition: switchPosition ? formatDuration(switchPosition) : undefined});
                    setCurrentPosition(startTime);
                }
            }
            return;
        }

        if (!untrack(isCasting))
            switchPosition = untrack(position);

        currentUrl = sourceUrl;
        console.log("changeSource", {currentUrl, sourceUrl, mediaType, shouldResume, startTime, switchPosition});

        for (const subtitle of subtitleMap.values()) {
            subtitle.remove();
        }

        subtitleMap.clear();          

        const currentVolume = currentVolume$();
        if (dashPlayer) {
            try {
                dashPlayer.destroy();
            } catch (e) {
                console.warn("Failed to destroy dash player", e);
            }
            dashPlayer = undefined;
        }

        if (hlsPlayer) {
            hlsPlayer.destroy();
            hlsPlayer = undefined;
        }

        if (videoElement) {
            videoElement.src = "";
            videoElement.onerror = null;
            videoElement.onloadedmetadata = null;
            videoElement.ontimeupdate = null;
            videoElement.onplay = null;
            videoElement.onpause = null;
            videoElement.onended = null;
            videoElement.onvolumechange = null;

            batch(() => {
                setPosition(Duration.fromMillis(0));
                setDuration(Duration.fromMillis(0));
            });
        }

        setEndControlsVisible(false);

        if (sourceUrl && mediaType && videoElement) {
            setIsLoading(false);

            if (mediaType === 'application/dash+xml' && !videoElement.canPlayType(mediaType)) {
                dashPlayer = dashjs.MediaPlayer().create();
                dashPlayer.updateSettings({
                    streaming: {
                        text: {
                            dispatchForManualRendering: true
                        },
                        manifestRequestTimeout: 60000
                    }
                });

                dashPlayer.on(dashjs.MediaPlayer.events.PLAYBACK_PLAYING, () => {
                    if (isCasting()) {
                        return;
                    }
            
                    setIsPlaying(true);
                });
            
                dashPlayer.on(dashjs.MediaPlayer.events.PLAYBACK_PAUSED, () => {
                    if (isCasting()) {
                        return;
                    }
            
                    setIsPlaying(false);
                });
            
                dashPlayer.on(dashjs.MediaPlayer.events.PLAYBACK_ENDED, () => {
                    if (isCasting()) {
                        return;
                    }
            
                    setIsPlaying(false);
                    props.onEnded?.();
                    setEndControlsVisible(true);
                });
            
                dashPlayer.on(dashjs.MediaPlayer.events.PLAYBACK_TIME_UPDATED, () => {
                    if (isCasting() || !videoElement) {
                        return;
                    }
            
                    const currentTimeMillis = (videoElement?.currentTime ?? 0) * 1000;
                    setPosition(Duration.fromMillis(currentTimeMillis));
                    let dashBufferLength = dashPlayer?.getBufferLength("video") 
                        ?? dashPlayer?.getBufferLength("audio") 
                        ?? dashPlayer?.getBufferLength("text") 
                        ?? dashPlayer?.getBufferLength("image") 
                        ?? 0;
                    if (Number.isNaN(dashBufferLength))
                        dashBufferLength = 0;
                    
                    setPositionBuffered(Duration.fromMillis(currentTimeMillis + (dashBufferLength * 1000)));
                    
                    //TODO: For buffered position, might need to calculate based on dashPlayer.getBufferLength()
                });

                const updateFps = () => {
                    const rep = dashPlayer?.getCurrentRepresentationForType?.('video');
                    if (!rep) {
                        frameRate = undefined;
                        return;
                    }

                    if (frameRate != rep.frameRate) {
                        frameRate = rep.frameRate;
                    }
                };
                
                dashPlayer.on(dashjs.MediaPlayer.events.QUALITY_CHANGE_RENDERED, e => { if (e.mediaType === 'video') updateFps(); });
                dashPlayer.on(dashjs.MediaPlayer.events.TRACK_CHANGE_RENDERED, updateFps);
                dashPlayer.on(dashjs.MediaPlayer.events.PERIOD_SWITCH_COMPLETED, updateFps);
                dashPlayer.on(dashjs.MediaPlayer.events.REPRESENTATION_SWITCH, e => updateFps());
                dashPlayer.on(dashjs.MediaPlayer.events.STREAM_INITIALIZED, () => {
                    updateFps();
                    const videoWidth = videoElement?.videoWidth ?? 0;
                    const videoHeight = videoElement?.videoHeight ?? 0;
                    setIsAudioOnly(videoWidth === 0 && videoHeight === 0);
                    if (videoWidth === 0 || videoHeight === 0)
                        setVideoDimensions({ width: 1920, height: 1080 });
                    else
                        setVideoDimensions({ width: videoWidth, height: videoHeight });
            
                    setDuration(Duration.fromMillis((videoElement?.duration ?? 0) * 1000));
                    onReady(shouldResume, startTime, false);
                });

                dashPlayer.on(dashjs.MediaPlayer.events.CUE_ENTER, (e: any) => {
                    const subtitle = document.createElement("div")
                    subtitle.textContent = e.text;
                    subtitleMap.set(e.cueID, subtitle);
                    videoCaptionsRef?.appendChild(subtitle);
                });
    
                dashPlayer.on(dashjs.MediaPlayer.events.CUE_EXIT, (e: any) => {
                    const subtitle = subtitleMap.get(e.cueID);
                    if (subtitle) {
                        subtitleMap.delete(e.cueID);
                        subtitle.remove();
                    }
                });

                dashPlayer.on(dashjs.MediaPlayer.events.PLAYBACK_VOLUME_CHANGED, () => {
                    onVolumeChanged(dashPlayer?.getVolume() ?? 1);
                });

                const fatalErrorCodes = [
                    // Manifest/MPD errors – playback won’t start if these occur:
                    dashjs.MediaPlayer.errors.MANIFEST_LOADER_PARSING_FAILURE_ERROR_CODE,
                    dashjs.MediaPlayer.errors.MANIFEST_LOADER_LOADING_FAILURE_ERROR_CODE,
                    dashjs.MediaPlayer.errors.MANIFEST_ERROR_ID_PARSE_CODE,
                    dashjs.MediaPlayer.errors.MANIFEST_ERROR_ID_NOSTREAMS_CODE,
                    dashjs.MediaPlayer.errors.MANIFEST_ERROR_ID_MULTIPLEXED_CODE,

                    // Download/Initialization errors – fatal if the manifest or initialization segment cannot be loaded:
                    dashjs.MediaPlayer.errors.DOWNLOAD_ERROR_ID_MANIFEST_CODE,
                    dashjs.MediaPlayer.errors.DOWNLOAD_ERROR_ID_INITIALIZATION_CODE,
                    dashjs.MediaPlayer.errors.DOWNLOAD_ERROR_ID_CONTENT_CODE,

                    // MediaSource errors – indicate that the browser can’t play the stream:
                    dashjs.MediaPlayer.errors.CAPABILITY_MEDIASOURCE_ERROR_CODE,
                    dashjs.MediaPlayer.errors.MEDIASOURCE_TYPE_UNSUPPORTED_CODE,

                    // Additional critical errors (that may block recovery):
                    dashjs.MediaPlayer.errors.TIME_SYNC_FAILED_ERROR_CODE,
                    dashjs.MediaPlayer.errors.FRAGMENT_LOADER_NULL_REQUEST_ERROR_CODE,
                    dashjs.MediaPlayer.errors.URL_RESOLUTION_FAILED_GENERIC_ERROR_CODE,
                    dashjs.MediaPlayer.errors.APPEND_ERROR_CODE,
                    dashjs.MediaPlayer.errors.REMOVE_ERROR_CODE,
                    dashjs.MediaPlayer.errors.DATA_UPDATE_FAILED_ERROR_CODE,
                    dashjs.MediaPlayer.errors.DOWNLOAD_ERROR_ID_SIDX_CODE,
                    dashjs.MediaPlayer.errors.DOWNLOAD_ERROR_ID_XLINK_CODE,

                    // DRM/Protection errors – if the content is encrypted and these errors occur, playback cannot proceed:
                    dashjs.MediaPlayer.errors.MEDIA_KEYERR_CODE,
                    dashjs.MediaPlayer.errors.MEDIA_KEYERR_UNKNOWN_CODE,
                    dashjs.MediaPlayer.errors.MEDIA_KEYERR_CLIENT_CODE,
                    dashjs.MediaPlayer.errors.MEDIA_KEYERR_SERVICE_CODE,
                    dashjs.MediaPlayer.errors.MEDIA_KEYERR_OUTPUT_CODE,
                    dashjs.MediaPlayer.errors.MEDIA_KEYERR_HARDWARECHANGE_CODE,
                    dashjs.MediaPlayer.errors.MEDIA_KEYERR_DOMAIN_CODE,
                    dashjs.MediaPlayer.errors.MEDIA_KEY_MESSAGE_ERROR_CODE,
                    dashjs.MediaPlayer.errors.MEDIA_KEY_MESSAGE_NO_CHALLENGE_ERROR_CODE,
                    dashjs.MediaPlayer.errors.SERVER_CERTIFICATE_UPDATED_ERROR_CODE,
                    dashjs.MediaPlayer.errors.KEY_STATUS_CHANGED_EXPIRED_ERROR_CODE,
                    dashjs.MediaPlayer.errors.MEDIA_KEY_MESSAGE_NO_LICENSE_SERVER_URL_ERROR_CODE,
                    dashjs.MediaPlayer.errors.KEY_SYSTEM_ACCESS_DENIED_ERROR_CODE,
                    dashjs.MediaPlayer.errors.KEY_SESSION_CREATED_ERROR_CODE,
                    dashjs.MediaPlayer.errors.MEDIA_KEY_MESSAGE_LICENSER_ERROR_CODE,

                    // MSS errors – if using Microsoft Smooth Streaming content:
                    dashjs.MediaPlayer.errors.MSS_NO_TFRF_CODE,
                    dashjs.MediaPlayer.errors.MSS_UNSUPPORTED_CODEC_CODE,

                    // Offline errors – if your app uses offline playback (optional):
                    dashjs.MediaPlayer.errors.OFFLINE_ERROR,
                    dashjs.MediaPlayer.errors.INDEXEDDB_QUOTA_EXCEED_ERROR,
                    dashjs.MediaPlayer.errors.INDEXEDDB_INVALID_STATE_ERROR,
                    dashjs.MediaPlayer.errors.INDEXEDDB_NOT_READABLE_ERROR,
                    dashjs.MediaPlayer.errors.INDEXEDDB_NOT_FOUND_ERROR,
                    dashjs.MediaPlayer.errors.INDEXEDDB_NETWORK_ERROR,
                    dashjs.MediaPlayer.errors.INDEXEDDB_DATA_ERROR,
                    dashjs.MediaPlayer.errors.INDEXEDDB_TRANSACTION_INACTIVE_ERROR,
                    dashjs.MediaPlayer.errors.INDEXEDDB_NOT_ALLOWED_ERROR,
                    dashjs.MediaPlayer.errors.INDEXEDDB_NOT_SUPPORTED_ERROR,
                    dashjs.MediaPlayer.errors.INDEXEDDB_VERSION_ERROR,
                    dashjs.MediaPlayer.errors.INDEXEDDB_TIMEOUT_ERROR,
                    dashjs.MediaPlayer.errors.INDEXEDDB_ABORT_ERROR,
                    dashjs.MediaPlayer.errors.INDEXEDDB_UNKNOWN_ERROR
                ];

                dashPlayer.on(dashjs.MediaPlayer.events.ERROR, (data) => {
                    console.error("DashJS ERROR", data);
                    const code = (data.error as any)?.code;
                    onError(`DashJS Error: ${JSON.stringify(data.error)}`, code ? fatalErrorCodes.includes(code) : false);
                });

                dashPlayer.on(dashjs.MediaPlayer.events.PLAYBACK_ERROR, (data) => {
                    console.error("DashJS PLAYBACK_ERROR", data);
                    const code = (data.error as any)?.code;
                    onError(`DashJS Playback Error: ${JSON.stringify(data.error)}`, code ? fatalErrorCodes.includes(code) : false);
                });

                dashPlayer.initialize(videoElement, sourceUrl, true, getResumePosition(shouldResume, startTime)?.as('seconds') ?? 0);
            } else if ((mediaType === 'application/vnd.apple.mpegurl' || mediaType === 'application/x-mpegURL') && !videoElement.canPlayType(mediaType)) {
                videoElement.onerror = (event: Event | string, source?: string, lineno?: number, colno?: number, error?: Error) => {
                    console.error("Player error", {source, lineno, colno, error});
                    onError(`Video Error: ${JSON.stringify({ source, lineno, colno, error})}`, true);
                };

                videoElement.onloadedmetadata = () => {                   
                    const videoWidth = videoElement?.videoWidth ?? 0;
                    const videoHeight = videoElement?.videoHeight ?? 0;
                    setIsAudioOnly(videoWidth === 0 && videoHeight === 0);
                    if (videoWidth === 0 || videoHeight === 0)
                        setVideoDimensions({ width: 1920, height: 1080 });
                    else
                        setVideoDimensions({ width: videoWidth, height: videoHeight });

                    setDuration(Duration.fromMillis((videoElement?.duration ?? 0) * 1000));
                    onReady(shouldResume, startTime);
                };

                videoElement.ontimeupdate = () => {
                    if (isCasting()) {
                        return;
                    }
        
                    const currentTime = videoElement?.currentTime ?? 0;
                    setPosition(Duration.fromMillis(currentTime * 1000));

                    if (videoElement && videoElement.buffered) {
                        const buffered = videoElement.buffered;
                        for (let i = 0; i < buffered.length; i++) {
                            const start = buffered.start(i);
                            const end = buffered.end(i);
                    
                            if (currentTime >= start && currentTime <= end) {
                                setPositionBuffered(Duration.fromMillis(end * 1000));
                                break;
                            }
                        }
                    }
                };

                videoElement.onplay = () => {
                    if (isCasting()) {
                        return;
                    }
        
                    setIsPlaying(true);
                };

                videoElement.onpause = () => {
                    if (isCasting()) {
                        return;
                    }
        
                    setIsPlaying(false);
                };

                videoElement.onended = () => {
                    if (isCasting()) {
                        return;
                    }
        
                    setIsPlaying(false);
                    props.onEnded?.();
                    setEndControlsVisible(true);
                };

                videoElement.onvolumechange = () => {
                    if (isCasting()) {
                        return;
                    }

                    onVolumeChanged(videoElement?.volume ?? 1);
                };

                videoElement
                
                hlsPlayer = new Hls();

                //TODO: Framerate
                /*hlsPlayer.on(Hls.Events.MANIFEST_PARSED, (eventName, data) => {
                    if (data?.levels?.length) {
                        const level = data.levels[data.firstLevel ?? 0];
                        if (level?.details?.framerate || level?.frameRate) {
                            setFrameRate(level.details?.framerate ?? level.frameRate);
                            console.info("Detected HLS framerate:", frameRate());
                        }
                    }
                });*/

                hlsPlayer.on(Hls.Events.LEVEL_SWITCHING, function (eventName, data) {
                    console.log("Player changed level to: " + data.level);
                    if(props.onPlayerQualityChanged)
                        props.onPlayerQualityChanged(data.level);
                });
                hlsPlayer.on(Hls.Events.ERROR, function(eventName, data) {
                    console.error("HLS player error", data);
                    onError(`HLS Error: ${JSON.stringify({ details: data.details, error: data.error })}`, data.fatal);
                });
                hlsPlayer.loadSource(sourceUrl);
                hlsPlayer.attachMedia(videoElement);
            } else {
                videoElement.onerror = (event: Event | string, source?: string, lineno?: number, colno?: number, error?: Error) => {
                    console.error("Player error", {source, lineno, colno, error});
                    onError(`Player error: ${JSON.stringify({source, lineno, colno, error})}`, true);
                };

                videoElement.onloadedmetadata = () => {                   
                    const videoWidth = videoElement?.videoWidth ?? 0;
                    const videoHeight = videoElement?.videoHeight ?? 0;
                    setIsAudioOnly(videoWidth === 0 && videoHeight === 0);
                    if (videoWidth === 0 || videoHeight === 0)
                        setVideoDimensions({ width: 1920, height: 1080 });
                    else
                        setVideoDimensions({ width: videoWidth, height: videoHeight });

                    setDuration(Duration.fromMillis((videoElement?.duration ?? 0) * 1000));
                    onReady(shouldResume, startTime);
                };

                videoElement.ontimeupdate = () => {
                    if (isCasting()) {
                        return;
                    }
        
                    const currentTime = videoElement?.currentTime ?? 0;
                    setPosition(Duration.fromMillis(currentTime * 1000));

                    if (videoElement && videoElement.buffered) {
                        const buffered = videoElement.buffered;
                        for (let i = 0; i < buffered.length; i++) {
                            const start = buffered.start(i);
                            const end = buffered.end(i);
                    
                            if (currentTime >= start && currentTime <= end) {
                                setPositionBuffered(Duration.fromMillis(end * 1000));
                                break;
                            }
                        }
                    }
                };

                videoElement.onplay = () => {
                    if (isCasting()) {
                        return;
                    }
        
                    setIsPlaying(true);
                };

                videoElement.onpause = () => {
                    if (isCasting()) {
                        return;
                    }
        
                    setIsPlaying(false);
                };

                videoElement.onended = () => {
                    if (isCasting()) {
                        return;
                    }
        
                    setIsPlaying(false);
                    props.onEnded?.();
                    setEndControlsVisible(true);
                };

                videoElement.onvolumechange = () => {
                    if (isCasting()) {
                        return;
                    }

                    onVolumeChanged(videoElement?.volume ?? 1);
                };

                videoElement.src = sourceUrl;
                videoElement.load();
            }
        } else {
            setIsLoading(true);
        }

        setVolume(currentVolume);
    };

    createEffect(async () => {
        await changeSourceToSetSource(props.source);
    });
    createEffect(()=>{
        const newLevel = props.sourceQuality;
        console.log("Source Quality changed: " + newLevel);
        if(hlsPlayer) {
            hlsPlayer!.currentLevel = newLevel && newLevel >= 0 && newLevel < hlsPlayer!.levels.length ? (hlsPlayer!.levels.length - newLevel) : -1;
        }
    });

    const toggleFullscreen = () => {
        syncFullscreenToDom(!isFullscreen());
    };

    const toggleVolume = async () => {
        if (isCasting()) {
            if (casting?.activeDevice.volume() > 0) {
                volumeBeforeMute = casting?.activeDevice.volume();
                await CastingBackend.changeVolume(0);
            } else if (volumeBeforeMute !== undefined) {
                await CastingBackend.changeVolume(volumeBeforeMute);
            } else {
                await CastingBackend.changeVolume(1);
            }
        } else {
            if (volume() > 0) {
                volumeBeforeMute = volume();
                setVolume(0);
            } else if (volumeBeforeMute !== undefined) {
                setVolume(volumeBeforeMute);
            } else {
                setVolume(1);
            }
        }
    };

    onMount(() => {
        document.addEventListener('fullscreenchange', handleFullscreenChange);
        setIsFullscreen(isContainerFullscreen()); 
        showControls();

        props.eventRestart?.register(async () => {
            const start = Duration.fromMillis(0);
            setPosition(start);
            if (isCasting()) {
                await CastingBackend.mediaSeek(start);
            } else {
                setCurrentPosition(start);
            }

            if (isCasting()) {
                await CastingBackend.mediaResume();
            } else {
                play();
            }
        }, this);

        console.info("Registered VideoLoader handler");
        StateWebsocket.registerHandlerNew("VideoLoader", (packet) => {
            console.info("VideoLoader triggered", packet);
            if (currentTag === packet.payload.tag && Globals.WindowID === packet.payload.windowId) {
                setLoaderGameVisible(packet.payload.duration);
                console.info("VideoLoader triggered accepted", packet);
            }
        }, "videoPlayerView");
        StateWebsocket.registerHandlerNew("VideoLoaderFinish", (packet) => {
            console.info("VideoLoaderFinish triggered", packet);
            if (currentTag === packet.payload.tag && Globals.WindowID === packet.payload.windowId) {
                setLoaderGameVisible(undefined);
            }
        }, "videoPlayerView");

        createEffect(() => console.info("loaderGameVisible$ changed", loaderGameVisible$()));

        props.onReady?.({
            toggleFullscreen: toggleFullscreen,
            toggleMute: toggleVolume
        });
    });

    onCleanup(async () => {
        changeSource(undefined, undefined, undefined);
        document.removeEventListener('fullscreenchange', handleFullscreenChange);
        stopHideControls();

        if (isCasting()) {
            await CastingBackend.mediaStop();
        }

        props.eventRestart?.unregister(this);

        console.info("Unregistered VideoLoader handler");
        StateWebsocket.unregisterHandler("VideoLoader", "videoPlayerView");
        StateWebsocket.unregisterHandler("VideoLoaderFinish", "videoPlayerView");
    });

    const togglePlay = async () => {
        if(props.onVerifyToggle && !props.onVerifyToggle(paused() || ended()))
            return;
        if (isCasting()) {
            if (casting?.activeDevice.isPlaying()) {
                await CastingBackend.mediaPause();
            } else {
                await CastingBackend.mediaResume();
            }
        } else {
            if (paused() || ended()) {
                play();
            } else {
                pause();
            }
        }
    };

    const handleMouseMove = (e: MouseEvent) => {
        showControls();
    };

    const handleMouseDown = (e: MouseEvent) => {
        showControls();
        mouseDownOnVideo = true;
    };

    const handleMouseUp = (e: MouseEvent) => {
        if (!mouseDownOnVideo) {
            return;
        }

        mouseDownOnVideo = false;
        showControls();
        //TODO: If fast forwarding, stop fast forwarding, else 
        togglePlay();
    };

    const handleDblClick = (e: MouseEvent) => {
        toggleFullscreen();
    };

    const handleEscape = async () => {
        if (isFullscreen()) {
            await syncFullscreenToDom(false);
        } else {
            props.handleEscape?.();
        }
    };

    const handleMinimize = async () => {
        if (isFullscreen()) {
            await syncFullscreenToDom(false);
        }
        props.handleMinimize?.();
    };

    //TODO: on mouse holding mouse down, fast forward the video until mouse goes up (starting after 1 second)
    //TODO: Skip a single frame using the , and . buttons

    function setContainerRef(el: HTMLDivElement) {
        containerRef = el;
        if(props.ref)
            props.ref(el);
    }

    const controlsVisible$ = createMemo(() => {
        if (loaderGameVisible$() !== undefined) {
            return false;
        } if (isCasting()) {
            return true;
        } else {
            return !isLoading() && (areControlsVisible() || props.lockOverlay || endControlsVisible$());
        }        
    });

    const performSetPosition = async (duration: Duration) => { 
        clearLiveChatOnSeek();
        setPosition(duration);
        if (isCasting()) {
            await CastingBackend.mediaSeek(duration);
        } else {
            setCurrentPosition(duration);
        }
    };

    const focusableOpts: Accessor<FocusableOptions | undefined> = createMemo(() => {
        if (!props.focusable)
            return undefined;

        return {
            onPress: togglePlay,
            onPressLabel: "Toggle Playback",
            onOptions: props.onOptions,
            onDirection: (el, dir, inputSource) => {
                switch (dir) {
                    case 'left':
                        performSetPosition(Duration.fromMillis(Math.max(Math.min(duration().toMillis(), position().toMillis() - 5000), 0)));
                        return true;
                    case 'right':
                        performSetPosition(Duration.fromMillis(Math.max(Math.min(duration().toMillis(), position().toMillis() + 5000), 0)));
                        return true;
                    case 'down':
                        console.info("isFullscreen", isFullscreen());
                        if (isFullscreen()) {
                            syncFullscreenToDom(false);
                            return true;
                        }
                        break;
                    case 'up':
                        if (!isFullscreen()) {
                            syncFullscreenToDom(true);
                            return true;
                        }
                        break;
                }

                return false;
            },
            onDirectionLabel: "(WS: Toggle Fullscreen  AD: Seek)"
        };
    });

    return (
        <div ref={setContainerRef} 
            classList={{
                [styles.container]: !isFullscreen(),
                [styles.containerFullscreen]: isFullscreen()
            }} 
            style={{ 
                ... props.style,
                cursor: areControlsVisible() ? undefined : "none"
            }} 
            onMouseMove={handleMouseMove}
            onMouseLeave={hideControls}
            onDblClick={handleDblClick}
            use:focusable={focusableOpts()}>

            <ErrorBoundary fallback={(err, reset) => (<div></div>)}>
                <video ref={videoElement} style="width: 100%; height: 100%;" onclick={()=>console.log("received click")}></video>
            </ErrorBoundary>
            
            <div class={styles.containerCasting} style={{"display": isAudioOnly() || isCasting() ? "block" : "none"}}>
                <Show when={props.source?.thumbnailUrl}>
                    <img src={props.source?.thumbnailUrl} onLoad={(ev) => { setThumbnailDimensions({ width: ev.currentTarget.naturalWidth, height: ev.currentTarget.naturalHeight }); }} referrerPolicy='no-referrer' />
                </Show>
            </div>

            <div
                classList={{
                    [styles.controls]: !isFullscreen(),
                    [styles.controlsFullscreen]: isFullscreen(),
                    [styles.controlsVisible]: controlsVisible$()
                }}>

                <PlayerControlsView
                    chapters={props.chapters}
                    video={props.video}
                    duration={duration()}
                    position={position()}
                    positionBuffered={positionBuffered()}
                    onSkip={onSkip}
                    onInteraction={() => showControls()}
                    onSetScrubbing={(scrubbing) => {
                        setIsScrubbing(scrubbing);
                        props.onSetScrubbing?.(scrubbing);
                    }}
                    onSetPosition={performSetPosition}
                    isPlaying={isPlaying()}
                    onSetVolume={(v) => setVolume(v)}
                    onToggleSubtitles={props.onToggleSubtitles}
                    onIncreasePlaybackSpeed={props.onIncreasePlaybackSpeed}
                    onDecreasePlaybackSpeed={props.onDecreasePlaybackSpeed}
                    onPreviousChapter={() => {
                        const chapter = previousChapter();
                        if (!chapter) {
                            const currentChapter = currentChapter$();
                            if (!currentChapter) {
                                return;
                            }

                            performSetPosition(Duration.fromMillis(currentChapter.timeStart * 1000));
                            return;
                        }

                        performSetPosition(Duration.fromMillis(chapter.timeStart * 1000));
                    }}
                    onNextChapter={() => {
                        const chapter = nextChapter();
                        if (!chapter) {
                            const currentChapter = currentChapter$();
                            if (!currentChapter) {
                                return;
                            }

                            performSetPosition(Duration.fromMillis(currentChapter.timeEnd * 1000));
                            return;
                        }

                        performSetPosition(Duration.fromMillis(chapter.timeStart * 1000));
                    }}
                    onPreviousVideo={props.onPreviousVideo}
                    onNextVideo={props.onNextVideo}
                    onOpenSearch={props.onOpenSearch}
                    onSingleFrameForward={() => {
                        if (paused())
                            performSetPosition(Duration.fromMillis(Math.min(duration().milliseconds, position().milliseconds + 1000 / (frameRate ?? 24))));
                    }}
                    onSingleFrameBackward={() => {
                        if (paused())
                            performSetPosition(Duration.fromMillis(Math.max(0, position().milliseconds - 1000 / (frameRate ?? 24))));
                    }}
                    isLive={props.source?.isLive}
                    volume={currentVolume$()}
                    handlePause={togglePlay}
                    handleToggleVolume={toggleVolume}
                    handlePlay={togglePlay}
                    handleFullscreen={toggleFullscreen}
                    handleSettingsMenu={props.onSettingsDialog}
                    handleTheatre={props.handleTheatre}
                    handleEscape={handleEscape}
                    handleMinimize={handleMinimize}
                    eventMoved={props.eventMoved}
                    buttons={props.buttons}
                    leftButtonContainerStyle={props.leftButtonContainerStyle}
                    rightButtonContainerStyle={props.rightButtonContainerStyle}>
                    {props.children}
                </PlayerControlsView>
            </div>

            <div ref={videoCaptionsRef} class={styles.captionsContainer} style={{"bottom": controlsVisible$() ? "100px" : "18px"}}></div>

            <Show when={isLoading() && !isCasting()}>
                <div class={styles.loader}>
                    <CircleLoader />
                </div>
                <Show when={props.loaderUI}>
                    {props.loaderUI}
                </Show>
            </Show>

            <Show when={props.resumePosition && resumePositionVisible()}>
                <div class={styles.resumeButton} onClick={() => {
                    if (props.resumePosition) {
                        setCurrentPosition(props.resumePosition);
                    }
                }}>
                    Resume at {formatDuration(props.resumePosition!)}
                </div>
            </Show>

            <Show when={loaderGameVisible$()}>
                <div style="position: absolute; top: 0px; width: 100%; height: 100%;">
                    <LoaderGame
                        duration={loaderGameVisible$()}
                        onReady={(h) => {
                            loader = h;
                            h?.startLoader(10000);
                        }}
                        style={{ width: "100%", height: "100%" }}
                    />
                </div>
                <Show when={props.loaderUI}>
                    {props.loaderUI}
                </Show>
            </Show>
        </div>
    );
};

export default VideoPlayerView;
