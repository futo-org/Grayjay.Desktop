
import { Component, For, Match, Show, Switch, batch, createEffect, createMemo, createResource, createSignal, onCleanup, onMount } from 'solid-js';
import styles from './index.module.css';
import chevron_right from '../../../../assets/icons/icon_chevron_right.svg';
import UIOverlay from '../../state/UIOverlay';
import { Event1 } from '../../utility/Event'
import InputText from '../../components/basics/inputs/InputText';
import Dropdown from '../../components/basics/inputs/Dropdown';
import OverlayCustomDialog from '../OverlayCustomDialog';
import iconWebBlue from "../../assets/icons/icon_link_blue.svg"
import iconLink from "../../assets/icons/icon_link.svg"
import iconError from "../../assets/icons/icon_error_warning.svg"
import IPluginPrompt from '../../backend/models/plugin/IPluginPrompt';
import { CustomDialogLocal, CustomDialogRemote } from '../OverlayRoot';
import Button from '../../components/buttons/Button';
import Loader from '../../components/basics/loaders/Loader';
import LoaderSmall from '../../components/basics/loaders/LoaderSmall';

import iconGear from "../../assets/icons/icon_32_settings.svg"
import iconPluginSettings from "../../assets/icons/ic_plugin_settings.svg"
import iconSources from "../../assets/icons/ic_circles.svg"
import iconChevDown from "../../assets/icons/icon_chrevron_down.svg"
import iconChevUp from "../../assets/icons/icon_chevron_right.svg"

import iconFail from "../../assets/icons/ic_fail_small.svg"
import iconSuccess from "../../assets/icons/ic_success_small.svg"

import Toggle from '../../components/basics/inputs/Toggle';

export interface OverlayImportSubscriptionsDialogProps {
  dialog: CustomDialogLocal
};
const OverlayImportSubscriptionsDialog: Component<OverlayImportSubscriptionsDialogProps> = (props: OverlayImportSubscriptionsDialogProps) => {

    function toggleSub(url: string) {
      console.log("Toggle Sub: " + url);
      props.dialog.action("selectSubscription", url);
    }

    function importSubs() {
      if(props.dialog.data$().Selected.length <= 0)
        return;
      props.dialog.action!('import', '');
    }

    function selectAll(){
      props.dialog.action!('selectAll', '');
    }
    function deselectAll(){
      props.dialog.action!('deselectAll', '');
    }

    return (
      <OverlayCustomDialog hideHeader={true}>
        
            <>
              <Show when={props.dialog.data$().Status == 'selection'}>
              <div style="width: 500px" onClick={(ev)=>ev.stopPropagation()}>
                <div style="text-align: center;">
                  <div class={styles.dialogTitle}>Import Subscriptions
                    <Show when={props.dialog.data$().Total > (props.dialog.data$().Loaded + props.dialog.data$().Failed)}>
                      <LoaderSmall style={{"display": "inline-block", "height": "16px", "width": "16px", "margin-left": "10px"}}></LoaderSmall> 
                    </Show>
                  </div>
                  <div class={styles.dialogSubtitle}>
                    Channels are being requested and will show up overtime. It may take a few minutes in case of lots of subscriptions.
                  </div>
                      <div>
                        <div class={styles.channels}>
                          <For each={props.dialog.data$().Channels}>{ channel =>
                            <div class={styles.channel}>
                              <div class={styles.toggle}>
                                <Toggle value={props.dialog.data$().Selected.indexOf(channel.url) >= 0} onToggle={(v)=>{toggleSub(channel.url)}} />
                              </div>
                              <div class={styles.thumbnail}>
                                <img src={channel.thumbnail} referrerPolicy='no-referrer' />
                              </div>
                              <div class={styles.name}>
                                {channel.name}
                              </div>
                            </div>
                          }</For>
                        </div>
                        <div style="margin-top: 10px; font-size: 12px;">
                          Total: {props.dialog.data$().Total}, Found: {props.dialog.data$().Loaded}, Failed: {props.dialog.data$().Failed}
                        </div>
                        <div style="text-align: center; margin-top: 15px;">
                          <Show when={props.dialog.data$().Channels.filter((x: any)=>props.dialog.data$().Selected.indexOf(x.url) >= 0).length == props.dialog.data$().Channels.length}>
                            <Button text='Deselect All' onClick={()=>deselectAll()} />
                          </Show>
                          <Show when={props.dialog.data$().Channels.filter((x: any)=>props.dialog.data$().Selected.indexOf(x.url) >= 0).length != props.dialog.data$().Channels.length}>
                            <Button text='Select All' onClick={()=>selectAll()} />
                          </Show>
                          <Button text='Cancel' onClick={()=>props.dialog.action!('close', '')} style={{"margin-left": "10px"}}></Button>
                          <Button text='Import' color={(props.dialog.data$().Selected.length > 0) ? '#019BE7' : '#181818'} onClick={()=>importSubs()} style={{"margin-left": "10px"}}></Button>
                        </div>
                      </div>
                    </div>
                  </div>
              </Show>

              <Show when={props.dialog.data$().Status == 'finished'}>
                <div style="text-align: center;">
                  <div>
                    <img src={iconSuccess} style="width: 100px" />
                  </div>
                  <h2>Finished</h2>
                  <div style="margin-bottom: 20px;">
                    Imported {props.dialog.data$().Selected.length} Subscriptions
                  </div>
                  <div>
                    <Button text='Close' onClick={()=>UIOverlay.dismiss()}></Button>
                  </div>
                </div>
              </Show>
            </>
      </OverlayCustomDialog>
    );
  };
  
  export default OverlayImportSubscriptionsDialog;