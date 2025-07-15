
import { Component, For, JSX, Match, Show, Switch, batch, createEffect, createMemo, createResource, createSignal, onCleanup, onMount } from 'solid-js';
import styles from './index.module.css';
import iconClose from '../../assets/icons/icon24_close.svg';

export interface OverlayCustomDialogProps {
  children: JSX.Element,
  hideHeader?: boolean,
  hideDialog?: boolean,
  onRootClick?: (ev: MouseEvent)=>void,
  onCloseClick?: (ev: MouseEvent)=>void
};
const OverlayCustomDialog: Component<OverlayCustomDialogProps> = (props: OverlayCustomDialogProps) => {


    return (
      <div>
        <Show when={!props.hideDialog}>
          <div class={styles.root} onClick={(ev)=>props.onRootClick && props.onRootClick(ev)}>
            <div class={styles.dialog}>
              <Show when={!props.hideHeader}>
                <div class={styles.dialogHeader}>
                  <div class={styles.closeButton} onClick={(ev)=>props.onCloseClick && props.onCloseClick(ev)}>
                    <img src={iconClose} />
                  </div>
                </div>
              </Show>
              {props.children}
            </div>
          </div>
        </Show>
        <Show when={props.hideDialog}>
          <div class={styles.root} onClick={(ev)=>props.onRootClick && props.onRootClick(ev)}>
              {props.children}
          </div>
        </Show>
      </div>
    );
  };
  
  export default OverlayCustomDialog;