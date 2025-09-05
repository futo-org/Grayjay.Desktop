
import { Component, For, JSX, Match, Show, Switch, batch, createContext, createEffect, createMemo, createResource, createSignal, onCleanup, onMount, useContext } from 'solid-js';
import styles from './index.module.css';
import chevron_right from '../../../../assets/icons/icon_chevron_right.svg';
import warning from '../../assets/icons/icon_warning.svg';
import OverlayDialog, { DialogDescriptor, DialogDropdown, DialogInputText, IDialogOutput } from '../OverlayDialog';

import { Event0 } from '../../utility/Event'
import UIOverlay from '../../state/UIOverlay';
import OverlayDialogLoader, { LoaderDescriptor } from '../OverlayDialogLoader';
import { ToastDescriptor } from '../OverlayRoot';
import StateWebsocket from '../../state/StateWebsocket';
import StateGlobal from '../../state/StateGlobal';


export interface OverlayToastsProps {
};
const OverlayToasts: Component<OverlayToastsProps> = (props: OverlayToastsProps) => {
    //TODO: Improve animations
    return (
      <div style="position: absolute; top: 65px; right: 0px; text-align: right; pointer-events: none; z-index: 3">
        <For each={StateGlobal.toasts$()}>{(item, i) =>
          <div class={styles.toast} classList={{[styles.expired]: !!item.expired$(), [styles.showing]: !item.expired$()}}>
            <div class={styles.toastTitle}>
              {item.descriptor.title}
            </div>
            <div class={styles.toastText}>
              {item.descriptor.text}
            </div>
          </div>
        }</For>
      </div>
    );
  };
  
  export default OverlayToasts;