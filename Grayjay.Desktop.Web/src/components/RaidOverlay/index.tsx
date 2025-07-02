import { Component, createEffect, Show } from 'solid-js';
import styles from './index.module.css';

export interface RaidEvent {
    targetName: string;
    targetThumbnail: string;
}

interface RaidOverlayProps {
    raid: RaidEvent | null;
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
                        <button class={styles.goButton} onClick={props.onGo}>
                            Go Now
                        </button>
                        <button class={styles.preventButton} onClick={props.onPrevent}>
                            Prevent
                        </button>
                    </div>
                </div>
            </div>
        </Show>
    );
};

export default RaidOverlay;
