
import { Component, For, Index, JSX, Match, Show, Switch, batch, createEffect, createMemo, createResource, createSignal, onCleanup, onMount } from 'solid-js';
import styles from './index.module.css';
import iconClose from '../../assets/icons/icon24_close.svg';
import UIOverlay from '../../state/UIOverlay';

import iconCheck from '../../assets/icons/icon_checkmark.svg'
import iconEdit from '../../assets/icons/icon24_edit.svg'
import { createResourceDefault, proxyImageVariable, toHumanBitrate } from '../../utility';
import ButtonFlex from '../../components/buttons/ButtonFlex';
import Button from '../../components/buttons/Button';
import InputText from '../../components/basics/inputs/InputText';
import { SubscriptionsBackend } from '../../backend/SubscriptionsBackend';
import OverlayImageSelector from '../OverlayImageSelector';
import OverlaySubscriptionsSelector from '../OverlaySubscriptionsSelector';


export interface OverlaySubscriptionGroupEditDialogProps {
  subscriptionGroup: ISubscriptionGroup,
  onResult?: (selected: ISubscriptionGroup) => void
};
const OverlaySubscriptionGroupEditDialog: Component<OverlaySubscriptionGroupEditDialogProps> = (props: OverlaySubscriptionGroupEditDialogProps) => {

    const [subscriptions$, subscriptionsResource] = createResourceDefault(async x=>(await SubscriptionsBackend.subscriptions()));

    const selected: string[] = [];
    const [selected$, setSelected] = createSignal<string[]>([]);

    const [stateImage$, setStateImage] = createSignal<boolean>(false);
    const [stateSubscriptions$, setStateSubscriptions] = createSignal<boolean>(false);
    const [stateConfirmDelete$, setConfirmDelete] = createSignal<boolean>(false);

    function select(sub: ISubscription) {
      const index = selected.indexOf(sub.channel.url);
      if(index >= 0)
        selected.splice(index, 1);
      else
        selected.push(sub.channel.url);
      setSelected([...selected]);
    }

    function deleteSelected() {
      props.subscriptionGroup.urls = props.subscriptionGroup.urls.filter(x=>selected$().indexOf(x) < 0);
      subscriptionsResource.refetch();
      setSelected([]);
    }
    function deleteGroup() {
      SubscriptionsBackend.subscriptionGroupDelete(props.subscriptionGroup.id);
      UIOverlay.dismiss();
    }

    function save(){
      UIOverlay.dismiss();
      SubscriptionsBackend.subscriptionGroupSave(props.subscriptionGroup)
    }

    function selectNewImage(img: IImageVariable) {
      if(!img)
        return;
      props.subscriptionGroup.image = img;
      setStateImage(false);
    }

    function onlyUnique(value: any, index: any, array: any) {
      return array.indexOf(value) === index;
    }
    function addSubscriptions(arr: string[]) {
      setStateSubscriptions(false);
      props.subscriptionGroup.urls = props.subscriptionGroup.urls.concat(arr).filter(onlyUnique);
      subscriptionsResource.refetch();
    }

    return (
      <Switch>
        <Match when={stateImage$()}>
          <OverlayImageSelector title='Subscription group image' description='Select Image for subscription group' channels={props.subscriptionGroup.urls}
            noDismiss={true}
            onResult={selectNewImage} />
        </Match>
        <Match when={stateSubscriptions$()}>
          <OverlaySubscriptionsSelector 
            preventDismiss={true}
            title='Subscription Group Subscriptions' 
            description='Select the subscriptions to add to your subscription groups'
            ignore={props.subscriptionGroup.urls ?? []}
            onResult={(selected) => addSubscriptions(selected)} />
        </Match>
        <Match when={stateConfirmDelete$()}>
          <div class={styles.container}> 
            <div class={styles.dialogHeader}>
              <div class={styles.headerText}>
                Are you sure you want to delete this group?
              </div>
              <div class={styles.headerSubText}>
                Deleted groups cannot be recovered
              </div>
            </div>
            <div style="height: 1px; background-color: rgba(255, 255, 255, 0.09); margin-top: 10px; margin-bottom: 10px;"></div>
            <div style="text-align: right">
                <Button text={"Cancel"}
                  onClick={()=>setConfirmDelete(false)}
                  style={{"margin-left": "auto", cursor: ("pointer")}}  />
                <Button text={"Delete"}
                  onClick={()=>deleteGroup()}
                  style={{"margin-left": "10px", cursor: ("pointer")}} 
                  color={"red"} />
            </div>
          </div>
        </Match>
        <Match when={!stateImage$()}>
          <div class={styles.container}> 
            <div class={styles.dialogHeader}>
              <div class={styles.headerText}>
                Edit Subscription Group
              </div>
              <div class={styles.headerSubText}>
                Here you can edit your subscription group
              </div>
              <div class={styles.closeButton} onClick={()=>UIOverlay.dismiss()}>
                <img src={iconClose} />
              </div>
            </div>
            <div style="margin-left: 20px; margin-right: 20px;">
              <div style="margin-top: 20px;">
                <div class={styles.sectionTitle}>Image</div>
                <div class={styles.sectionDescription}>Edit which image is used as background for your group</div>
                <div>
                    <div class={styles.image} style={{"background-image": "url(" + proxyImageVariable(props.subscriptionGroup.image) + ")"}} onClick={()=>setStateImage(true)}>
                      <div class={styles.text}>
                        <img src={iconEdit} />
                      </div>
                    </div>
                </div>
              </div>
              <div style="margin-top: 20px;">
                <div class={styles.sectionTitle}>Name</div>
                <div class={styles.sectionDescription}>Edit what the name of your group is.</div>
                <InputText placeholder='Subscription group name'
                  value={props.subscriptionGroup.name} onTextChanged={(val) => props.subscriptionGroup.name = val} />
              </div>
              <div style="margin-top: 20px;">
                <div class={styles.sectionTitle}>Subscriptions</div>
                <div class={styles.sectionDescription}>These are the subscriptions in the group, you can delete groups by selecting them and clicking Delete Selected.</div>
                <div class={styles.subscriptionsContainer}>
                  <For each={subscriptions$()?.filter(x=>props.subscriptionGroup.urls.indexOf(x.channel.url) >= 0)}>{ sub =>
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
            </div>
            <div style="height: 1px; background-color: rgba(255, 255, 255, 0.09); margin-top: 10px; margin-bottom: 10px;"></div>
            <div style="text-align: right">
                  <Show when={selected$().length > 0}>
                    <Button text={"Delete Selected"}
                      onClick={()=>deleteSelected()}
                      style={{"margin-left": "10px", cursor: ("pointer")}} 
                      color={"rgba(249, 112, 102, 0.08)"} />
                  </Show>
                <Button text={"Add Subscriptions"}
                  onClick={()=>setStateSubscriptions(true)}
                  style={{"margin-left": "10px", cursor: ("pointer")}} 
                  color={"#222"} />
                <Button text={"Delete Group"}
                  onClick={()=>setConfirmDelete(true)}
                  style={{"margin-left": "10px", cursor: ("pointer")}} 
                  color={"red"} />
                <Button text={"Save"}
                  onClick={()=>save()}
                  style={{"margin-left": "10px", cursor: ("pointer")}} 
                  color={"linear-gradient(267deg, #01D6E6 -100.57%, #0182E7 90.96%)"} />
            </div>
          </div>
        </Match>
      </Switch>
    );
  };
  
  export default OverlaySubscriptionGroupEditDialog;