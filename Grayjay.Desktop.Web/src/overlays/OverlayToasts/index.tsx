
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


export interface OverlayToastsProps {
};
const OverlayToasts: Component<OverlayToastsProps> = (props: OverlayToastsProps) => {
    const [toasts$, setToasts] =  createSignal<ToastDescriptor[]>([]);

    const toastStates = {

    } as any;

    StateWebsocket.registerHandlerNew("Toast", (packet)=>{
      try {
        const toast = packet.payload as ToastDescriptor;
        if(toast) {
          toast.id = (Math.random() + 1).toString(36).substring(7);

          const [expired$, setExpired] = createSignal(false);

          toastStates[toast.id] = {
            expired$: expired$
          }
          setToasts([toast].concat(toasts$()));
          //setToasts(toasts$().concat([toast]));
          setTimeout(()=>{
            setExpired(true);
            setTimeout(()=>{
              setToasts(toasts$().filter((x)=>x != toast));
            }, 600)
          }, (toast.long ? 3000 : 1500))
        }
      }
      catch{}
    }, "toasts");
    

    //TODO: Improve animations
    return (
      <div style="position: absolute; top: 65px; right: 0px; text-align: right; pointer-events: none;">
        <For each={toasts$()}>{(item, i) =>
          <div class={styles.toast} classList={{[styles.expired]: !!toastStates[item.id ?? ""]?.expired$(), [styles.showing]: !toastStates[item.id ?? ""]?.expired$()}}>
            <div class={styles.toastTitle}>
              {item.title}
            </div>
            <div class={styles.toastText}>
              {item.text}
            </div>
          </div>
        }</For>
      </div>
    );
  };
  
  export default OverlayToasts;