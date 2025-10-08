import { Component, For, Match, Show, Switch, createEffect, createMemo, createSignal } from 'solid-js'

import styles from './index.module.css';
import { useFocus } from '../../FocusProvider';

interface ControllerOverlayProps {

}

const ControllerOverlay: Component<ControllerOverlayProps> = (props) => {
    const colorAction = "#efde1e";
    const colorPress = "#77f62b";
    const colorBack = "#ed412a";
    const colorOptions = "#1db3ee";
    const colorDirection = "#88b3c9";

    const focus = useFocus();

    const isVisible$ = createMemo(() => focus?.getFocusedNode() && (focus?.getFocusedNode()?.opts.onPress || focus?.getFocusedNode()?.opts.onOptions || focus?.getFocusedNode()?.opts.onBack || focus?.getFocusedNode()?.opts.onDirection));
    return (
        <Show when={isVisible$()}>
            <Show when={focus?.lastInputSource() === "keyboard"}>
                <div class={styles.container}>
                    <Show when={focus?.getFocusedNode()?.opts.onPress !== undefined}><div class={styles.button}><div class={styles.buttonImage} style={"background: " + colorPress}>Enter</div> {focus?.getFocusedNode()?.opts.onPressLabel ?? "Activate"}</div></Show>
                    <Show when={focus?.getFocusedNode()?.opts.onOptions !== undefined}><div class={styles.button}><div class={styles.buttonImage} style={"background: " + colorOptions}>O</div> {focus?.getFocusedNode()?.opts.onOptionsLabel ?? "Options"}</div></Show>
                    <Show when={focus?.getFocusedNode()?.opts.onAction !== undefined}><div class={styles.button}><div class={styles.buttonImage} style={"background: " + colorAction}>P</div> {focus?.getFocusedNode()?.opts.onActionLabel ?? "Action"}</div></Show>
                    <Show when={focus?.getFocusedNode()?.opts.onBack !== undefined}><div class={styles.button}><div class={styles.buttonImage} style={"background: " + colorBack}>Esc</div> {focus?.getFocusedNode()?.opts.onBackLabel ?? "Back"}</div></Show>
                    <Show when={focus?.getFocusedNode()?.opts.onDirection !== undefined}><div class={styles.button}><div class={styles.buttonImage} style={"background: " + colorDirection}>WASD</div> {focus?.getFocusedNode()?.opts.onDirectionLabel ?? "Direction"}</div></Show>
                </div>
            </Show>
            <Show when={focus?.lastInputSource() === "gamepad"}>
                <div class={styles.container}>
                    <Show when={focus?.getFocusedNode()?.opts.onPress !== undefined}><div class={styles.button}><div class={styles.buttonImage} style={"background: " + colorPress}>A</div> {focus?.getFocusedNode()?.opts.onPressLabel ?? "Activate"}</div></Show>
                    <Show when={focus?.getFocusedNode()?.opts.onOptions !== undefined}><div class={styles.button}><div class={styles.buttonImage} style={"background: " + colorOptions}>X</div> {focus?.getFocusedNode()?.opts.onOptionsLabel ?? "Options"}</div></Show>
                    <Show when={focus?.getFocusedNode()?.opts.onAction !== undefined}><div class={styles.button}><div class={styles.buttonImage} style={"background: " + colorAction}>Y</div> {focus?.getFocusedNode()?.opts.onActionLabel ?? "Action"}</div></Show>
                    <Show when={focus?.getFocusedNode()?.opts.onBack !== undefined}><div class={styles.button}><div class={styles.buttonImage} style={"background: " + colorBack}>B</div> {focus?.getFocusedNode()?.opts.onBackLabel ?? "Back"}</div></Show>
                    <Show when={focus?.getFocusedNode()?.opts.onDirection !== undefined}><div class={styles.button}><div class={styles.buttonImage} style={"background: " + colorDirection}>DPAD/Thumb</div> {focus?.getFocusedNode()?.opts.onDirectionLabel ?? "Direction"}</div></Show>
                </div>
            </Show>
        </Show>
    );
};

export default ControllerOverlay;
