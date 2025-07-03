import { Component, createMemo, onMount } from 'solid-js';
import styles from './index.module.css';
import type { LiveDonationEvent } from '../../state/StateLiveChat';
import { CSSColor } from '../../CSSColor';

interface LiveChatDonationPillProps {
    donation: LiveDonationEvent;
    onShowOverlay: (donation: LiveDonationEvent) => void;
}

const LiveChatDonationPill: Component<LiveChatDonationPillProps> = (props) => {
    let expireBarRef: HTMLDivElement | undefined;

    const bg = createMemo(() => props.donation?.colorDonation ?? '#2A2A2A');
    const fg = createMemo(() => {
        const cssColor = CSSColor.parseColor(bg());
        return (cssColor.lightness >= 0.5) ? '#000' : '#fff';
    });

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
        <div class={styles.root} style={{ 'background-color': bg(), color: fg() }} onClick={() => props.onShowOverlay(props.donation)}>
            <div class={styles.content}>
                {props.donation.thumbnail && props.donation.thumbnail?.length && (
                    <img src={props.donation.thumbnail} class={styles.authorImage} />
                )}
                <span class={styles.amount}>{props.donation.amount}</span>
            </div>
            <div ref={expireBarRef} class={styles.expireBar} style={{"background-color": fg()}} />
        </div>
    );
};

export default LiveChatDonationPill;
