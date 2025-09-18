import { Component, JSX, Show, batch, createMemo, createSignal, onCleanup } from 'solid-js';
import styles from './index.module.css';
import OverlayDialog, { DialogDescriptor, IDialogOutput } from '../OverlayDialog';
import OverlayDialogLoader, { LoaderDescriptor } from '../OverlayDialogLoader';
import { ToastDescriptor } from '../OverlayRoot';
import UIOverlay from '../../state/UIOverlay';
import { Event0 } from '../../utility/Event';

export interface OverlayRequest {
  dialog?: DialogDescriptor,
  loader?: LoaderDescriptor,
  custom?: () => JSX.Element,
  output?: IDialogOutput,
  toast?: ToastDescriptor,
  id?: string,

  onShown?: ()=>void,
  onGlobalDismiss?: (id: string)=>void
}

const OverlayModals: Component = () => {
 
    const [dialog$, setDialog] = createSignal<DialogDescriptor | undefined>(undefined);
    const [loader$, setLoader] = createSignal<LoaderDescriptor | undefined>(undefined);
    const [customFactory$, setCustomFactory] = createSignal<(() => JSX.Element) | undefined>(undefined);
    const isActive$ = createMemo(() => !!(dialog$() || customFactory$() || loader$()));

    let overlayCurrent: OverlayRequest | undefined = undefined;
    const overlayQueue: OverlayRequest[] = [];
    const globalDismissEvent = new Event0();

    UIOverlay.onOverlay.registerOne('overlay', (req) => {
      if (!req.toast) {
        if (!req.id) req.id = (Math.random() + 1).toString(36).substring(7);
        overlayQueue.push(req);
      }
      if (!overlayCurrent && !isActive$()) {
        loadNextRequest();
      }
    });

    UIOverlay.onDismiss.registerOne('overlay', (id: string) => {
      if (!overlayCurrent || overlayCurrent.id !== id) return;
      loadNextRequest();
    });
      
    onCleanup(() => globalDismissEvent.unregister('onCurrentGlobalDismiss'));

    function swapContent(next?: OverlayRequest) {
      batch(() => {
        setDialog(undefined);
        setLoader(undefined);
        setCustomFactory(undefined);
        if (!next) return;
        if (next.dialog) setDialog(next.dialog);
        else if (next.loader) setLoader(next.loader);
        if (next.custom) setCustomFactory(() => next.custom);
        else setCustomFactory(undefined);
      });
    }

    function loadNextRequest() {
      const next = overlayQueue.shift();
      console.info("loadNextRequest", overlayQueue);
      globalDismissEvent.unregister('onCurrentGlobalDismiss');
      if (!next) {
        overlayCurrent = undefined;
        swapContent(undefined);
        UIOverlay.currentOverlay = undefined;
        return;
      }

      overlayCurrent = next;
      UIOverlay.currentOverlay = next;
      if (next.onGlobalDismiss) {
        globalDismissEvent.registerOnce('onCurrentGlobalDismiss', () =>
          next.onGlobalDismiss!(next.id ?? '')
        );
      }

      swapContent(next);
      next.onShown?.();
    }

    return (
      <div
        class={styles.root}
        // Hide when inactive but DO NOT unmount; avoids provider/context churn.
        style={{ display: isActive$() ? undefined : 'none' }}
        onClick={() => globalDismissEvent.invoke()}
      >
        <div onClick={(e) => e.stopPropagation()}>
          <OverlayDialog dialog={dialog$()} onGlobalDismiss={globalDismissEvent} />
          <OverlayDialogLoader dialog={loader$()} />
          {customFactory$()?.()}
        </div>
      </div>
    );
  };
  
  export default OverlayModals;