
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
import { focusScope } from '../../focusScope'; void focusScope;
import { focusable } from "../../focusable"; void focusable;
import { Navigator, useNavigate } from '@solidjs/router';

export interface OverlayImportSelectDialogProps {
  
};
const OverlayImportSelectDialog: Component<OverlayImportSelectDialogProps> = (props: OverlayImportSelectDialogProps) => {
    const globalBack = () => (UIOverlay.dismiss(), true);

    return (
      <div class={styles.container} use:focusScope={{
          initialMode: 'trap'
      }}> 
        <div class={styles.dialogHeader}>
          <div class={styles.headerText}>
            Import
          </div>
          <div class={styles.headerSubText}>
            Select the format you would like to import.
          </div>
          <div class={styles.closeButton} onClick={()=>UIOverlay.dismiss()}>
            <img src={iconClose} />
          </div>
        </div>
          <div>
            <div style="margin-bottom: 10px;">
              <DescriptorButton icon={iconPlatforms} text='Import from platform' disabled={true} onClick={()=>{UIOverlay.dismiss();}} style={{"width": "100%"}}
                description='Import your data from a specific source' />
            </div>
            <div style="margin-bottom: 10px;">
              <DescriptorButton icon={iconZip} text='Import Grayjay Export (.zip)' onClick={()=>{UIOverlay.dismiss(); ImportBackend.importZip()}} style={{"width": "100%"}}
                description='Import zip file exported from Grayjay mobile app or desktop application.'
                focusableOpts={{
                  onPress: () => {UIOverlay.dismiss(); ImportBackend.importZip()},
                  onBack: globalBack
                }} />
            </div>
            <div style="margin-bottom: 10px;">
              <DescriptorButton icon={iconZipEncrypted} text='Import Grayjay Auto-Backup (.ezip)' disabled={true} onClick={()=>{UIOverlay.dismiss();}} style={{"width": "100%"}}
                description='Pick a Grayjay auto-backup encrypted zip file' />
            </div>
            <div style="margin-bottom: 10px;">
              <DescriptorButton icon={iconDocument} text='Import Line Text file (.txt)' disabled={true} onClick={()=>{UIOverlay.dismiss();}} style={{"width": "100%"}}
                description='Pick a text file with one entry per line' />
            </div>
            <div style="margin-bottom: 40px;">
              <DescriptorButton icon={iconNewPipe} text='NewPipe Subscriptions (.json)' onClick={()=>{UIOverlay.dismiss(); ImportBackend.importNewPipe()}} style={{"width": "100%"}}
                description='Pick a NewPipe subscriptions json file'
                focusableOpts={{
                  onPress: () => {UIOverlay.dismiss(); ImportBackend.importNewPipe()},
                  onBack: globalBack
                }} />
            </div>
            <div>
              <Button text='Cancel' color='transparant' onClick={()=>UIOverlay.dismiss()} style={{"width": "100%"}}
                focusableOpts={{
                  onPress: globalBack,
                  onBack: globalBack
                }} />
            </div>
          </div>
      </div>
    );
  };
  
  export default OverlayImportSelectDialog;