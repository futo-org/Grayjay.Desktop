
import { Component, For, Index, JSX, Match, Show, Switch, batch, createEffect, createMemo, createResource, createSignal, onCleanup, onMount } from 'solid-js';
import styles from './index.module.css';
import iconClose from '../../assets/icons/icon24_close.svg';
import UIOverlay from '../../state/UIOverlay';

import iconCheck from '../../assets/icons/icon_checkmark.svg'
import { promptFile, toHumanBitrate } from '../../utility';
import ButtonFlex from '../../components/buttons/ButtonFlex';
import Button from '../../components/buttons/Button';
import InputText from '../../components/basics/inputs/InputText';
import { SubscriptionsBackend } from '../../backend/SubscriptionsBackend';
import { ImagesBackend } from '../../backend/ImagesBackend';
import OverlayCustomDialog from '../OverlayCustomDialog';


export interface OverlayImageProps {
  img: string,
  noDismiss?: boolean
};
const OverlayImage: Component<OverlayImageProps> = (props: OverlayImageProps) => {
    return (
      <OverlayCustomDialog onRootClick={()=>UIOverlay.dismiss()} hideDialog={true} hideHeader={true}>
        <div class={styles.dialogHeader}>
          <div class={styles.closeButton} onClick={()=>UIOverlay.dismiss()}>
            <img src={iconClose} />
          </div>
        </div>
        <div class={styles.container}> 
          <img src={props.img} class={styles.dialogImage} />
        </div>
      </OverlayCustomDialog>
    );
  };
  
  export default OverlayImage;