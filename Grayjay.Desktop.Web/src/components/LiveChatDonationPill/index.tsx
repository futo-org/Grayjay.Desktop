import { Component, onMount } from 'solid-js';
import styles from './index.module.css';
import type { LiveDonationEvent } from '../../state/StateLiveChat';
import { hexToRgba } from '../../utility';

interface LiveChatDonationPillProps {
    donation: LiveDonationEvent;
    onShowOverlay: (donation: LiveDonationEvent) => void;
}

const LiveChatDonationPill: Component<LiveChatDonationPillProps> = (props) => {
    let expireBarRef: HTMLDivElement | undefined;

    const bgColor = props.donation.colorDonation ?? '#2A2A2A';
    let textColor = '#FFFFFF';
    const rgba = hexToRgba(bgColor);
    if (rgba) {
        const { r, g, b } = rgba;
        if ((r + g + b) > 400 && (r > 140 || g > 140 || b > 140)) {
            textColor = '#000000';
        }
    }

    onMount(() => {
        if (expireBarRef && props.donation.expire) {
            expireBarRef.animate(
                [{ transform: 'scaleX(1)' }, { transform: 'scaleX(0)' }],
                {
                    duration: props.donation.expire + 500,
                    easing: 'linear',
                    fill: 'forwards',
                }
            );
        }
    });

    return (
        <div
            class={styles.root}
            style={{ 'background-color': bgColor, color: textColor }}
            onClick={() => props.onShowOverlay(props.donation)}
        >
            <div class={styles.content}>
                {props.donation.thumbnail && (
                    <img
                        src={props.donation.thumbnail}
                        class={styles.authorImage}
                        onError={(e) => { (e.currentTarget as HTMLImageElement).src = '/assets/img/default-thumbnail.png'; }}
                    />
                )}
                <span class={styles.amount}>{props.donation.amount}</span>
            </div>
            <div ref={expireBarRef} class={styles.expireBar} />
        </div>
    );
};

export default LiveChatDonationPill;
