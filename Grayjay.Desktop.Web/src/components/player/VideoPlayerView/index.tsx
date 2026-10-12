import { Accessor, Component, ErrorBoundary, JSX, Show, batch, createEffect, createMemo, createSignal, on, onCleanup, onMount, untrack } from 'solid-js';
import styles from './index.module.css';
import PlayerControlsView from '../PlayerControlsView';
import { Duration } from 'luxon';
import { SourceSelected } from '../../contentDetails/VideoDetailView';
import { DetailsBackend, ISourceDrm } from '../../../backend/DetailsBackend';
import { CastConnectionState, useCasting } from '../../../contexts/Casting';
import { CastingBackend } from '../../../backend/CastingBackend';
import { Event0 } from "../../../utility/Event";
import * as dashjs from 'dashjs';
import Hls, { type ErrorData } from 'hls.js';
import { UmpFormatInfo, UmpPlayer } from '../UmpPlayer/UmpPlayer';
import { DashAudioTrack, DashTrackSelection, DashVideoRepresentation, describeAudioTrack, groupAudioTracks, pickDefaultTracks, pickInitialAudioTrack } from './dashTracks';
import { ChapterType, IChapter } from '../../../backend/models/contentDetails/IChapter';
import { IPlatformVideoDetails } from '../../../backend/models/contentDetails/IPlatformVideoDetails';
import CircleLoader from '../../basics/loaders/CircleLoader';
import { formatDuration, getDefaultPlaybackSpeed, uuidv4 } from '../../../utility';
import { LoaderGame, LoaderGameHandle } from '../../LoaderGame';
import StateWebsocket from '../../../state/StateWebsocket';
import Globals from '../../../globals';
import { clearLiveChatOnSeek } from '../../../state/StateLiveChat';
import { focusable } from '../../../focusable'; void focusable;
import { FocusableOptions, InputSource } from '../../../nav';
import { SettingsBackend } from '../../../backend/SettingsBackend';
import ExceptionModel from '../../../backend/exceptions/ExceptionModel';
import UIOverlay from '../../../state/UIOverlay';
import { PlaybackSetupCancelled, preparePlayback } from '../LinuxCdm/Prepare';
import { PlaybackSession } from '../LinuxCdm/Session';
import { createHelperHls } from '../LinuxCdm/Hls';
import { attachHelperDash } from '../LinuxCdm/Dash';

// dash.js error payloads can reference themselves (a segment request carries its representation), which JSON.stringify rejects.
const stringifyDashError = (error: unknown): string => {
    const seen = new WeakSet<object>();
    try {
        return JSON.stringify(error, (_key, value: unknown) => {
            if (typeof value === "object" && value !== null) {
                if (seen.has(value)) {
                    return "[Circular]";
                }
                seen.add(value);
            }
            return value;
        });
    } catch {
        return String(error);
    }
};

export type PlaybackErrorKind = "drm-license" | "drm" | "drm-unsupported" | "generic";

type DashErrorPayload = {
    code?: number;
    data?: {
        responseCode?: number;
    };
};

interface VideoProps {
    onVideoDimensionsChanged: (width: number, height: number) => void;
    children: JSX.Element;
    video?: IPlatformVideoDetails,
    source?: SourceSelected;
    sourceQuality?: number;
    onPlayerQualityChanged?: (level: number) => void;
    umpVideoKey?: string;
    umpAudioKey?: string;
    onUmpInitialVideoFormat?: (format: UmpFormatInfo) => void;
    onUmpFormats?: (video: UmpFormatInfo[], audio: UmpFormatInfo[]) => void;
    onUmpActiveFormat?: (role: "video" | "audio", format: UmpFormatInfo) => void;
    dashVideoRepresentationId?: string;
    dashAudioTrackKey?: string;
    preferredAudioLanguage?: string | null;
    preferOriginalAudio?: boolean;
    onDashTracks?: (tracks: DashTrackSelection) => void;
    onDashActiveRepresentation?: (representationId: string | undefined) => void;
    onSettingsDialog?: (event: HTMLElement|undefined) => void;
    onFullscreenChange?: (isFullscreen: boolean) => void;
    onToggleSubtitles?: () => void;
    onProgress?: (progress: number) => void;
    onEnded?: () => void;
    onError?: (message: string, fatal: boolean, kind: PlaybackErrorKind, reloadable?: boolean) => void;
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
    seek(time: Duration): Promise<void>;
};

function absoluteUrl(relativeUrl: string): string {
    return new URL(relativeUrl, window.location.origin).toString();
}

const VideoPlayerView: Component<VideoProps> = (props) => {
    const casting = useCasting()!;
    
    let videoCaptionsRef: HTMLDivElement | undefined;
    let videoElement: HTMLVideoElement | undefined;
    let containerRef: HTMLDivElement | undefined;
    let dashPlayer: dashjs.MediaPlayerClass | undefined;
    let hlsPlayer: Hls | undefined;
    let umpPlayer: UmpPlayer | undefined;
    let timeout: NodeJS.Timeout | undefined;
    let volumeBeforeMute: number | undefined = undefined;
    let subtitleMap: Map<string, HTMLParagraphElement> = new Map<string, HTMLParagraphElement>();
    let sideLoadedSubtitleTrack: HTMLTrackElement | undefined;
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
    const [protectedPlaybackMessage, setProtectedPlaybackMessage] = createSignal<string>();
    const [resumePositionVisible, setResumePositionVisible] = createSignal(false);
    const [endControlsVisible$, setEndControlsVisible] = createSignal(false);
    const [loaderGameVisible$, setLoaderGameVisible] = createSignal<number>();
    let frameRate: number | undefined = undefined; //TODO: Framerate is currently not accurate, not properly exposed by video,hlsjs,dashjs, would need to feed it in from sources
    let currentUrl: string | undefined;
    // License URLs carry the backend's video generation, so a reload of the same manifest gets a new one.
    let currentDrmKey: string | undefined;
    let stoppedByDrmError = false;
    // A key system that worked once stays usable, so later DRM sources start without the check.
    const verifiedKeySystems = new Set<string>();
    let keySystemCheckId = 0;
    let loader: LoaderGameHandle | undefined;
    let currentTag = uuidv4();
    let setupCancellation: AbortController | undefined;
    let helperCleanup: (() => void) | undefined;

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
                    void seek(Duration.fromMillis(chapter.timeEnd * 1000));
                }
            }
            else if(chapter.type == ChapterType.SKIPONCE) {
                if(skippedOnce.indexOf(chapter) < 0) {
                    skippedOnce.push(chapter);
                    void seek(Duration.fromMillis(chapter.timeEnd * 1000));
                }
            }
        }
    });
    function onSkip() {
        const chapter = currentChapter$();
        if(!chapter)
            return;
        void seek(Duration.fromMillis(chapter.timeEnd * 1000));
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

    type CastLoadRequest = {
        tag: string;
        source: SourceSelected;
        resumePosition: Duration;
        duration: Duration;
        title?: string;
    };

    const [pendingCastLoad, setPendingCastLoad] = createSignal<CastLoadRequest | null>(null);
    let lastLoadedCastKey: string | undefined;
    const [castUpdateInFlight, setCastUpdateInFlight] = createSignal(false);
    let loadedCast: { deviceId: string, source: SourceSelected } | undefined;
    let lastLocalPositionBeforeCast = Duration.fromMillis(0);

    const computeResumeForCast = (source: SourceSelected) => {
        if (source.isLive) return Duration.fromMillis(0);
        if (source.shouldResume) {
            return untrack(position);
        }

        return source.time ?? Duration.fromMillis(0);
    };


    const stopCastingIfApplicable = async () => {
        await CastingBackend.mediaStop();
    };

    const changeSourceToSetSource = async (source: SourceSelected | undefined) => {
        const selectionTag = uuidv4();
        currentTag = selectionTag;
        setupCancellation?.abort();
        const setup = setupCancellation = new AbortController();
        setLoaderGameVisible(undefined);

        console.info("source", source);
        if (!source || (source.video == -1 && source.audio == -1)) {
            if (untrack(isCasting)) {
                await CastingBackend.mediaStop();
                loadedCast = undefined;
                lastLoadedCastKey = undefined;
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

        if (untrack(isCasting)) {
            console.info("start casting because changeSourceToSetSource call and casting");
            const resumePosition = computeResumeForCast(source);
            setPendingCastLoad({
                tag: currentTag,
                source,
                resumePosition,
                duration: untrack(duration),
                title: props.video?.name
            });
            return;
        }

        let descriptor;
        try {
            descriptor = await DetailsBackend.sourceProxy(source.url, source.video, source.videoIsLocal, source.audio, source.audioIsLocal, source.subtitle, source.subtitleIsLocal, selectionTag);
        } catch (error) {
            if (currentTag !== selectionTag) return;
            if (error instanceof ExceptionModel) {
                // Menu source changes have no dialog handler above them, and dialogs render outside the fullscreen player.
                await syncFullscreenToDom(false);
                UIOverlay.overlayError(error, { retry: () => changeSourceToSetSource(source) });
                return;
            }
            throw error;
        }
        if (currentTag !== selectionTag) return;
        console.log("Direct url", descriptor.url, descriptor.type);

        console.info("change source because changeSourceToSetSource call");
        try {
            const helper = descriptor.drm?.keySystem === 'com.widevine.alpha' &&
                ['application/dash+xml', 'application/vnd.apple.mpegurl', 'application/x-mpegURL'].includes(descriptor.type)
                ? await preparePlayback(setup.signal) : false;
            if (currentTag !== selectionTag) return;
            changeSource(descriptor.url, descriptor.type, source.shouldResume, source.time, descriptor.drm, helper);
        } catch (ex) {
            if (currentTag !== selectionTag || (ex instanceof DOMException && ex.name === 'AbortError')) return;
            if (ex instanceof PlaybackSetupCancelled) {
                setIsLoading(false);
                await syncFullscreenToDom(false);
                UIOverlay.overlay({
                    dialog: {
                        title: "Protected playback setup cancelled",
                        description: "This content needs Widevine to play.",
                        buttons: [
                            { title: "Close", onClick: () => {}, style: "none" },
                            { title: "Set up", onClick: () => { if (currentTag === selectionTag) void changeSourceToSetSource(source); }, style: "primary" }
                        ]
                    }
                });
                return;
            }
            console.error("Failed to load source", ex);
            onError(String(ex), true, 'drm');
        }
    };

    createEffect(on(isCasting, async (isCurrentlyCasting) => {
        if (casting && isCurrentlyCasting) {
            console.info("start casting because isCasting change");
            changeSource(undefined);
            // Keep the local position until the device reports one, so a refused cast resumes here.
            setPosition(lastLocalPositionBeforeCast);
            stopHideControls();

            const s = props.source;
            if (s) {
                setPendingCastLoad({
                    tag: currentTag,
                    source: s,
                    resumePosition: lastLocalPositionBeforeCast,
                    duration: untrack(duration),
                    title: props.video?.name
                });
            }
        } else {
            console.info("stop casting because isCasting change");
            //TODO: playWhenReady = casting?.activeDevice.device()?.isPlaying() ?? false
            setPendingCastLoad(null);
            lastLoadedCastKey = undefined;
            loadedCast = undefined;
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
        }
    });

    createEffect(() => {
        if (isScrubbing())
            stopHideControls();
        else if (!untrack(isCasting))
            startHideControls();
    })

    createEffect(on(casting.activeDevice.device, (device) => {
        const isCurrentlyCasting = !!device;
        if (isCurrentlyCasting && !isCasting()) {
            lastLocalPositionBeforeCast = untrack(position);
        }
        setIsCasting(isCurrentlyCasting);
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

    createEffect(on(casting.activeDevice.mediaItemEnd, () => {
        if (!casting) {
            return;
        }

        props.onEnded?.();
        console.info("casting video ended");
    }));

    // Only a time reported by the device moves the position; the last report may be stale.
    createEffect(on(casting.activeDevice.time, (time) => {
        if (!untrack(isCasting) || untrack(isScrubbing)) {
            return;
        }

        setPosition(time);
        setPositionBuffered(time);

        const timeLeft = duration().minus(time);
        console.log("Received position", {time_s: time.as('seconds'), timeLeft_s: timeLeft.as('seconds')});
    }, { defer: true }));

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
        if (stoppedByDrmError) {
            // After a fatal error, stay stopped until the source is reloaded.
            return;
        }
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

    const cueText = (text: string): string => {
        if (!text || !text.includes("<") && !text.includes("&"))
            return text ?? "";
        const stripped = text.replace(/<[^>]*>/g, "");
        const decoded = stripped.includes("&") ? (new DOMParser().parseFromString(stripped, "text/html").documentElement.textContent ?? stripped) : stripped;
        return decoded.split("\n").map(x => x.replace(/[\u200B\s]+/g, " ").trim()).filter(x => x.length > 0).join("\n");
    };

    const umpLiveWindow = (): { start: number, end: number } | undefined => {
        if (!umpPlayer?.isLive || !videoElement)
            return undefined;
        const seekable = videoElement.seekable;
        if (!seekable || seekable.length === 0)
            return undefined;
        const start = seekable.start(0);
        const end = seekable.end(seekable.length - 1);
        return isFinite(start) && isFinite(end) && end > start ? { start, end } : undefined;
    };

    const seekLocal = (time: Duration) => {
        const seconds = time.as('seconds') + (umpLiveWindow()?.start ?? 0);
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

        if (shouldSetCurrentPosition && (props.source?.isLive ?? false) === false) {
            seekLocal(getResumePosition(shouldResume, startTime));
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

    const onError = (error: string, fatal: boolean, kind: PlaybackErrorKind = "generic", reloadable?: boolean) => {
        props.onError?.(error, fatal, kind, reloadable);
        if (fatal) {
            setIsLoading(false);
            setProtectedPlaybackMessage(undefined);
            setLoaderGameVisible(undefined);
            setIsPlaying(false);
            if (kind !== "generic") {
                // Stop at a DRM error instead of playing the unencrypted lead under the dialog.
                stoppedByDrmError = true;
                pause();
            }
        }
    };

    createEffect(() => {
        const resumePosition = props.resumePosition;
        const pos = position();
        const dur = duration();
        //console.log("resumePosition", { resumePosition: resumePosition?.as('milliseconds'), dur: dur.as('milliseconds'), pos: pos.as('milliseconds') });
        if (!resumePosition) {
            setResumePositionVisible(false);
            return;
        }

        const pos_ms = pos.as('milliseconds');
        const res_ms = resumePosition.as('milliseconds');
        const dur_ms = dur.as('milliseconds');
        const visible = res_ms > 60000 && dur_ms - res_ms > 5000 && res_ms - pos_ms > 5000 && pos_ms < 8000;
        //console.log("resumePosition", { a: res_ms > 60000, b: dur_ms - res_ms > 5000, c: res_ms - pos_ms > 5000, d: pos_ms < 8000, res_ms, dur_ms, pos_ms });
        setResumePositionVisible(visible);
    });

    const describeDashAudioTracks = (player: dashjs.MediaPlayerClass): DashAudioTrack[] =>
        groupAudioTracks(player.getTracksFor("audio").map(track => describeAudioTrack(player, track)));

    // The user's track, or the settings default when the user has not chosen one or this Period lacks it.
    const resolveDashAudioTrack = (tracks: DashAudioTrack[]): DashAudioTrack | undefined =>
        tracks.find(track => track.key === untrack(() => props.dashAudioTrackKey))
            ?? pickInitialAudioTrack(tracks, untrack(() => props.preferredAudioLanguage), untrack(() => props.preferOriginalAudio));

    // Preferences the active Period lacks are kept, so a later Period that offers them gets them back.
    const refreshDashTracks = () => {
        const player = dashPlayer;
        if (!player) {
            return;
        }
        try {
            const representations: DashVideoRepresentation[] = player.getRepresentationsByType("video").map(representation => ({
                id: representation.id,
                width: representation.width,
                height: representation.height,
                bandwidth: representation.bandwidth
            }));
            const audioTracks = describeDashAudioTracks(player);

            const preferredRepresentationId = untrack(() => props.dashVideoRepresentationId);
            const offersPreferredRepresentation = representations.some(representation => representation.id === preferredRepresentationId);
            // A manual quality stays manual in a Period without it; dash.js then keeps the nearest bitrate.
            player.updateSettings({ streaming: { abr: { autoSwitchBitrate: { video: preferredRepresentationId === undefined } } } });
            if (preferredRepresentationId !== undefined && offersPreferredRepresentation) {
                // A forced replace leaves dash.js gap jumping off for the session and stalls on manifests with nominal segment durations.
                // dash.js 5.0.3 types the id as a number but matches it with === against the string Representation.id.
                player.setRepresentationForTypeById("video", preferredRepresentationId as never, false);
            }

            const audioTrack = resolveDashAudioTrack(audioTracks);
            const currentAudioTrack = player.getCurrentTrackFor("audio");
            const currentAudioTrackKey = currentAudioTrack ? describeAudioTrack(player, currentAudioTrack).key : undefined;
            if (audioTrack && audioTrack.key !== currentAudioTrackKey) {
                player.setCurrentTrack(audioTrack.mediaInfo, true);
            }

            const currentRepresentationId = player.getCurrentRepresentationForType("video")?.id;
            let representationId: string | undefined = undefined;
            if (preferredRepresentationId !== undefined) {
                representationId = offersPreferredRepresentation ? preferredRepresentationId : currentRepresentationId;
            }
            batch(() => {
                props.onDashTracks?.({ representations, audioTracks, representationId, audioTrackKey: audioTrack?.key ?? currentAudioTrackKey });
                props.onDashActiveRepresentation?.(currentRepresentationId);
            });
        } catch (e) {
            console.warn("Failed to refresh the DASH tracks", e);
        }
    };

    const SIDE_LOADED_CUE_PREFIX = "sideLoaded:";

    const clearSideLoadedCues = () => {
        for (const [cueId, subtitle] of subtitleMap) {
            if (cueId.startsWith(SIDE_LOADED_CUE_PREFIX)) {
                subtitle.remove();
                subtitleMap.delete(cueId);
            }
        }
    };

    // DRM DASH manifests carry no subtitles, so the player side-loads the WebVTT URL the backend names.
    const setSideLoadedSubtitle = (subtitleUrl?: string) => {
        if (sideLoadedSubtitleTrack?.getAttribute("src") === subtitleUrl) {
            return;
        }

        if (sideLoadedSubtitleTrack) {
            sideLoadedSubtitleTrack.track.oncuechange = null;
            sideLoadedSubtitleTrack.remove();
            sideLoadedSubtitleTrack = undefined;
        }
        clearSideLoadedCues();

        if (!subtitleUrl || !videoElement) {
            return;
        }

        const trackElement = document.createElement("track");
        trackElement.kind = "subtitles";
        trackElement.label = "Subtitles";
        trackElement.default = true;
        trackElement.src = subtitleUrl;
        videoElement.appendChild(trackElement);
        // Cues go to the captions container like dash cues, so the audio-only thumbnail does not hide them.
        trackElement.track.mode = "hidden";
        trackElement.track.oncuechange = () => {
            clearSideLoadedCues();
            Array.from(trackElement.track.activeCues ?? []).forEach((cue, cueIndex) => {
                const subtitle = document.createElement("div");
                subtitle.textContent = cueText((cue as VTTCue).text);
                subtitleMap.set(SIDE_LOADED_CUE_PREFIX + cueIndex, subtitle);
                videoCaptionsRef?.appendChild(subtitle);
            });
        };
        sideLoadedSubtitleTrack = trackElement;
    };

    // Resolves when the key system can create media keys; audio-only and video-only configs both count.
    const checkKeySystemAccess = async (keySystem: string): Promise<void> => {
        if (!navigator.requestMediaKeySystemAccess) {
            throw new Error("EME is not supported");
        }
        const robustness = keySystem === "com.widevine.alpha" ? "SW_SECURE_CRYPTO" : "";
        const access = await navigator.requestMediaKeySystemAccess(keySystem, [
            { initDataTypes: ["cenc"], videoCapabilities: [{ contentType: 'video/mp4; codecs="avc1.42E01E"', robustness }] },
            { initDataTypes: ["cenc"], audioCapabilities: [{ contentType: 'audio/mp4; codecs="mp4a.40.2"', robustness }] }
        ]);
        await access.createMediaKeys();
    };

    const changeSource = (sourceUrl?: string, mediaType?: string, shouldResume?: boolean, startTime?: Duration, drm?: ISourceDrm, helper = false) => {
        //TODO: Implement playWhenReady ?
        console.info("changeSource", {sourceUrl, mediaType, shouldResume, startTime, drm});

        const drmKey = drm ? `${drm.licenseUrl}|${drm.certificateUrl ?? ""}` : undefined;
        if (currentUrl === sourceUrl && currentDrmKey === drmKey) {
            // A subtitle toggle on a DRM source keeps the manifest URL, so only the track changes.
            setSideLoadedSubtitle(drm?.subtitleUrl);
            if (startTime) {
                const startTime_ms = startTime.as('milliseconds');
                const currentTime_ms = position().as('milliseconds');
                if (Math.abs(startTime_ms - currentTime_ms) < 5000) {
                    console.warn("Skipped changing video URL because URL and time is (nearly) unchanged", {sourceUrl, currentUrl, shouldResume, startTime: startTime ? formatDuration(startTime) : undefined, switchPosition: switchPosition ? formatDuration(switchPosition) : undefined});
                } else {
                    console.info("Skipped changing video URL because URL is the same, but time was changed, seeking instead", {sourceUrl, currentUrl, shouldResume, startTime: startTime ? formatDuration(startTime) : undefined, switchPosition: switchPosition ? formatDuration(switchPosition) : undefined});
                    seekLocal(startTime);
                }
            }
            return;
        }

        if (sourceUrl !== undefined && currentUrl === sourceUrl && !startTime) {
            // Same manifest with a new license URL: reload in place instead of restarting.
            shouldResume = true;
        }

        setIsAudioOnly(false);
        setIsPlaying(false);
        stoppedByDrmError = false;
        keySystemCheckId++;
        frameRate = undefined;

        if (!untrack(isCasting))
            switchPosition = untrack(position);

        currentUrl = sourceUrl;
        currentDrmKey = drmKey;
        console.log("changeSource", {currentUrl, sourceUrl, mediaType, shouldResume, startTime, switchPosition});

        for (const subtitle of subtitleMap.values()) {
            subtitle.remove();
        }

        subtitleMap.clear();
        setSideLoadedSubtitle(undefined);

        const currentVolume = currentVolume$();
        helperCleanup?.();
        helperCleanup = undefined;
        if (dashPlayer) {
            try {
                dashPlayer.destroy();
            } catch (e) {
                console.warn("Failed to destroy dash player", e);
            }
            dashPlayer = undefined;
            batch(() => {
                props.onDashTracks?.({ representations: [], audioTracks: [] });
                props.onDashActiveRepresentation?.(undefined);
            });
        }

        if (hlsPlayer) {
            hlsPlayer.destroy();
            hlsPlayer = undefined;
        }

        if (umpPlayer) {
            umpPlayer.destroy();
            umpPlayer = undefined;
        }

        if (videoElement) {
            videoElement.src = "";
            videoElement.onerror = null;
            videoElement.onloadedmetadata = null;
            videoElement.onloadeddata = null;
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

        if (sourceUrl && mediaType && drm && !helper && !verifiedKeySystems.has(drm.keySystem)) {
            // Without usable EME the players stall or report no streams, so the key system is checked first.
            const checkId = keySystemCheckId;
            setIsLoading(true);
            checkKeySystemAccess(drm.keySystem).then(() => {
                if (checkId !== keySystemCheckId) {
                    return;
                }
                verifiedKeySystems.add(drm.keySystem);
                startSource(sourceUrl, mediaType, shouldResume, startTime, drm, currentVolume, helper);
            }, (error: unknown) => {
                if (checkId !== keySystemCheckId) {
                    return;
                }
                setIsLoading(false);
                onError(`Key system ${drm.keySystem} is not available: ${String(error)}`, true, "drm-unsupported");
            });
            return;
        }

        startSource(sourceUrl, mediaType, shouldResume, startTime, drm, currentVolume, helper);
    };

    const startSource = (sourceUrl: string | undefined, mediaType: string | undefined, shouldResume: boolean | undefined, startTime: Duration | undefined, drm: ISourceDrm | undefined, currentVolume: number, helper: boolean) => {
        setProtectedPlaybackMessage(undefined);
        if (sourceUrl && mediaType && videoElement) {
            setIsLoading(helper && !!drm);
            if (helper && drm) {
                videoElement.onloadeddata = () => {
                    setIsLoading(false);
                    setProtectedPlaybackMessage(undefined);
                };
            }

            if (mediaType === 'application/dash+xml' && !videoElement.canPlayType(mediaType)) {
                dashPlayer = dashjs.MediaPlayer().create();
                const initializedStreamIds = new Set<string>();
                dashPlayer.updateSettings({
                    streaming: {
                        text: {
                            dispatchForManualRendering: true
                        },
                        manifestRequestTimeout: 60000,
                        retryAttempts: {
                            license: 0
                        },
                        // The cached choice is shared by every plugin and would override the Primary Language setting.
                        lastMediaSettingsCachingInfo: {
                            enabled: false
                        }
                    }
                });

                dashPlayer.on(dashjs.MediaPlayer.events.PLAYBACK_PLAYING, () => {
                    if (isCasting()) {
                        return;
                    }
                    if (stoppedByDrmError) {
                        // Media keys play the element directly, bypassing play().
                        pause();
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
                // A preloaded Period initializes while the previous one plays and fires no event when it becomes active.
                dashPlayer.on(dashjs.MediaPlayer.events.PERIOD_SWITCH_COMPLETED, () => {
                    updateFps();
                    refreshDashTracks();
                });
                dashPlayer.on(dashjs.MediaPlayer.events.QUALITY_CHANGE_RENDERED, e => {
                    if (e.mediaType === 'video') {
                        props.onDashActiveRepresentation?.(e.newRepresentation?.id);
                    }
                });
                dashPlayer.on(dashjs.MediaPlayer.events.REPRESENTATION_SWITCH, e => updateFps());
                // A manifest refresh fires STREAM_UPDATED for existing streams; the first one comes before STREAM_INITIALIZED.
                dashPlayer.on(dashjs.MediaPlayer.events.STREAM_UPDATED, (e: dashjs.MediaPlayerEvent & { streamInfo?: dashjs.StreamInfo }) => {
                    const streamId = e.streamInfo?.id;
                    if (streamId !== undefined && initializedStreamIds.has(streamId) && streamId === dashPlayer?.getActiveStream()?.getId()) {
                        refreshDashTracks();
                    }
                });
                dashPlayer.on(dashjs.MediaPlayer.events.STREAM_INITIALIZED, e => {
                    if (e.streamInfo?.id !== undefined) {
                        initializedStreamIds.add(e.streamInfo.id);
                    }
                    if (e.streamInfo?.id !== dashPlayer?.getActiveStream()?.getId()) {
                        return;
                    }
                    updateFps();
                    refreshDashTracks();
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
                    // dash.js 5.1+ re-adds active cues when it refreshes its cue window, which fires enter again.
                    if (subtitleMap.has(e.cueID)) {
                        return;
                    }
                    const subtitle = document.createElement("div")
                    subtitle.textContent = cueText(e.text);
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

                const isKnownErrorCode = (code: number | undefined): code is number => code !== undefined;

                const drmErrorCodes = (): number[] => ([
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
                ] as (number | undefined)[]).filter(isKnownErrorCode);

                const fatalErrorCodes = (): number[] => ([
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

                    ...drmErrorCodes(),

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
                ] as (number | undefined)[]).filter(isKnownErrorCode);

                const classifyDashError = (code: number | undefined): PlaybackErrorKind => {
                    if (code === undefined) {
                        return "generic";
                    }
                    if (code === dashjs.MediaPlayer.errors.MEDIA_KEY_MESSAGE_LICENSER_ERROR_CODE) {
                        return "drm-license";
                    }
                    if (drm && code === dashjs.MediaPlayer.errors.MANIFEST_ERROR_ID_NOSTREAMS_CODE) {
                        // A protected manifest reports no streams when the browser cannot decrypt it.
                        return "drm";
                    }
                    return drmErrorCodes().includes(code) ? "drm" : "generic";
                };

                dashPlayer.on(dashjs.MediaPlayer.events.ERROR, (data) => {
                    console.error("DashJS ERROR", data);
                    const dashError = data.error as DashErrorPayload | undefined;
                    const code = dashError?.code;
                    const responseCode = dashError?.data?.responseCode;
                    const statusSuffix = responseCode !== undefined ? ` (license server status ${responseCode})` : "";
                    // A 409 means the license URL is from before a reload of this video, so reloading fetches a current one.
                    onError(`DashJS Error${statusSuffix}: ${stringifyDashError(data.error)}`, code !== undefined ? fatalErrorCodes().includes(code) : false, classifyDashError(code), responseCode === 409);
                });

                dashPlayer.on(dashjs.MediaPlayer.events.PLAYBACK_ERROR, (data) => {
                    console.error("DashJS PLAYBACK_ERROR", data);
                    const code = (data.error as DashErrorPayload | undefined)?.code;
                    onError(`DashJS Playback Error: ${stringifyDashError(data.error)}`, code !== undefined ? fatalErrorCodes().includes(code) : false);
                });

                const trackSelectionPlayer = dashPlayer;
                dashPlayer.setCustomInitialTrackSelectionFunction((tracks: dashjs.MediaInfo[]) => {
                    if (tracks[0]?.type !== 'audio') {
                        return pickDefaultTracks(tracks);
                    }
                    const chosenTrack = resolveDashAudioTrack(groupAudioTracks(tracks.map(track => describeAudioTrack(trackSelectionPlayer, track))));
                    return chosenTrack ? [chosenTrack.mediaInfo] : tracks;
                });

                if (helper && drm) {
                    const session = new PlaybackSession(drm, error => onError(error, true, 'drm'), setProtectedPlaybackMessage);
                    helperCleanup = attachHelperDash(dashPlayer, session, error => onError(error, true, 'drm'));
                } else if (drm) {
                    dashPlayer.setProtectionData({
                        [drm.keySystem]: {
                            serverURL: absoluteUrl(drm.licenseUrl),
                            ...(drm.serviceCertificate ? { serverCertificate: drm.serviceCertificate } : {})
                        }
                    });
                }

                // dash.js 5.2 CMCD parses every response URL with new URL(), which throws on a relative manifest URL.
                dashPlayer.initialize(videoElement, new URL(sourceUrl, window.location.href).href, true, getResumePosition(shouldResume, startTime)?.as('seconds') ?? 0);

                setSideLoadedSubtitle(drm?.subtitleUrl);
            } else if ((mediaType === 'application/vnd.apple.mpegurl' || mediaType === 'application/x-mpegURL') && Hls.isSupported()) {
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
                    if (stoppedByDrmError) {
                        // Media keys play the element directly, bypassing play().
                        pause();
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
                
                const hlsBaseConfig = { startPosition: -1 };
                hlsPlayer = helper && drm
                    ? createHelperHls(new PlaybackSession(drm, error => onError(error, true, 'drm'), setProtectedPlaybackMessage))
                    : drm ? new Hls({
                        ...hlsBaseConfig,
                        emeEnabled: true,
                        drmSystems: {
                            [drm.keySystem]: {
                                licenseUrl: absoluteUrl(drm.licenseUrl),
                                ...(drm.certificateUrl ? { serverCertificateUrl: absoluteUrl(drm.certificateUrl) } : {})
                            }
                        }
                    })
                    : new Hls(hlsBaseConfig);

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

                const classifyHlsError = (data: ErrorData): PlaybackErrorKind => {
                    if (data.details === Hls.ErrorDetails.KEY_SYSTEM_LICENSE_REQUEST_FAILED) {
                        return "drm-license";
                    }
                    return data.type === Hls.ErrorTypes.KEY_SYSTEM_ERROR ? "drm" : "generic";
                };

                hlsPlayer.on(Hls.Events.ERROR, function(eventName, data) {
                    console.error("HLS player error", data);
                    const licenseConflict = data.details === Hls.ErrorDetails.KEY_SYSTEM_LICENSE_REQUEST_FAILED && data.response?.code === 409;
                    onError(`HLS Error: ${JSON.stringify({ details: data.details, error: data.error })}`, data.fatal, classifyHlsError(data), licenseConflict);
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

                    const mediaDuration = videoElement?.duration ?? 0;
                    setDuration(Duration.fromMillis(isFinite(mediaDuration) ? mediaDuration * 1000 : 0));
                    onReady(shouldResume, startTime);
                };

                videoElement.ontimeupdate = () => {
                    if (isCasting()) {
                        return;
                    }
        
                    const currentTime = videoElement?.currentTime ?? 0;
                    const liveWindow = umpLiveWindow();
                    const windowStart = liveWindow?.start ?? 0;
                    if (liveWindow)
                        setDuration(Duration.fromMillis((liveWindow.end - liveWindow.start) * 1000));
                    setPosition(Duration.fromMillis(Math.max(0, currentTime - windowStart) * 1000));

                    if (videoElement && videoElement.buffered) {
                        const buffered = videoElement.buffered;
                        for (let i = 0; i < buffered.length; i++) {
                            const start = buffered.start(i);
                            const end = buffered.end(i);
                    
                            if (currentTime >= start && currentTime <= end) {
                                setPositionBuffered(Duration.fromMillis(Math.max(0, end - windowStart) * 1000));
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

                if (mediaType === 'application/vnd.yt-ump') {
                    const player = new UmpPlayer(videoElement, sourceUrl, {
                        onError: (message, fatal, kind) => onError(message, fatal, "generic", kind === "reload" || kind === "blocked"),
                        onCueEnter: (id, text) => {
                            const subtitle = document.createElement("div");
                            subtitle.textContent = cueText(text);
                            subtitleMap.set(id, subtitle);
                            videoCaptionsRef?.appendChild(subtitle);
                        },
                        onCueExit: (id) => {
                            const subtitle = subtitleMap.get(id);
                            if (subtitle) {
                                subtitleMap.delete(id);
                                subtitle.remove();
                            }
                        },
                        onInitialVideoFormat: (format) => props.onUmpInitialVideoFormat?.(format),
                        onFormatsChanged: (video, audio) => props.onUmpFormats?.(video, audio),
                        onActiveFormatChanged: (role, format) => props.onUmpActiveFormat?.(role, format)
                    });
                    umpPlayer = player;
                    player.setVideoFormat(untrack(() => props.umpVideoKey));
                    player.setAudioFormat(untrack(() => props.umpAudioKey));
                    player.start(getResumePosition(shouldResume, startTime)?.as('seconds') ?? 0);
                } else {
                    videoElement.src = sourceUrl;
                    videoElement.load();
                }
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
    createEffect(() => {
        const key = props.umpVideoKey;
        umpPlayer?.setVideoFormat(key);
    });
    createEffect(() => {
        const key = props.umpAudioKey;
        umpPlayer?.setAudioFormat(key);
    });
    // Settings read after the initial pick still move playback to the preferred language, unless the user chose a track.
    createEffect(on(() => [props.dashVideoRepresentationId, props.dashAudioTrackKey, props.preferredAudioLanguage, props.preferOriginalAudio], () => {
        refreshDashTracks();
    }, { defer: true }));

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
            await seek(start);

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
            toggleMute: toggleVolume,
            seek
        });
    });

    onCleanup(async () => {
        currentTag = uuidv4();
        setupCancellation?.abort();
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

    const seek = async (time: Duration) => { 
        clearLiveChatOnSeek();

        const isLive = props.source?.isLive ?? false;
        const maxMs = duration().toMillis();
        const clamped = isLive ? time : Duration.fromMillis(Math.max(0, Math.min(time.toMillis(), maxMs)));

        setPosition(clamped);
        if (isCasting()) {
            await CastingBackend.mediaSeek(clamped);
        } else {
            seekLocal(clamped);
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
                        void seek(Duration.fromMillis(Math.max(Math.min(duration().toMillis(), position().toMillis() - 5000), 0)));
                        return true;
                    case 'right':
                        void seek(Duration.fromMillis(Math.max(Math.min(duration().toMillis(), position().toMillis() + 5000), 0)));
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

    createEffect(on(
        () => [isCasting(), casting.activeDevice.state(), pendingCastLoad(), castUpdateInFlight()] as const,
        async ([castingNow, state, req, inFlight]) => {
            if (inFlight) return;
            if (!castingNow) return;
            if (state !== CastConnectionState.Connected) return;
            if (!req) return;

            const key = `${req.tag}|${req.source.url}|${req.source.video}|${req.source.audio}|${req.source.subtitle}|${req.resumePosition.toMillis()}`;
            if (key === lastLoadedCastKey) return;
            casting.actions.close();
            const deviceId = casting.activeDevice.device()?.id;
            const previous = loadedCast && loadedCast.deviceId === deviceId ? loadedCast.source : undefined;
            const source = req.source;
            const subtitleOnly = previous && source.shouldResume
                && previous.url === source.url && previous.video === source.video && previous.audio === source.audio
                && previous.videoIsLocal === source.videoIsLocal && previous.audioIsLocal === source.audioIsLocal
                && (previous.subtitle !== source.subtitle || previous.subtitleIsLocal !== source.subtitleIsLocal);

            setCastUpdateInFlight(true);
            try {
                const changedSubtitle = subtitleOnly && await CastingBackend.changeSubtitle(source.subtitle, source.subtitleIsLocal);
                if (!changedSubtitle) {
                    await CastingBackend.mediaLoad({
                        streamType: source.isLive ? "LIVE" : "BUFFERED",
                        resumePosition: req.resumePosition,
                        duration: req.duration,
                        sourceSelected: source,
                        speed: await getDefaultPlaybackSpeed(),
                        tag: req.tag,
                        title: req.title
                    });
                }
                if (casting.activeDevice.device()?.id !== deviceId) return;
                loadedCast = deviceId ? { deviceId, source: { ...source } } : undefined;
                lastLoadedCastKey = key;
            } catch (error) {
                console.error("Failed to update cast playback", error);
                if (error instanceof ExceptionModel) {
                    // The backend refuses DRM sources with a dialog; stay in cast mode and leave fullscreen so it shows.
                    await syncFullscreenToDom(false);
                    UIOverlay.overlayError(error);
                } else {
                    onError(`Casting failed: ${error instanceof Error ? error.message : String(error)}`, true, "generic", true);
                }
            } finally {
                if (pendingCastLoad() === req) setPendingCastLoad(null);
                setCastUpdateInFlight(false);
            }
        },
        { defer: true }
    ));

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
                    onSetPosition={seek}
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

                            void seek(Duration.fromMillis(currentChapter.timeStart * 1000));
                            return;
                        }

                        void seek(Duration.fromMillis(chapter.timeStart * 1000));
                    }}
                    onNextChapter={() => {
                        const chapter = nextChapter();
                        if (!chapter) {
                            const currentChapter = currentChapter$();
                            if (!currentChapter) {
                                return;
                            }

                            void seek(Duration.fromMillis(currentChapter.timeEnd * 1000));
                            return;
                        }

                        void seek(Duration.fromMillis(chapter.timeStart * 1000));
                    }}
                    onPreviousVideo={props.onPreviousVideo}
                    onNextVideo={props.onNextVideo}
                    onOpenSearch={props.onOpenSearch}
                    onSingleFrameForward={() => {
                        if (paused())
                            void seek(Duration.fromMillis(Math.min(duration().milliseconds, position().milliseconds + 1000 / (frameRate ?? 24))));
                    }}
                    onSingleFrameBackward={() => {
                        if (paused())
                            void seek(Duration.fromMillis(Math.max(0, position().milliseconds - 1000 / (frameRate ?? 24))));
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
                    <Show when={protectedPlaybackMessage()}>
                        <div class={styles.protectedPlaybackStatus} role="status">{protectedPlaybackMessage()}</div>
                    </Show>
                </div>
                <Show when={props.loaderUI}>
                    {props.loaderUI}
                </Show>
            </Show>

            <Show when={props.resumePosition && resumePositionVisible()}>
                <div class={styles.resumeButton} onClick={async () => {
                    if (props.resumePosition) {
                        await seek(props.resumePosition);
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
