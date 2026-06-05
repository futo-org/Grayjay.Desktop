import { createContext, useContext, JSX, ParentComponent, createSignal, Accessor, batch, createMemo, onMount } from "solid-js";
import { range, shuffleArray } from "../utility";
import { IOrderedPlatformVideo, WatchLaterBackend } from "../backend/WatchLaterBackend";
import { IPlatformVideo } from "../backend/models/content/IPlatformVideo";
import { Duration } from "luxon";
import { SettingsBackend } from "../backend/SettingsBackend";
import StateWebsocket from "../state/StateWebsocket";
import { DetailsBackend } from "../backend/DetailsBackend";

export enum VideoState {
    Closed = 0,
    Maximized = 1,
    Minimized,
    Fullscreen
};

export enum VideoMode {
    Standard = 0,
    Theatre
};

export interface VideoContextState {
    state: VideoState;
    index?: number;
    queue?: IPlatformVideo[];
};

export interface VideoContextValue {
    id: string;
    state: Accessor<VideoState>;
    index: Accessor<number | undefined>;
    queue: Accessor<IPlatformVideo[] | undefined>;
    watchLater: Accessor<IOrderedPlatformVideo[] | undefined>;
    video: Accessor<IPlatformVideo | undefined>;
    repeat: Accessor<boolean>;
    shuffle: Accessor<boolean>;
    startTime: Accessor<Duration | undefined>;
    desiredMode: Accessor<VideoMode>;
    theatrePinned: Accessor<boolean>;
    volume: Accessor<number>;
    minimizedVideos: Accessor<VideoContextValue[]>;
    activePlaybackVideoId: Accessor<string | undefined>;
    //queueType watch later, playlist en queue of undefined
    actions: {
        openVideo: (video: IPlatformVideo, time?: Duration, videoState?: VideoState) => void;
        openVideoByUrl: (url: string, time?: Duration, videoState?: VideoState) => void;
        setQueue: (index: number, queue: IPlatformVideo[], repeat?: boolean, shuffle?: boolean, videoState?: VideoState) => void;
        addToQueue: (v: IPlatformVideo) => void;
        setIndex: (index: number) => void;
        setRepeat: (value: boolean) => void;
        setShuffle: (value: boolean) => void;
        closeVideo: () => void;
        setState: (videoState: VideoState) => void;
        refetchWatchLater: () => void;
        setDesiredMode: (mode: VideoMode) => void;
        setTheatrePinned: (pinned: boolean) => void;
        setVolume: (volume: number) => void;
        setStartTime: (startTime: Duration | undefined) => void;
        requestPlayback: () => void;
    }
};

const VideoContext = createContext<VideoContextValue>();
export interface VideoContextProps {
    children: JSX.Element;
};

export const VideoProvider: ParentComponent<VideoContextProps> = (props) => {
    const [watchLater, setWatchLater] = createSignal<IOrderedPlatformVideo[]>();
    const [minimizedVideos, setMinimizedVideos] = createSignal<VideoContextValue[]>([]);
    const [activePlaybackVideoId, setActivePlaybackVideoId] = createSignal<string | undefined>();
    let minimizedVideoId = 0;
    let mainVideo: VideoContextValue;

    const refetchWatchLater = async () => {
        const videos = await WatchLaterBackend.getAll();
        setWatchLater(videos);
        console.log("set watch later", videos);
    };

    const removeMinimizedVideo = (id: string) => {
        setMinimizedVideos(videos => videos.filter(v => v.id !== id));
        if (activePlaybackVideoId() === id) {
            setActivePlaybackVideoId(undefined);
        }
    };

    const createVideoContext = (options: {
        id: string;
        initialState?: VideoState;
        initialIndex?: number;
        initialQueue?: IPlatformVideo[];
        initialStartTime?: Duration;
        initialRepeat?: boolean;
        initialShuffle?: boolean;
        initialDesiredMode?: VideoMode;
        initialTheatrePinned?: boolean;
        initialVolume?: number;
        beforeReplace?: () => void;
        onClose?: (id: string) => void;
        onSetState?: (id: string, videoState: VideoState) => boolean;
        persistSettings?: boolean;
    }): VideoContextValue => {
        const [queue, setQueue] = createSignal<IPlatformVideo[] | undefined>(options.initialQueue);
        const [index, setIndex] = createSignal<number | undefined>(options.initialIndex);
        const [startTime, setStartTime] = createSignal<Duration | undefined>(options.initialStartTime);
        const [state, setState] = createSignal<VideoState>(options.initialState ?? VideoState.Closed);
        const [repeat, setRepeat] = createSignal<boolean>(options.initialRepeat ?? false);
        const [shuffle, setShuffle] = createSignal<boolean>(options.initialShuffle ?? false);
        const [desiredMode, setDesiredModeInternal] = createSignal<VideoMode>(options.initialDesiredMode ?? VideoMode.Theatre);
        const [theatrePinned, setTheatrePinnedInternal] = createSignal<boolean>(options.initialTheatrePinned ?? true);
        const [volume, setVolumeInternal] = createSignal<number>(options.initialVolume ?? 1);
        const video = createMemo(() => {
            const q = queue();
            const i = index();
            if (!q || i === undefined || i < 0 || i >= q.length) {
                return undefined;
            }

            return q[i];
        });

        const setVideoState = (videoState: VideoState) => {
            if (options.onSetState?.(options.id, videoState)) {
                return;
            }

            console.info("VIDEO STATE CHANGED", videoState, { id: options.id });
            setState(videoState);
        };

        const openVideo = (v: IPlatformVideo, time?: Duration, videoState?: VideoState) => {
            options.beforeReplace?.();
            const desiredVideoState = videoState ?? VideoState.Maximized;
            batch(() => {
                setIndex(0);
                setStartTime(time);
                setQueue([ v ]);
                if (state() !== desiredVideoState)
                    setVideoState(desiredVideoState);
            });
        };

        const openVideoByUrl = async (url: string, time?: Duration, videoState?: VideoState) => {
            options.beforeReplace?.();
            const desiredVideoState = videoState ?? VideoState.Maximized;
            if (state() !== desiredVideoState)
                setVideoState(desiredVideoState);
            const videoLoadResult = await DetailsBackend.videoLoad(url);
            batch(() => {
                setIndex(0);
                setStartTime(time);
                setQueue([ videoLoadResult.video ]);
            });
        };

        const sq = (index: number, queue: IPlatformVideo[], repeat?: boolean, shuffle?: boolean, videoState?: VideoState) => {
            if (index < 0 || index >= queue.length) {
                console.error("index not valid for queue", { index, queue });
                return;
            }

            options.beforeReplace?.();
            const desiredVideoState = videoState ?? VideoState.Maximized;
            batch(() => {
                setIndex(index);
                setQueue(queue);
                setStartTime(undefined);
                if (repeat !== undefined)
                    setRepeat(repeat);
                if (shuffle !== undefined)
                    setShuffle(shuffle);
                if (state() !== desiredVideoState)
                    setVideoState(desiredVideoState);
            });
        };

        const addToQueue = (video: IPlatformVideo) => {
            if (index() === undefined) {
                openVideo(video);
                return;
            }

            setQueue([ ... (queue() ?? []), video ]);
        };

        const closeVideo = () => {
            batch(() => {
                console.log("Closing video", { id: options.id });
                setIndex(undefined);
                setQueue(undefined);
                setStartTime(undefined);
                setState(VideoState.Closed);
                if (activePlaybackVideoId() === options.id) {
                    setActivePlaybackVideoId(undefined);
                }
                options.onClose?.(options.id);
            });
        };

        const setDesiredMode = (mode: VideoMode) => {
            setDesiredModeInternal(mode);
            if (options.persistSettings) {
                SettingsBackend.persistSet("desiredMode", mode);
            }
        };

        const setTheatrePinned = (pinned: boolean) => {
            setTheatrePinnedInternal(pinned);
            if (options.persistSettings) {
                SettingsBackend.persistSet("theatrePinned", pinned);
            }
        };

        const setVolume = (volume: number) => {
            setVolumeInternal(volume);
            if (options.persistSettings) {
                SettingsBackend.persistSet("volume", volume);
            }
        };

        return {
            id: options.id,
            index,
            queue,
            watchLater,
            state,
            repeat,
            shuffle,
            video,
            startTime,
            desiredMode,
            theatrePinned,
            volume,
            minimizedVideos,
            activePlaybackVideoId,
            actions: {
                setIndex: (i: number) => {
                    batch(() => {
                        setIndex(i);
                        setStartTime(undefined);
                    });
                },
                openVideo,
                openVideoByUrl,
                setQueue: sq,
                closeVideo,
                addToQueue,
                setState: setVideoState,
                setRepeat,
                setShuffle,
                setDesiredMode,
                setTheatrePinned,
                setVolume,
                refetchWatchLater,
                setStartTime,
                requestPlayback: () => setActivePlaybackVideoId(options.id)
            }
        };
    };

    const promoteMinimizedVideo = (id: string, videoState: VideoState) => {
        const minimizedVideo = minimizedVideos().find(v => v.id === id);
        const q = minimizedVideo?.queue();
        const i = minimizedVideo?.index();
        if (!minimizedVideo || !q || i === undefined) {
            return false;
        }

        batch(() => {
            mainVideo.actions.setDesiredMode(minimizedVideo.desiredMode());
            mainVideo.actions.setTheatrePinned(minimizedVideo.theatrePinned());
            mainVideo.actions.setVolume(minimizedVideo.volume());
            mainVideo.actions.setQueue(i, q, minimizedVideo.repeat(), minimizedVideo.shuffle(), videoState);
            mainVideo.actions.setStartTime(minimizedVideo.startTime());
            removeMinimizedVideo(id);
        });
        return true;
    };

    const archiveMainVideoIfMinimized = () => {
        if (!mainVideo || mainVideo.state() !== VideoState.Minimized) {
            return;
        }

        const q = mainVideo.queue();
        const i = mainVideo.index();
        if (!q || i === undefined) {
            return;
        }

        const id = `minimized-${++minimizedVideoId}`;
        const minimizedVideo = createVideoContext({
            id,
            initialState: VideoState.Minimized,
            initialIndex: i,
            initialQueue: [ ... q ],
            initialStartTime: mainVideo.startTime(),
            initialRepeat: mainVideo.repeat(),
            initialShuffle: mainVideo.shuffle(),
            initialDesiredMode: mainVideo.desiredMode(),
            initialTheatrePinned: mainVideo.theatrePinned(),
            initialVolume: mainVideo.volume(),
            onClose: removeMinimizedVideo,
            onSetState: (id, videoState) => {
                if (videoState === VideoState.Maximized || videoState === VideoState.Fullscreen) {
                    return promoteMinimizedVideo(id, videoState);
                }
                return false;
            }
        });
        setMinimizedVideos(videos => [ ... videos, minimizedVideo ]);
    };

    mainVideo = createVideoContext({
        id: "main",
        beforeReplace: archiveMainVideoIfMinimized,
        persistSettings: true
    });

    onMount(async () => {
        await refetchWatchLater();
    });

    StateWebsocket.registerHandlerNew("WatchLaterChanged", (packet)=>{
        console.log("WatchLater changed");
        refetchWatchLater();
    }, "videoProvider");

    SettingsBackend.persistGet("desiredMode", VideoMode.Theatre).then((r: VideoMode) => mainVideo.actions.setDesiredMode(r)).catch(e => console.error("Failed to get persistent setting 'desiredMode'.", e));
    SettingsBackend.persistGet("theatrePinned", true).then((r: boolean) => mainVideo.actions.setTheatrePinned(r)).catch(e => console.error("Failed to get persistent setting 'theatrePinned'.", e));
    SettingsBackend.persistGet("volume", 1).then((r: number) => mainVideo.actions.setVolume(r)).catch(e => console.error("Failed to get persistent setting 'volume'.", e));

    return (
        <VideoContext.Provider value={mainVideo}>
            {props.children}
        </VideoContext.Provider>
    );
}

export function useVideo() { return useContext(VideoContext); }
