
import { Component, For, Index, JSX, Match, Show, Switch, batch, createEffect, createMemo, createResource, createSignal, onCleanup, onMount } from 'solid-js';
import styles from './index.module.css';
import iconClose from '../../assets/icons/icon24_close.svg';
import UIOverlay from '../../state/UIOverlay';

import iconCheck from '../../assets/icons/icon_checkmark.svg'
import { createResourceDefault, toHumanBitrate } from '../../utility';
import ButtonFlex from '../../components/buttons/ButtonFlex';
import Button from '../../components/buttons/Button';
import InputText from '../../components/basics/inputs/InputText';
import { SubscriptionsBackend } from '../../backend/SubscriptionsBackend';


export interface OverlaySubscsriptionsSelectorDialogProps {
  title: string,
  description: string,
  ignore: string[],
  preventDismiss: boolean | undefined,
  onResult?: (selected: string[]) => void
};
const OverlaySubscriptionsSelector: Component<OverlaySubscsriptionsSelectorDialogProps> = (props: OverlaySubscsriptionsSelectorDialogProps) => {

    const [subscriptions$] = createResourceDefault(async x=>(await SubscriptionsBackend.subscriptions()).filter(x=>!props.ignore || props.ignore.indexOf(x.channel.url) < 0));

    const selected: string[] = [];
    const [selected$, setSelected] = createSignal<string[]>([]);

    function select(sub: ISubscription) {
      const index = selected.indexOf(sub.channel.url);
      if(index >= 0)
        selected.splice(index, 1);
      else
        selected.push(sub.channel.url);
      setSelected([...selected]);
    }

    function submit(){
      if(!props.preventDismiss)
        UIOverlay.dismiss();
      if(props.onResult)
        props.onResult(selected);
    }

    return (
      <div class={styles.container}> 
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
          <div style="margin-top: 30px;">
            <InputText placeholder='Search for videos or creators'
              style={{"margin": "10px"}} />
          </div>
          <div class={styles.subscriptionsContainer}>
            <For each={subscriptions$()}>{ sub =>
              <div class={styles.subscription} classList={{[styles.enabled]: selected$().indexOf(sub.channel.url) >= 0}} onClick={()=>select(sub)}>
                <div class={styles.check}>
                  <img src={iconCheck} />
                </div>
                <div class={styles.image} style={{"background-image": "url(" + sub.channel.thumbnail + ")"}}>

                </div>
                <div class={styles.name}>
                  {sub.channel.name}
                </div>
              </div>
            }</For>
          </div>
        </div>
        <div style="height: 1px; background-color: rgba(255, 255, 255, 0.09); margin-top: 10px; margin-bottom: 10px;"></div>
        <div style="text-align: right">
            <Button text={"Select " + selected$().length + " creators"}
              onClick={()=>submit()}
              style={{"margin-left": "auto", cursor: ("pointer")}} 
              color={"linear-gradient(267deg, #01D6E6 -100.57%, #0182E7 90.96%)"} />
        </div>
      </div>
    );
  };
  
  export default OverlaySubscriptionsSelector;