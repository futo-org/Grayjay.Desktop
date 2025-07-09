import { Component, createSignal, onCleanup, onMount, Show, JSX, createEffect } from "solid-js";
import styles from "./index.module.css";

export interface LoaderGameHandle {
    startLoader(durationMs?: number | null): void;
    stopAndResetLoader(): void;
}

interface LoaderGameProps {
    src?: string;
    duration?: number;
    onReady?: (h: LoaderGameHandle) => void;
    class?: string;
    style?: JSX.CSSProperties;
}

export const LoaderGame: Component<LoaderGameProps> = (props) => {
    let frame!: HTMLIFrameElement;
    let raf = 0;

    const [progress, setProgress] = createSignal(0);
    const [running, setRunning] = createSignal(false);
    const [deterministic, setDeterministic] = createSignal(false);
    const [src, setSrc] = createSignal("about:blank");

    let startTime = 0;
    let expectedDur = 0;

    const startLoader = (durationMs: number) => {
        if (!running()) {
            setSrc(props.src ?? "https://releases.grayjay.app/loadergame.html");
        }
        cancelAnimationFrame(raf);

        if (durationMs && durationMs > 0) {
            expectedDur = durationMs;
            startTime = performance.now();
            setProgress(0);
            setDeterministic(true);
            setRunning(true);
            raf = requestAnimationFrame(updateBar);
        } else {
            setDeterministic(false);
            setRunning(true);
        }
    };
    const stopAndResetLoader = () => {
        cancelAnimationFrame(raf);
        setRunning(false);
        setDeterministic(false);
        setSrc("about:blank");
    };

    const updateBar = () => {
        const elapsed = performance.now() - startTime;
        setProgress(Math.min(1, elapsed / expectedDur));
        if (elapsed >= expectedDur) {
                setDeterministic(false);
        } else {
            raf = requestAnimationFrame(updateBar);
        }
    };

    createEffect(() => {
        if (props.duration !== undefined) {
            startLoader(props.duration);
        } else {
            stopAndResetLoader();
        }
    });

    const handle: LoaderGameHandle = {
        startLoader, 
        stopAndResetLoader
    };

    onMount(() => props.onReady?.(handle));
    onCleanup(() => cancelAnimationFrame(raf));

    return (
        <div class={`${styles.root} ${props.class ?? ""}`} style={props.style}>
            <Show when={running()}>
                <iframe ref={frame} src={src()} class={styles.iframe} />
            </Show>

            <Show when={running() && deterministic()}>
                <div class={styles.detBar} style={{ width: `${progress() * 100}%` }} />
            </Show>

            <Show when={running() && !deterministic()}>
                <div class={styles.spinBox}>
                    <svg viewBox="0 0 100 100" class={styles.spinnerRotator}>
                        <defs>
                            <filter id="spinnerGlow" x="-50%" y="-50%" width="200%" height="200%">
                                <feGaussianBlur stdDeviation="6" result="b" />
                                <feMerge>
                                    <feMergeNode in="b" />
                                    <feMergeNode in="SourceGraphic" />
                                </feMerge>
                            </filter>
                            <linearGradient id="lgSweep" x1="0%" y1="0%" x2="100%" y2="0%">
                                <stop offset="0%" stop-color="rgba(255,255,255,0)" />
                                <stop offset="50%" stop-color="#fff" />
                                <stop offset="100%" stop-color="rgba(255,255,255,0)" />
                            </linearGradient>
                        </defs>
                        <circle class={`${styles.glowArc} ${styles.spinnerArc}`} cx="50" cy="50" r="44" filter="url(#spinnerGlow)" />
                        <circle class={`${styles.coreArc} ${styles.spinnerArc}`} cx="50" cy="50" r="44" stroke="url(#lgSweep)" />
                    </svg>
                </div>
            </Show>
        </div>
    );
};
