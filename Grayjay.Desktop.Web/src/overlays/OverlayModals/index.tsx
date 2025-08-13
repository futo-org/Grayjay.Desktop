
import { Component, For, JSX, Match, Show, Switch, batch, createContext, createEffect, createMemo, createResource, createSignal, onCleanup, onMount, useContext } from 'solid-js';
import styles from './index.module.css';
import chevron_right from '../../../../assets/icons/icon_chevron_right.svg';
import warning from '../../assets/icons/icon_warning.svg';
import OverlayDialog, { DialogDescriptor, DialogDropdown, DialogInputText, IDialogOutput } from '../OverlayDialog';

import { Event0 } from '../../utility/Event'
import UIOverlay from '../../state/UIOverlay';
import OverlayDialogLoader, { LoaderDescriptor } from '../OverlayDialogLoader';
import { ToastDescriptor } from '../OverlayRoot';


export interface OverlayContext {
}

export interface OverlayRequest {
  dialog?: DialogDescriptor,
  loader?: LoaderDescriptor,
  custom?: JSX.Element,
  output?: IDialogOutput,
  toast?: ToastDescriptor,
  id?: string,

  onShown?: ()=>void,
  onGlobalDismiss?: (id: string)=>void
}

export interface OverlayModalsProps {
};
const OverlayModals: Component<OverlayModalsProps> = (props: OverlayModalsProps) => {
  
    const [dialog$, setDialog] = createSignal<DialogDescriptor | undefined>(undefined);
    const [loader$, setLoader] = createSignal<LoaderDescriptor | undefined>(undefined);
    const [custom$, setCustom] = createSignal<JSX.Element | undefined>(undefined);
    const isActive$ = createMemo(()=>dialog$() || custom$() || loader$());

    let overlayCurrent: OverlayRequest | undefined = undefined;
    const overlayQueue: OverlayRequest[] = [];
    const globalDismissEvent = new Event0();


    UIOverlay.onOverlay.registerOne("overlay", (req)=>{
      if(!req.toast) {
        if(!req.id)
          req.id = (Math.random() + 1).toString(36).substring(7);
        overlayQueue.push(req);
      }
      if(!isActive$()) {
        loadNextRequest();
      }
    });
    UIOverlay.onDismiss.registerOne("overlay", (id: string)=>{
      if(overlayCurrent == undefined && overlayQueue.length == 0)
        return;
      if(overlayCurrent?.id != id)
        return;
      loadNextRequest();
    });

    function loadNextRequest() {
      const nextRequest = overlayQueue.shift();
      if(nextRequest) {
        hide();
        overlayCurrent = nextRequest;
        if(nextRequest!.dialog)
          setDialog(nextRequest.dialog);
        else if(nextRequest!.loader)
          setLoader(nextRequest.loader);
        else if(nextRequest!.custom)
          setCustom(nextRequest.custom);
        if(nextRequest.dialog || nextRequest.loader || nextRequest.custom)
          UIOverlay.currentOverlay = nextRequest;
        if(nextRequest.onShown)
          nextRequest.onShown();
        if(nextRequest.onGlobalDismiss)
          globalDismissEvent.registerOnce("onCurrentGlobalDismiss", ()=>nextRequest.onGlobalDismiss!(nextRequest.id ?? ""));
      }
      else
        hide();
    }

    function hide() {
      overlayCurrent = undefined;
      globalDismissEvent.unregister("onCurrentGlobalDismiss");
      setDialog(undefined);
      setCustom(undefined);
      setLoader(undefined);
      UIOverlay.currentOverlay = undefined;
    }

    return (
      <Show when={isActive$()}>
        <div class={styles.root} onClick={()=>globalDismissEvent.invoke()}>
          <OverlayDialog dialog={dialog$()} onGlobalDismiss={globalDismissEvent} />
          <OverlayDialogLoader dialog={loader$()} />
          <Show when={custom$()}>
            {custom$()}
          </Show>
        </div>
      </Show>
    );
  };
  
  export default OverlayModals;