
import { Component, For, JSX, Match, Show, Switch, batch, createEffect, createMemo, createResource, createSignal, onCleanup, onMount } from 'solid-js';
import styles from './index.module.css';
import { focusScope } from '../../focusScope'; void focusScope;
import { focusable } from "../../focusable"; void focusable;
import iconClose from '../../assets/icons/icon24_close.svg';

export interface OverlayCustomDialogProps {
  children: JSX.Element,
  hideHeader?: boolean,
  hideDialog?: boolean,
  onRootClick?: () => void,
  onCloseClick?: () => void
  focusScope?: boolean
};
const OverlayCustomDialog: Component<OverlayCustomDialogProps> = (props: OverlayCustomDialogProps) => {
    return (
      <div use:focusScope={props.focusScope ? {
          initialMode: 'trap'
      } : undefined}>
        <Show when={!props.hideDialog}>
          <div class={styles.root} onClick={(ev)=>props.onRootClick && props.onRootClick()}>
            <div class={styles.dialog}>
              <Show when={!props.hideHeader}>
                <div class={styles.dialogHeader}>
                  <div class={styles.closeButton} onClick={(ev)=>props.onCloseClick && props.onCloseClick()} use:focusable={{
                    onPress: () => props.onCloseClick && props.onCloseClick()
                  }}>
                    <img src={iconClose} />
                  </div>
                </div>
              </Show>
              {props.children}
            </div>
          </div>
        </Show>
        <Show when={props.hideDialog}>
          <div class={styles.root} onClick={(ev)=>props.onRootClick && props.onRootClick()}>
              {props.children}
          </div>
        </Show>
      </div>
    );
  };
  
  export default OverlayCustomDialog;