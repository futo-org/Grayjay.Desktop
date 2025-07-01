import { Component, createEffect, createMemo, createSignal, For, JSX, on, onCleanup, onMount, Show } from 'solid-js';
import styles from './index.module.css';
import ScrollContainer from '../containers/ScrollContainer';
import StateWebsocket from '../../state/StateWebsocket';
import { createStore } from 'solid-js/store';
import { toHumanNumber } from '../../utility';
import LiveChatState from '../../state/StateLiveChat';

interface LiveChatWindowProps {
    style?: JSX.CSSProperties;
    viewCount?: number;
}

const MAX_MESSAGES = 200;

enum LiveEventType {
    UNKNOWN = 0,
    COMMENT = 1,
    EMOJIS = 4,
    DONATION = 5,
    VIEWCOUNT = 10,
    RAID = 100
}

interface BaseLiveEvent {
    type: LiveEventType;
}

interface LiveCommentEvent extends BaseLiveEvent {
    type: 1;
    name: string;
    thumbnail?: string;
    message: string;
    colorName?: string;
    badges?: string[];
}

interface LiveDonationEvent extends BaseLiveEvent {
    type: 5;
    name: string;
    thumbnail?: string;
    message: string;
    amount: string;
    colorDonation?: string;
    expire?: number;
    receivedAt?: number;
}

interface LiveEmojisEvent extends BaseLiveEvent {
    type: 4;
    emojis: Record<string, string>;
}

interface LiveRaidEvent extends BaseLiveEvent {
    type: 100;
    targetName: string;
    targetUrl: string;
    targetThumbnail: string;
}

interface LiveViewCountEvent extends BaseLiveEvent {
    type: 10;
    viewCount: number;
}

type LiveChatEvent =
    | LiveCommentEvent
    | LiveDonationEvent
    | LiveEmojisEvent
    | LiveRaidEvent
    | LiveViewCountEvent;

const LiveChatWindow: Component<LiveChatWindowProps> = (props) => {
    const store = LiveChatState.store;
    const [autoScroll, setAutoScroll] = createSignal(true);

    function isScrolledToBottom() {
        if (!scrollContainerRef) return false;
        const { scrollTop, scrollHeight, clientHeight } = scrollContainerRef;
        return scrollTop + clientHeight >= scrollHeight - 10;
    }

    function scrollToBottom() {
        if (scrollContainerRef) {
            scrollContainerRef.scrollTop = scrollContainerRef.scrollHeight;
        }
    }

    function onScroll() {
        setAutoScroll(isScrolledToBottom());
    }

    const donationList = createMemo(() =>
        Object.values(store.donations)
        .sort((a, b) => (b.receivedAt ?? 0) - (a.receivedAt ?? 0))
    );

    const renderBadges = (name: string, badges: string[] = [], emojis: Record<string,string>) =>
        <>
            <span>{name}</span>
            {badges.filter(b => emojis[b]).map(b =>
                <img src={emojis[b]} alt={b} style="height:16px; vertical-align:middle; margin-left:4px;" />
            )}
        </>;

    const renderEmojis = (message: string, emojis: Record<string,string>) => {
        const parts = message.split(/(__.*?__)/g);
        return parts.map(part => {
        const m = part.match(/^__(.*?)__$/);
        return m && emojis[m[1]]
            ? <img src={emojis[m[1]]} alt={m[1]} style="height:20px;vertical-align:middle;margin:0 2px;" />
            : <span>{part}</span>;
        });
    };

    const handleScroll = (e: Event) => {
        setAutoScroll(isScrolledToBottom());
    };

    createEffect(on(() => store.messages, (msgs) => {
        queueMicrotask(() => {
            if (autoScroll()) scrollToBottom();
        });
    }));

    let scrollContainerRef: HTMLDivElement | undefined;
    return (
        <div class={styles.container} style={props.style}>
            <div class={styles.containerHeader}>
                Chat
                <span style="margin-left:auto; padding-right:14px; font-size:13px; color:rgba(255,255,255,0.5);">
                    {toHumanNumber((store.viewerCount == 0 ? (props.viewCount ?? 0) : store.viewerCount))} viewers
                </span>
            </div>
            <Show when={donationList().length > 0}>
                <div style="display:flex; flex-wrap:wrap; gap:8px; padding:8px 16px;">
                    <For each={donationList()}>
                        {(d) => (
                            <div
                                style={{
                                    'background-color': d.colorDonation ?? '#333',
                                    color: '#fff',
                                    padding: '6px 10px',
                                    'border-radius': '6px',
                                    cursor: 'pointer',
                                    'font-size': '13px'
                                }}
                                onClick={() => alert(`Donation from ${d.name}: ${d.message}`)}
                            >
                                💸 {d.amount} — {d.name}
                            </div>
                        )}
                    </For>
                </div>
            </Show>

            {store.raid && (
                <div style="background:#552266; color:white; padding:8px 16px; display:flex; align-items:center;">
                    <img src={store.raid!.targetThumbnail} style="width:32px; height:32px; border-radius:4px; margin-right:12px;" />
                    <div>
                        <strong>🚀 Raid </strong> {store.raid!.targetName}
                    </div>
                    <button style="margin-left:auto;" onclick={() => /*TODO setRaid(null) */ {}}>Dismiss</button>
                </div>
            )}

            <div class={styles.containerBody}>
                <ScrollContainer ref={scrollContainerRef} scrollToBottomButton={true} scrollSmooth={false} onScroll={handleScroll}>
                    <div style="width: 100%; height: 8px"></div>
                    <For each={store.messages}>
                        {(event) => {
                            switch (event.type) {
                                case LiveEventType.COMMENT:
                                    return (
                                        <div class={styles.liveChatItem}>
                                            <Show when={event.thumbnail && event.thumbnail.length} fallback={<div class={styles.liveChatAuthorImage}></div>}>
                                                <img
                                                    src={event.thumbnail || '/assets/img/default-thumbnail.png'}
                                                    onError={(e) => (e.currentTarget.src = '/assets/img/default-thumbnail.png')}
                                                    class={styles.liveChatAuthorImage}
                                                />
                                            </Show>


                                            <div class={styles.liveChatContent}>
                                                <span class={styles.liveChatAuthorName} style={{ color: event.colorName || '#ffffff' }}>
                                                    {renderBadges(event.name.trim(), event.badges || [], store.emojis)}
                                                </span>
                                                <span class={styles.liveChatMessage}>{renderEmojis(event.message.trim(), store.emojis)}</span>
                                            </div>
                                        </div>
                                    );
                                default:
                                    return null;
                            }
                        }}
                    </For>
                    <div style="width: 100%; height: 8px"></div>
                </ScrollContainer>
            </div>
        </div>
    );
};

export default LiveChatWindow;
