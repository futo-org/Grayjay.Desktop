
import { Accessor, Component, For, JSX, Match, Setter, Show, Switch, batch, createContext, createEffect, createMemo, createResource, createSignal, onCleanup, onMount, useContext } from 'solid-js';
import styles from './index.module.css';
import chevron_right from '../../../../assets/icons/icon_chevron_right.svg';
import warning from '../../assets/icons/icon_warning.svg';
import OverlayDialog, { DialogDescriptor, DialogDropdown, DialogInputText, IDialogOutput } from '../OverlayDialog';

import { Event1 } from '../../utility/Event'
import UIOverlay from '../../state/UIOverlay';
import OverlayDialogLoader, { LoaderDescriptor } from '../OverlayDialogLoader';
import StateWebsocket from '../../state/StateWebsocket';
import { WebSocketPacket } from '../../backend/models/socket/WebSocketPacket';
import { DialogBackend } from '../../backend/DialogBackend';
import OverlayCustomDialog from '../OverlayCustomDialog';
import OverlayImportDialog from '../OverlayImportDialog';
import OverlayImportSubscriptionsDialog from '../OverlayImportSubscriptionsDialog';
import OverlaySyncStatusDialog from '../OverlaySyncStatusDialog';
import OverlaySyncConfirmDialog from '../OverlaySyncConfirmDialog';
import { OverlayRequest } from '../OverlayModals';
import OverlayImportPlaylistsDialog from '../OverlayImportPlaylistsDialog';
import OverlayToasts from '../OverlayToasts';


export interface OverlayContext {
}

export interface ToastDescriptor {
  icon?: string,
  title?: string,
  text: string,
  long?: boolean,
  id?: string
}
export interface DialogDescriptorRemote {
  icon?: string,
  text?: string,
  textDetails?: string,
  code?: string,
  defaultCloseAction?: number,
  actions?: DialogActionRemote[]
}
export interface DialogActionRemote {
  text?: string,
  style?: number
}
export interface CustomDialogRemote {
  id: string,
  name: string,
  data: any
}
export interface CustomDialogLocal {
  id: string,
  name: string,
  data$: Accessor<any>,
  setData: Setter<any>,
  action: Function
}

export interface OverlayRootProps {
};
const OverlayRoot: Component<OverlayRootProps> = (props: OverlayRootProps) => {
  
    const [toast$, setToast] = createSignal<ToastDescriptor | undefined>(undefined);
    const isActive$ = createMemo(()=>toast$());

    let overlayCurrent: ToastDescriptor | undefined = undefined;
    const overlayQueue: ToastDescriptor[] = [];
    
    StateWebsocket.registerHandlerNew("Dialog", async (packet)=>{
      try {
        const dialog = packet.payload as DialogDescriptorRemote;
        const localDialog = {
          icon: dialog.icon,
          title: dialog.text,
          description: dialog.textDetails,
          code: dialog.code,
          buttons: dialog.actions?.map(x=>({
            title: x.text,
            style: x.style,
            onClick: (output)=>{
              StateWebsocket.socket.send(JSON.stringify({
                id: packet.id,
                type: "DialogResp",
                payload: output
              } as WebSocketPacket));
            }
          })),
          defaultAction: dialog.defaultCloseAction
        } as DialogDescriptor;
        if(dialog) {
          const resp = await UIOverlay.dialog(localDialog);
          if(resp)
            DialogBackend.dialogRespond(packet.id, resp);
        }
      }
      catch{}
    }, "dialogs");

    let customDialogs = {};
    StateWebsocket.registerHandlerNew("CustomDialog", async (packet)=>{
      try {
        const dialogRemote = packet.payload as CustomDialogRemote;
        if(dialogRemote) {

          const [data$, setData] = createSignal(dialogRemote.data);
          const dialogLocal = {
            id: dialogRemote.id,
            name: dialogRemote.name,
            data$: data$,
            setData: setData,
            action: (action: string, data: any)=>{
              DialogBackend.dialogRespondCustom(dialogRemote.id ?? "", action, data);
            }
          } as CustomDialogLocal;
          customDialogs[dialogLocal.id] = dialogLocal;
          let ui: () => JSX.Element;
          switch(dialogLocal.name) {
            case "import":
              ui = () => <OverlayImportDialog dialog={dialogLocal}></OverlayImportDialog>
              break;
            case "importSubs":
              ui = () => <OverlayImportSubscriptionsDialog dialog={dialogLocal}></OverlayImportSubscriptionsDialog>
              break;
            case "importPlaylists":
              ui = () => <OverlayImportPlaylistsDialog dialog={dialogLocal}></OverlayImportPlaylistsDialog>
              break;
            case "syncConfirm":
              ui = () => <OverlaySyncConfirmDialog dialog={dialogLocal}></OverlaySyncConfirmDialog>
              break;
            case "syncStatus":
              ui = () => <OverlaySyncStatusDialog dialog={dialogLocal}></OverlaySyncStatusDialog>
              break;
            default:
              throw Error('Unrecognized dialog type: ' + dialogLocal.name);
          }

          let overlayObj: OverlayRequest | undefined = undefined;
          overlayObj = UIOverlay.overlay({
            onGlobalDismiss: ()=>{
              delete customDialogs[dialogLocal.id!]
              UIOverlay.dismiss(overlayObj?.id);
              dialogLocal.action!("close", {});
            },
            custom: ui
          });
        }
      }
      catch{}
    }, "customDialogs");
    StateWebsocket.registerHandlerNew("CustomDialogUpdate", async (packet)=>{
      try {
        const dialog = customDialogs[packet.id];
        if(dialog) {
          dialog.setData(packet.payload);
        }
        
      }
      catch{}
    }, "customDialogs");
    StateWebsocket.registerHandlerNew("CustomDialogClose", async (packet)=>{
      try {
        const dialog = customDialogs[packet.id];
        if(dialog) {
          UIOverlay.dismiss();
        }
        
      }
      catch{}
    }, "customDialogs");

    function loadNextRequest() {
      const nextRequest = overlayQueue.shift();
      if(nextRequest) {
        hide();
        overlayCurrent = nextRequest;
        setToast(nextRequest);
        setTimeout(()=>{
          loadNextRequest();
        }, 3000);
      }
      else
        hide();
    }

    function hide() {
      overlayCurrent = undefined;
      setToast(undefined);
    }

    return (
      <div>
      <Show when={isActive$()}>
        <div class={styles.root}>
        </div>
      </Show>
      <OverlayToasts />
      </div>
    );
  };
  
  export default OverlayRoot;