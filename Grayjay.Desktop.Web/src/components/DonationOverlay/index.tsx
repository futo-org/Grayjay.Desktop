import { Component, createEffect, createSignal, onCleanup, onMount, Show } from 'solid-js';
import styles from './index.module.css';
import type { LiveDonationEvent } from '../../state/StateLiveChat';
import { hexToRgba } from '../../utility';

interface DonationOverlayProps {
    donation: LiveDonationEvent | null;
    onDone: () => void;
}

const DonationOverlay: Component<DonationOverlayProps> = (props) => {
    const bg = () => props.donation?.colorDonation ?? '#2A2A2A';
    const fg = () => {
        const rgba = props.donation && hexToRgba(props.donation.colorDonation || '');
        if (rgba) {
            const { r, g, b } = rgba;
            if ((r + g + b) > 400 && (r > 140 || g > 140 || b > 140)) return '#000';
        }
        return '#fff';
    };
    
    return (
        <Show when={props.donation}>
            <div class={styles.backdrop} style={{ '--bg-color': bg(), '--fg-color': fg() }} onClick={() => props.onDone()}>
                <div
                    class={styles.overlay}
                    style={{ 'background-color': bg(), color: fg() }}
                >
                    <div class={styles.header}>
                        <Show when={props.donation?.thumbnail && props.donation?.thumbnail.length}>
                            <img src={props.donation?.thumbnail} class={styles.authorImage} />
                        </Show>
                        <span class={styles.authorName}>{props.donation!.name}</span>
                    </div>
                    <div class={styles.message}>{props.donation!.message}</div>
                    <div class={styles.amount}>{props.donation!.amount.trim()}</div>
                </div>
            </div>
        </Show>
    );
};

export default DonationOverlay;
