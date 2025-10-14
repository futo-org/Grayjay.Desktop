
import { Accessor, Component, For, Index, JSX, Match, Show, Suspense, Switch, batch, createEffect, createMemo, createResource, createSignal, onCleanup, onMount } from 'solid-js';
import styles from './index.module.css';
import iconClose from '../../assets/icons/icon24_close.svg';
import UIOverlay from '../../state/UIOverlay';

import iconCheck from '../../assets/icons/icon_checkmark.svg'
import { toHumanBitrate } from '../../utility';
import ButtonFlex from '../../components/buttons/ButtonFlex';
import Button from '../../components/buttons/Button';
import { DownloadBackend } from '../../backend/DownloadBackend';
import Loader from '../../components/basics/loaders/Loader';

import { ImportBackend } from '../../backend/ImportBackend';
import DescriptorButton from '../../components/buttons/DescriptorButton';

import iconZip from '../../assets/icons/icon_zip_detailed.svg'
import iconZipEncrypted from '../../assets/icons/ic_zip_encrypted_detailed.svg'
import iconPlatforms from '../../assets/icons/ic_sources_detailed.svg'
import iconNewPipe from '../../assets/icons/ic_newpipe.svg'
import iconDocument from '../../assets/icons/ic_document_detailed.svg'
import { Navigator, useNavigate } from '@solidjs/router';
import { SyncDevice } from '../../backend/models/sync/SyncDevice';
import StateSync from '../../state/StateSync';

export interface OverlaySelectOnlineSyncDeviceProps {
  title: string,
  description: string,
  act: (arg0: SyncDevice) => void
};
const OverlaySelectOnlineSyncDeviceDialog: Component<OverlaySelectOnlineSyncDeviceProps> = (props: OverlaySelectOnlineSyncDeviceProps) => {
    return (
      <div class={styles.container} style="width: 700px" onClick={(ev) => ev.stopPropagation()} onMouseDown={(ev) => ev.stopPropagation()}> 
        <div class={styles.dialogHeader}>
          <div class={styles.headerText}>
            {props.title}
          </div>
          <div class={styles.headerSubText}>
            {props.description}
          </div>
          <div class={styles.closeButton} onClick={()=>UIOverlay.dismiss()}>
            <img src={iconClose} />
          </div>
        </div>
          <div>
            <For each={StateSync.devicesOnline$()}>{ dev => 
              <div onClick={()=>{UIOverlay.dismiss(); props.act(dev)}} style="cursor: pointer">
                  <div style="display: flex; flex-direction: row; border-radius: 6px; background: #1B1B1B; padding: 14px 18px 14px; gap: 12px;  margin-left: 24px; margin-right: 24px; align-items: center;">
                    <img src={StateSync.getSyncIcon(dev.linkType)} style="width: 44px;" />
                    <div style="display: flex; flex-direction: column; flex-grow: 1; align-items: flex-start; justify-content: center;">
                        <div style="overflow: hidden; color: white; text-align: center; text-overflow: ellipsis; font-family: Inter; font-size: 14px; font-style: normal; font-weight: 500; line-height: normal;">{dev.displayName ?? dev.publicKey}</div>
                        <div style="color: #595959; font-family: Inter; font-size: 10px; font-style: normal; font-weight: 500; line-height: normal;">{dev.metadata}</div>
                    </div>
                
                    <div style="border-radius: 4px; border: 1px solid #2E2E2E; display: flex; padding: 6px 8px; align-items: center; gap: 4px; flex-shrink: 0; height: fit-content;">
                      <img src={StateSync.getSyncIcon(dev.linkType)} style="width: 20x; cursor: pointer;" />
                      <div style="color: #BFBFBF; font-family: Inter; font-size: 10px; font-style: normal; font-weight: 500; line-height: normal;">{StateSync.getSyncLinkName(dev.linkType)}</div>
                    </div>
                  </div>
              </div>
            }</For>
          </div>
      </div>
    );
  };
  
  export default OverlaySelectOnlineSyncDeviceDialog;