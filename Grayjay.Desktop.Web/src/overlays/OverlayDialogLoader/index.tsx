
import { Component, For, Match, Show, Switch, batch, createEffect, createMemo, createResource, createSignal, onCleanup, onMount } from 'solid-js';
import styles from './index.module.css';
import chevron_right from '../../../../assets/icons/icon_chevron_right.svg';
import UIOverlay from '../../state/UIOverlay';
import { Event0, Event1 } from '../../utility/Event'
import InputText from '../../components/basics/inputs/InputText';
import Dropdown from '../../components/basics/inputs/Dropdown';
import Loader from '../../components/basics/loaders/Loader';
import LoaderSmall from '../../components/basics/loaders/LoaderSmall';



export interface LoaderDescriptor {
  icon?: string,
  title: string,
  description: string,
  code?: string,
  onDismiss: Event1<string>
}

export interface OverlayDialogLoaderProps {
  dialog: LoaderDescriptor | undefined
};
const OverlayDialog: Component<OverlayDialogLoaderProps> = (props: OverlayDialogLoaderProps) => {

  
    props.dialog?.onDismiss?.registerOne("loader", (id)=>{
      UIOverlay.dismiss();
    });

    return (
      <Show when={props.dialog}>
        <div class={styles.dialog} onClick={(ev)=>ev.stopPropagation()} onMouseDown={(ev) => ev.stopPropagation()}>
          <Show when={props.dialog?.icon}>
            <img src={props.dialog?.icon} class={styles.icon} />
          </Show>
          <div class={styles.title}>
            {props.dialog!.title}
          </div>
          <div class={styles.description}>
            {props.dialog!.description}
          </div>
          <Show when={props.dialog?.code}>
            <div class={styles.code}>
                {props.dialog!.code}
            </div>
          </Show>
          <div class={styles.loaderContainer}>
            <LoaderSmall />
          </div>
        </div>
      </Show>
    );
  };
  
  export default OverlayDialog;