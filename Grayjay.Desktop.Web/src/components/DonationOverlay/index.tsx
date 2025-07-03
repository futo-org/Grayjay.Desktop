import { Component, createEffect, createMemo, createSignal, onCleanup, onMount, Show } from 'solid-js';
import styles from './index.module.css';
import type { LiveDonationEvent } from '../../state/StateLiveChat';
import { CSSColor } from '../../CSSColor';
import LiveChatState from '../../state/StateLiveChat';

interface DonationOverlayProps {
    donation: LiveDonationEvent | null;
    onDone: () => void;
}

const DonationOverlay: Component<DonationOverlayProps> = (props) => {
    const store = LiveChatState.store;
    const bg = createMemo(() => props.donation?.colorDonation ?? '#2A2A2A');
    const fg = createMemo(() => {
        const cssColor = CSSColor.parseColor(bg());
        return (cssColor.lightness > 0.5) ? '#000' : '#fff';
    });

    const renderEmojis = (message: string, emojis: Record<string,string>) => {
        const parts = message.split(/(__.*?__)/g);
        return parts.map(part => {
            const m = part.match(/^__(.*?)__$/);
            return m && emojis[m[1]]
                ? <img src={emojis[m[1]]} alt={m[1]} style="height:20px;vertical-align:middle;margin:0 2px;" />
                : <span>{part}</span>;
        });
    };

    return (
        <Show when={props.donation}>
            <div class={styles.backdrop} style={{ '--bg-color': bg(), '--fg-color': fg() }} onClick={() => props.onDone()}>
                <div class={styles.overlay} style={{ 'background-color': bg(), color: fg() }}>
                    <div class={styles.header}>
                        <Show when={props.donation?.thumbnail && props.donation?.thumbnail.length}>
                            <img src={props.donation?.thumbnail} class={styles.authorImage} />
                        </Show>
                        <span class={styles.authorName}>{props.donation!.name}</span>
                    </div>
                    <div class={styles.message}>{renderEmojis(props.donation!.message, store.emojis)}</div>
                    <div class={styles.amount}>{props.donation!.amount.trim()}</div>
                </div>
            </div>
        </Show>
    );
};

export default DonationOverlay;
