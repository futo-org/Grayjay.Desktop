import { Component, createEffect, createMemo, createSignal, For, JSX, on, onCleanup, onMount, Show } from 'solid-js';
import styles from './index.module.css';
import ScrollContainer from '../containers/ScrollContainer';
import { toHumanNumber } from '../../utility';
import LiveChatState, { LiveDonationEvent, LiveEventType, LiveRaidEvent } from '../../state/StateLiveChat';
import LiveChatDonationPill from '../LiveChatDonationPill';
import { Portal } from 'solid-js/web';
import DonationOverlay from '../DonationOverlay';
import RaidOverlay from '../RaidOverlay';

interface LiveChatWindowProps {
    style?: JSX.CSSProperties;
    viewCount?: number;
    onExecuteRaid: (raid: LiveRaidEvent) => void;
}

const MAX_MESSAGES = 200;

const LiveChatWindow: Component<LiveChatWindowProps> = (props) => {
    const store = LiveChatState.store;
    const [overlayDonation, setOverlayDonation] = createSignal<LiveDonationEvent | null>(null);
    const [overlayRaid, setOverlayRaid] = createSignal<LiveRaidEvent | null>(null);
    const [autoScroll, setAutoScroll] = createSignal(true);

    let donationScrollerRef: HTMLDivElement | undefined;
    const [canScrollLeft, setCanScrollLeft] = createSignal(false);
    const [canScrollRight, setCanScrollRight] = createSignal(false);
    function updateDonationArrows() {
        if (!donationScrollerRef) return;
        const { scrollLeft, scrollWidth, clientWidth } = donationScrollerRef;
        setCanScrollLeft(scrollLeft > 0);
        setCanScrollRight(scrollLeft + clientWidth < scrollWidth - 1);
    }
    function scrollDonations(amount: number) {
        donationScrollerRef?.scrollBy({ left: amount, behavior: 'smooth' });
    }
    onMount(() => {
        updateDonationArrows();
    });
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

    const donationList = createMemo(() =>
        Object.values(store.donations).sort((a, b) => (b.receivedAt ?? 0) - (a.receivedAt ?? 0))
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

    createEffect(() => {
        setOverlayRaid(store.raid);
        console.log("raid", store.raid);
    });

    const handleRaidGo = () => {
        setOverlayRaid(null);
        var raid = store.raid;
        if (raid)
            props.onExecuteRaid(raid);        
    };
    const handleRaidPrevent = () => {
        setOverlayRaid(null);
    };

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
                <div class={styles.donationStripWrapper}>
                    <button
                        class={styles.chevron}
                        classList={{ [styles.left]: true, [styles.hidden]: !canScrollLeft() }}
                        onClick={() => scrollDonations(-200)}
                        aria-label="Scroll donations left"
                    >‹</button>
                    <div
                        class={styles.donationStrip}
                        ref={donationScrollerRef}
                        onScroll={() => updateDonationArrows()}
                    >
                        <For each={donationList()}>
                            {d => (
                                <LiveChatDonationPill
                                    donation={d}
                                    onShowOverlay={setOverlayDonation}
                                />
                            )}
                        </For>
                    </div>
                    <button
                        class={styles.chevron}
                        classList={{ [styles.right]: true, [styles.hidden]: !canScrollRight() }}
                        onClick={() => scrollDonations(+200)}
                        aria-label="Scroll donations right"
                    >›</button>
                </div>
            </Show>

            <div class={styles.containerBody}>
                <ScrollContainer ref={scrollContainerRef} scrollToBottomButton={true} scrollSmooth={false} onScroll={handleScroll}>
                    <div style="width: 100%; height: 8px"></div>
                    <For each={store.messages}>
                        {(event) => {
                            switch (event.type) {
                                case LiveEventType.COMMENT:
                                    return (
                                        <div class={styles.liveChatItem}>
                                            <Show when={event.thumbnail && event.thumbnail.length}>
                                                <img src={event.thumbnail} class={styles.liveChatAuthorImage} />
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
                <DonationOverlay donation={overlayDonation()} onDone={() => setOverlayDonation(null)} />
                <RaidOverlay raid={overlayRaid()} onGo={handleRaidGo} onPrevent={handleRaidPrevent} />
            </div>
        </div>
    );
};

export default LiveChatWindow;
