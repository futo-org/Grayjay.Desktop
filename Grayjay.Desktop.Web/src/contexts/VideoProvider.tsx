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
    //queueType watch later, playlist en queue of undefined
    actions: {
        openVideo: (video: IPlatformVideo, time?: Duration, videoState?: VideoState) => void;
        openVideoByUrl: (url: string, time?: Duration, videoState?: VideoState) => void;
        setQueue: (index: number, queue: IPlatformVideo[], repeat?: boolean, shuffle?: boolean) => void;
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
    }
};

const VideoContext = createContext<VideoContextValue>();
export interface VideoContextProps {
    children: JSX.Element;
};

export const VideoProvider: ParentComponent<VideoContextProps> = (props) => {
    const [queue, setQueue] = createSignal<IPlatformVideo[] | undefined>();
    const [index, setIndex] = createSignal<number | undefined>();
    const [startTime, setStartTime] = createSignal<Duration | undefined>();
    const [state, setState] = createSignal<VideoState>(VideoState.Closed);
    const [repeat, setRepeat] = createSignal<boolean>(false);
    const [shuffle, setShuffle] = createSignal<boolean>(false);
    const [desiredMode, setDesiredModeInternal] = createSignal<VideoMode>(VideoMode.Theatre);
    const [theatrePinned, setTheatrePinnedInternal] = createSignal<boolean>(true);
    const [volume, setVolumeInternal] = createSignal<number>(1);
    const video = createMemo(() => {
        const q = queue();
        const i = index();
        if (!q || i === undefined || i < 0 || i >= q.length) {
            return undefined;
        }

        return q[i];
    })

    const openVideo = (v: IPlatformVideo, time?: Duration, videoState?: VideoState) => { 
        const desiredVideoState = videoState ?? VideoState.Maximized;
        batch(() => {
            setIndex(0);
            setStartTime(time);
            setQueue([ v ]);
            if (state() !== desiredVideoState)
                setState(desiredVideoState);
        });
    };
    const openVideoByUrl = async (url: string, time?: Duration, videoState?: VideoState) => { 
        const desiredVideoState = videoState ?? VideoState.Maximized;
        const videoLoadResult = await DetailsBackend.videoLoad(url);
        batch(() => {
            setIndex(0);
            setStartTime(time);
            setQueue([ videoLoadResult.video ]);
            if (state() !== desiredVideoState)
                setState(desiredVideoState);
        });
    };
    const sq = (index: number, queue: IPlatformVideo[], repeat?: boolean, shuffle?: boolean) => { 
        if (index < 0 || index >= queue.length) {
            console.error("index not valid for queue", {index, queue});
            return;
        }

        batch(() => {
            setIndex(index);
            setQueue(queue);
            setStartTime(undefined);
            if (repeat !== undefined)
                setRepeat(repeat);
            if (shuffle !== undefined)
                setShuffle(shuffle);
            if (state() === VideoState.Closed)
                setState(VideoState.Maximized);
        });
    };
    const addToQueue = (video: IPlatformVideo) => { 
        if (index() === undefined) {
            setIndex(0);
            setQueue([ video ]);
            setState(VideoState.Minimized);
            return;
        }

        setQueue([ ... (queue() ?? []), video ]);
    };
    const closeVideo = () => {
        batch(()=>{
            console.log("Closing video");
            setIndex(undefined);
            setQueue(undefined);
            setStartTime(undefined);
            setState(VideoState.Closed);
        });
    };

    const refetchWatchLater = async () => {
        const videos = await WatchLaterBackend.getAll();
        setWatchLater(videos);
        console.log("set watch later", videos);
    }
    const [watchLater, setWatchLater] = createSignal<IOrderedPlatformVideo[]>();
    onMount(async () => {
        await refetchWatchLater();
    });

    const setDesiredMode = (mode: VideoMode) => {
        setDesiredModeInternal(mode);
        SettingsBackend.persistSet("desiredMode", mode);
    };

    const setTheatrePinned = (pinned: boolean) => {
        setTheatrePinnedInternal(pinned);
        SettingsBackend.persistSet("theatrePinned", pinned);
    };

    const setVolume = (volume: number) => {
        setVolumeInternal(volume);
        SettingsBackend.persistSet("volume", volume);
    };

    StateWebsocket.registerHandlerNew("WatchLaterChanged", (packet)=>{
        console.log("WatchLater changed");
        refetchWatchLater();
    }, "videoProvider");
    
    const value: VideoContextValue = {
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
            setState: (videoState: VideoState) => {
                console.info("VIDEO STATE CHANGED", videoState);
                setState(videoState);
            },
            setRepeat,
            setShuffle,
            setDesiredMode,
            setTheatrePinned,
            setVolume,
            refetchWatchLater,
            setStartTime
        }
    };

    SettingsBackend.persistGet("desiredMode", VideoMode.Theatre).then((r: VideoMode) => setDesiredModeInternal(r)).catch(e => console.error("Failed to get persistent setting 'desiredMode'.", e));
    SettingsBackend.persistGet("theatrePinned", true).then((r: boolean) => setTheatrePinnedInternal(r)).catch(e => console.error("Failed to get persistent setting 'theatrePinned'.", e));
    SettingsBackend.persistGet("volume", 1).then((r: number) => setVolumeInternal(r)).catch(e => console.error("Failed to get persistent setting 'volume'.", e));

    return (
        <VideoContext.Provider value={value}>
            {props.children}
        </VideoContext.Provider>
    );
}

export function useVideo() { return useContext(VideoContext); }