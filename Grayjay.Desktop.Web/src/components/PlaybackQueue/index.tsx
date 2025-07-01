import { Component, JSX, Show, createEffect, createMemo } from 'solid-js'

import styles from './index.module.css';
import IconButton from '../buttons/IconButton';

import iconLoopActive from '../../assets/icons/icon_loop_active.svg';
import iconShuffleActive from '../../assets/icons/icon_shuffle_active.svg';
import iconLoopInactive from '../../assets/icons/icon_loop_inactive.svg';
import iconShuffleInactive from '../../assets/icons/icon_shuffle_inactive.svg';
import iconClear from '../../assets/icons/close_FILL0_wght300_GRAD0_opsz24.svg';
import ScrollContainer from '../containers/ScrollContainer';
import { proxyImage, swap } from '../../utility';
import VirtualDragDropList from '../containers/VirtualDragDropList';
import iconDrag from '../../assets/icons/icon_drag.svg';
import iconPlay from '../../assets/icons/icon24_play.svg';
import { IPlatformVideo } from '../../backend/models/content/IPlatformVideo';

interface PlaybackQueueProps {
    style?: JSX.CSSProperties;
    scrollContainerStyle?: JSX.CSSProperties;
    title?: string;
    index: number;
    videos: IPlatformVideo[];
    repeat?: boolean;
    shuffle?: boolean;
    onVideoClick?: (video: IPlatformVideo) => void;
    onVideoRemoved?: (index: number) => void;
    onShuffleClick?: () => void;
    onRepeatClick?: () => void;
    onIndexMoved?: (oldIndex: number, newIndex: number) => void;
}

const PlaybackQueue: Component<PlaybackQueueProps> = (props) => {
    let scrollContainerRef: HTMLDivElement | undefined;

    const handleRemove = (ev: MouseEvent, index: number) => {
        props.onVideoRemoved?.(index);
        ev.preventDefault();
        ev.stopPropagation();
    };

    return (
        <div class={styles.containerPlaybackQueue} style={props.style}>
            <div style="display: flex; flex-direction: row; width: 100%;  margin-bottom: 24px;">
                <div style="display: flex; flex-direction: column; flex-grow: 1; margin-left: 8px;">
                    <div class={styles.title}>{props.title ?? "Queue"}</div>
                    <div class={styles.metadata}>{props.title ?? `Item ${props.index + 1} of ${props.videos.length}`}</div>
                </div>
                <div style="display: flex; flex-direction: row; flex-shrink: 0; gap: 12px; margin-right: 8px;">
                    <Show when={props.repeat !== undefined}>
                        <IconButton icon={props.repeat ? iconLoopActive : iconLoopInactive} style={{ padding: "6px" }}
                            onClick={() => props.onRepeatClick?.()} />
                    </Show>
                    <Show when={props.shuffle !== undefined}>
                        <IconButton icon={props.shuffle ? iconShuffleActive : iconShuffleInactive} style={{ padding: "6px" }}
                            onClick={() => props.onShuffleClick?.()} />
                    </Show>
                </div>
            </div>
            <ScrollContainer scrollToTopButton={false} ref={scrollContainerRef} wrapperStyle={{ "max-height": "700px", ... props.scrollContainerStyle }}>
                    <VirtualDragDropList items={props.videos}
                        itemHeight={88}
                        onSwap={(index1, index2) => {
                            if (props.index === index1 || props.index === index2)
                                props.onIndexMoved?.(index1, index2);
                            swap(props.videos, index1, index2);
                        }}
                        builder={(index, item, containerRef, startDrag) => {
                            const video = createMemo(() => item() as IPlatformVideo | undefined);
                            const bestThumbnail = createMemo(() => {
                                const v = video();
                                return (v && (v?.thumbnails?.sources?.length ?? 0) > 0) ? v!.thumbnails.sources[Math.max(0, v!.thumbnails.sources.length - 1)] : null;
                            });

                            return (
                                <div style={{
                                    height: "80px",
                                    "margin-bottom": "8px",
                                    "margin-right": "12px",
                                    "display": "flex",
                                    "flex-direction": "row",
                                    "width": "calc(100% - 12px)",
                                    "align-items": "center",
                                    "overflow": "hidden",
                                    "gap": "8px",
                                    "background-color": index() == props.index ? "#2E2E2E" : undefined,
                                    "border-radius": "8px"
                                }} onClick={() => {
                                    const v = video();
                                    if (!v) return;
                                    props.onVideoClick?.(v);
                                }}>
                                    <div style="display: flex; width: 44px; height: 44px; padding: 20px; cursor: pointer; justify-content: center; align-items: center;" class={styles.itemDrag} onMouseDown={(e) => {
                                        startDrag(e.pageY, containerRef!.getBoundingClientRect().top, e.target as HTMLElement);
                                        e.preventDefault();
                                        e.stopPropagation();
                                    }}>
                                        <Show when={index() === props.index} fallback={
                                            <>
                                                <div style="position: absolute;">{(index() ?? 0) + 1}</div>
                                                <img class={styles.iconDrag} src={iconDrag} style="position: absolute;" />
                                            </>
                                        }>
                                            <img src={iconPlay} style="position: absolute;" />
                                        </Show>
                                    </div>
                                    <img src={bestThumbnail()?.url} 
                                        style="border-radius: 3px; height: 56px; width: 100px; cursor: pointer;" />
                                    <div style="display: flex; flex-direction: column; flex-grow: 1; overflow: hidden; cursor: pointer;">
                                        <div class={styles.itemTitle}>{video()?.name}</div>
                                        <div class={styles.itemAuthor} style="margin-top: 6px">{video()?.author?.name}</div>
                                    </div>
                                    <img src={iconClear} style="padding: 13px; height: 24px; width: 24px; cursor: pointer;" onClick={(ev) => {
                                        const i = index();
                                        if (i !== undefined) {
                                            handleRemove(ev, i);
                                        }
                                    }} />
                                </div>
                            );
                        }}
                        outerContainerRef={scrollContainerRef} />
                </ScrollContainer>
        </div>
    );
};
/*
<div class={styles.itemNumber} onMouseDown={(e) => {
    startDrag(e.pageY, containerRef!.getBoundingClientRect().top, e.target as HTMLElement);
    e.preventDefault();
    e.stopPropagation();
}}>{index + 1}</div>
*/
export default PlaybackQueue;

