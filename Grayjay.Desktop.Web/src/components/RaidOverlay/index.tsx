import { Component, createEffect, Show } from 'solid-js';
import styles from './index.module.css';
import { LiveRaidEvent } from '../../state/StateLiveChat';

interface RaidOverlayProps {
    raid: LiveRaidEvent | null;
    onGo: () => void;
    onPrevent: () => void;
}

const RaidOverlay: Component<RaidOverlayProps> = (props) => {
    createEffect(() => {
        console.log("raid", props.raid);
    });

    return (
        <Show when={props.raid}>
            <div class={styles.backdrop} onClick={props.onPrevent}>
                <div class={styles.overlay} onClick={e => e.stopPropagation()}>
                    <div class={styles.raidHeader}>
                        <span class={styles.raidMessage}>Viewers are raiding</span>
                        <div class={styles.raidTarget}>
                            <Show when={props.raid?.targetThumbnail && props.raid?.targetThumbnail?.length}>
                                <img
                                    src={props.raid?.targetThumbnail}
                                    alt={props.raid?.targetName}
                                    class={styles.raidImage}
                                />
                            </Show>
                            <span class={styles.raidName}>{props.raid?.targetName}</span>
                        </div>
                    </div>

                    <div class={styles.raidButtons}>
                        <Show when={props.raid?.isOutgoing === true}>
                            <button class={styles.goButton} onClick={props.onGo}>
                                Go Now
                            </button>
                        </Show>
                        <button class={styles.preventButton} onClick={props.onPrevent}>
                            Dismiss
                        </button>
                    </div>
                </div>
            </div>
        </Show>
    );
};

export default RaidOverlay;
