import { Show, createSignal } from 'solid-js';
import UIOverlay from '../../../state/UIOverlay';
import Button from '../../buttons/Button';
import styles from '../../../overlays/OverlayDialog/index.module.css';
import { focusScope } from '../../../focusScope'; void focusScope;
import { capabilities, call, listenProgress, type Progress } from './bridge';

export class PlaybackSetupCancelled extends Error {
    constructor() { super('Protected playback setup was cancelled.'); this.name = 'PlaybackSetupCancelled'; }
}

export async function preparePlayback(signal: AbortSignal): Promise<boolean> {
    const caps = await capabilities();
    signal.throwIfAborted();
    if (!caps.enabled || caps.ready) return caps.enabled;
    const token = crypto.randomUUID();
    const [progress, setProgress] = createSignal<Progress>();
    const [downloading, setDownloading] = createSignal(false);
    let settled = false;
    let reject!: (error: unknown) => void;
    let resolve!: (value: boolean) => void;
    const result = new Promise<boolean>((yes, no) => { resolve = yes; reject = no; });
    const unsubscribe = listenProgress(token, setProgress);
    const stop = (error: Error) => {
        if (settled) return;
        settled = true;
        void call('cancelSetup', token).catch(() => {});
        reject(error);
        UIOverlay.dismiss(token);
    };
    const cancel = () => stop(new PlaybackSetupCancelled());
    const abort = () => stop(new DOMException('Playback setup cancelled', 'AbortError'));
    signal.addEventListener('abort', abort, { once: true });
    UIOverlay.onDismiss.register(id => { if (id === token) cancel(); }, token);
    const download = async () => {
        if (downloading() || settled) return;
        try {
            setDownloading(true);
            await call('prepare', token);
            if (settled) return;
            settled = true; resolve(true); UIOverlay.dismiss(token);
        } catch (error) {
            if (settled) return;
            settled = true; reject(error); UIOverlay.dismiss(token);
        }
    };
    UIOverlay.overlay({
        id: token,
        onShown: () => { if (settled) UIOverlay.dismiss(token); },
        dismissOnBackdrop: false,
        custom: () => <div class={styles.dialog} role="dialog" aria-modal="true" aria-label="Set up protected playback" use:focusScope={{ initialMode: 'trap' }}
            onClick={e => e.stopPropagation()} onMouseDown={e => e.stopPropagation()}>
            <div class={styles.title}>Set up protected playback</div>
            <div class={styles.description} aria-live="polite">
                {downloading() ? progress()?.message ?? 'Preparing download…' : 'Download Widevine from Google to play this content? It will be saved for future playback.'}
            </div>
            <Show when={downloading()}>
                <Show when={progress()?.total} fallback={<progress style={{ width: '100%', 'margin-top': '20px' }} />}>
                    <progress style={{ width: '100%', 'margin-top': '20px' }} max={progress()?.total ?? 1} value={progress()?.received ?? 0} />
                </Show>
                <Show when={progress()?.total}>
                    <div class={styles.description}>{((progress()?.received ?? 0) / 1048576).toFixed(1)} / {((progress()?.total ?? 0) / 1048576).toFixed(1)} MB</div>
                </Show>
            </Show>
            <div class={styles.buttons}>
                <Button text="Cancel" onClick={cancel} focusableOpts={{ onBack: () => { cancel(); return true; } }} />
                <Show when={!downloading()}><Button text="Download" color="#019BE7" autofocus onClick={() => void download()}
                    focusableOpts={{ onBack: () => { cancel(); return true; } }} /></Show>
            </div>
        </div>
    });
    try { return await result; }
    finally { unsubscribe(); signal.removeEventListener('abort', abort); UIOverlay.onDismiss.unregister(token); }
}
