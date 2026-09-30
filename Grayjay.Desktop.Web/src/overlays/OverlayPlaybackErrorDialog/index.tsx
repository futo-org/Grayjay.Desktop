import { Component, createSignal, onMount, onCleanup, Show } from 'solid-js';
import styles from './index.module.css';
import UIOverlay from '../../state/UIOverlay';
import Button from '../../components/buttons/Button';
import iconError from '../../assets/icons/icon_error.svg';
import iconClose from '../../assets/icons/icon24_close.svg';
import { focusScope } from '../../focusScope'; void focusScope;
import { focusable } from '../../focusable'; void focusable;

export interface OverlayPlaybackErrorDialogProps {
  initialSeconds: number;
  hasPlaylistNext: boolean;
  errorMessage?: string;
  onReload: () => void;
  onNext?: () => void;
  onCancel?: () => void;
}

const OverlayPlaybackErrorDialog: Component<OverlayPlaybackErrorDialogProps> = (props) => {
  const [secondsLeft$, setSecondsLeft] = createSignal(props.initialSeconds);
  let timer: any = null;

  const dismissAnd = (action?: () => void) => {
    if (timer) {
      clearInterval(timer);
      timer = null;
    }
    UIOverlay.dismiss();
    action?.();
  };

  onMount(() => {
    timer = setInterval(() => {
      setSecondsLeft((prev) => {
        if (prev <= 1) {
          dismissAnd(props.onReload);
          return 0;
        }
        return prev - 1;
      });
    }, 1000);
  });

  onCleanup(() => {
    if (timer) {
      clearInterval(timer);
      timer = null;
    }
  });

  return (
    <div
      class={styles.dialog}
      role="dialog"
      aria-modal="true"
      onClick={(ev) => ev.stopPropagation()}
      onMouseDown={(ev) => ev.stopPropagation()}
      use:focusScope={{ initialMode: 'trap' }}
    >
      <img src={iconError} class={styles.icon} alt="" />

      <img
        src={iconClose}
        class={styles.iconClose}
        alt="Close"
        role="button"
        tabindex={0}
        onClick={() => dismissAnd(props.onCancel)}
        use:focusable={{
          onPress: () => dismissAnd(props.onCancel),
          onBack: () => { dismissAnd(props.onCancel); return true; },
        }}
      />

      <div class={styles.title}>
        Playback Error
      </div>

      <div class={styles.description}>
        An error occurred while playing the video.
        <div style={{ "margin-top": "8px" }}>
          Auto-reloading in <span class={styles.countdownHighlight}>{secondsLeft$()}</span>s...
        </div>
        <Show when={props.errorMessage}>
          <div style={{ "margin-top": "8px", "font-size": "13px", "color": "#777", "word-break": "break-word" }}>
            {props.errorMessage}
          </div>
        </Show>
      </div>

      <div class={styles.buttons}>
        <Button
          text="Cancel"
          color="#2E2E2E"
          style={{ flex: "1 0 0", display: "flex", "align-items": "center", "justify-content": "center" }}
          onClick={(e) => {
            e.preventDefault();
            e.stopPropagation();
            dismissAnd(props.onCancel);
          }}
          focusableOpts={{
            onPress: () => dismissAnd(props.onCancel),
            onBack: () => { dismissAnd(props.onCancel); return true; }
          }}
        />

        <Show when={props.hasPlaylistNext}>
          <Button
            text="Next Video"
            color="#2E2E2E"
            style={{ flex: "1 0 0", display: "flex", "align-items": "center", "justify-content": "center" }}
            onClick={(e) => {
              e.preventDefault();
              e.stopPropagation();
              dismissAnd(props.onNext);
            }}
            focusableOpts={{
              onPress: () => dismissAnd(props.onNext),
              onBack: () => { dismissAnd(props.onCancel); return true; }
            }}
          />
        </Show>

        <Button
          text="Reload Now"
          color="#019BE7"
          autofocus={true}
          style={{ flex: "1 0 0", display: "flex", "align-items": "center", "justify-content": "center" }}
          onClick={(e) => {
            e.preventDefault();
            e.stopPropagation();
            dismissAnd(props.onReload);
          }}
          focusableOpts={{
            onPress: () => dismissAnd(props.onReload),
            onBack: () => { dismissAnd(props.onCancel); return true; }
          }}
        />
      </div>
    </div>
  );
};

export default OverlayPlaybackErrorDialog;
