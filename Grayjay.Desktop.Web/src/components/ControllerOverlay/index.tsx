import { Component, For, Match, Show, Switch, createEffect, createMemo, createSignal } from 'solid-js'

import styles from './index.module.css';
import { useFocus } from '../../FocusProvider';

interface ControllerOverlayProps {

}

const ControllerOverlay: Component<ControllerOverlayProps> = (props) => {
    //const colorOptions = "#FFC857";
    const colorPress = "#0ba30bff";
    const colorBack = "#c12f3bff";
    const colorOptions = "#118AB2";
    const colorDirection = "#e39a11ff";

    const focus = useFocus();
    return (
        <Show when={focus?.getFocusedNode()}>
            <Show when={focus?.lastInputSource() === "keyboard"}>
                <div class={styles.container}>
                    <Show when={focus?.getFocusedNode()?.opts.onPress !== undefined}><div class={styles.button}><div class={styles.buttonImage} style={"background: " + colorPress}>Enter</div> Activate</div></Show>
                    <Show when={focus?.getFocusedNode()?.opts.onOptions !== undefined}><div class={styles.button}><div class={styles.buttonImage} style={"background: " + colorOptions}>O</div> Options</div></Show>
                    <Show when={focus?.getFocusedNode()?.opts.onBack !== undefined}><div class={styles.button}><div class={styles.buttonImage} style={"background: " + colorBack}>Escape</div> Back</div></Show>
                    <Show when={focus?.getFocusedNode()?.opts.onDirection !== undefined}><div class={styles.button}><div class={styles.buttonImage} style={"background: " + colorDirection}>WASD/Arrows</div> Directionality</div></Show>
                </div>
            </Show>
            <Show when={focus?.lastInputSource() === "gamepad"}>
                <div class={styles.container}>
                    <Show when={focus?.getFocusedNode()?.opts.onPress !== undefined}><div class={styles.button}><div class={styles.buttonImage} style={"background: " + colorPress}>A</div> Activate</div></Show>
                    <Show when={focus?.getFocusedNode()?.opts.onOptions !== undefined}><div class={styles.button}><div class={styles.buttonImage} style={"background: " + colorOptions}>X</div> Options</div></Show>
                    <Show when={focus?.getFocusedNode()?.opts.onBack !== undefined}><div class={styles.button}><div class={styles.buttonImage} style={"background: " + colorBack}>B</div> Back</div></Show>
                    <Show when={focus?.getFocusedNode()?.opts.onDirection !== undefined}><div class={styles.button}><div class={styles.buttonImage} style={"background: " + colorDirection}>DPAD/Thumb</div> Directionality</div></Show>
                </div>
            </Show>
        </Show>
    );
};

export default ControllerOverlay;