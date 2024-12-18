
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

export interface OverlayShareDialogProps {
  text: string
};
const OverlayShareDialog: Component<OverlayShareDialogProps> = (props: OverlayShareDialogProps) => {
    const [didCopy$, setDidCopy] = createSignal(false);

    function copyText() {
      const text = props.text;
      navigator.clipboard.writeText(text);
      setDidCopy(true);
      setTimeout(()=>{
        setDidCopy(false);
      }, 800);
    }
    return (
      <div class={styles.container}> 
        <div class={styles.dialogHeader}>
          <div class={styles.headerText}>
            Share
          </div>
          <div class={styles.headerSubText}>
            Copy the text to share to an application.
          </div>
          <div class={styles.closeButton} onClick={()=>UIOverlay.dismiss()}>
            <img src={iconClose} />
          </div>
        </div>
          <div style="position: relative;">
            <div class={styles.copyText}>{props.text}</div>
            <Button text='Copy' onClick={copyText} style={{"margin-top": "10px", "width": "100%"}}></Button>
            <div class={styles.copyLabel} classList={{[styles.visible]: didCopy$()}}>
              Copied to clipboard
            </div>
          </div>
      </div>
    );
  };
  
  export default OverlayShareDialog;