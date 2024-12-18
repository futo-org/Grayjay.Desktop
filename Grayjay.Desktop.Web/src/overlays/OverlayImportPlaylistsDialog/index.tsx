
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

export interface OverlayImportPlaylistsDialogProps {
  dialog: CustomDialogLocal
};
const OverlayImportPlaylistsDialog: Component<OverlayImportPlaylistsDialogProps> = (props: OverlayImportPlaylistsDialogProps) => {

    function toggleSub(url: string) {
      console.log("Toggle Sub: " + url);
      props.dialog.action("selectPlaylist", url);
    }

    function importPlaylists() {
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
        
            <Switch>
              <Match when={props.dialog.data$().Status == 'selection'}>
              <div style="width: 500px" onClick={(ev)=>ev.stopPropagation()}>
                <div style="text-align: center;">
                  <div class={styles.dialogTitle}>Import Playlists
                    <Show when={props.dialog.data$().Total > (props.dialog.data$().Loaded + props.dialog.data$().Failed)}>
                      <LoaderSmall style={{"display": "inline-block", "height": "16px", "width": "16px", "margin-left": "10px"}}></LoaderSmall> 
                    </Show>
                  </div>
                  <div class={styles.dialogSubtitle}>
                    Playlists are being requested and will show up overtime. It may take a few minutes in case of lots of playlists.
                  </div>
                      <div>
                        <div class={styles.channels}>
                          <For each={props.dialog.data$().Playlists}>{ playlist =>
                            <div class={styles.channel}>
                              <div class={styles.toggle}>
                                <Toggle value={props.dialog.data$().Selected.indexOf(playlist.url) >= 0} onToggle={(v)=>{toggleSub(playlist.url)}} />
                              </div>
                              <div class={styles.thumbnail}>
                                <img src={playlist.thumbnail} referrerPolicy='no-referrer' />
                              </div>
                              <div class={styles.name}>
                                {playlist.name}
                              </div>
                            </div>
                          }</For>
                        </div>
                        <div style="margin-top: 10px; font-size: 12px;">
                          Total: {props.dialog.data$().Total}, Found: {props.dialog.data$().Loaded}, Failed: {props.dialog.data$().Failed}
                        </div>
                        <div style="text-align: center; margin-top: 15px;">
                          <Show when={props.dialog.data$().Playlists.filter((x: any)=>props.dialog.data$().Selected.indexOf(x.url) >= 0).length == props.dialog.data$().Playlists.length}>
                            <Button text='Deselect All' onClick={()=>deselectAll()} />
                          </Show>
                          <Show when={props.dialog.data$().Playlists.filter((x: any)=>props.dialog.data$().Selected.indexOf(x.url) >= 0).length != props.dialog.data$().Playlists.length}>
                            <Button text='Select All' onClick={()=>selectAll()} />
                          </Show>
                          <Button text='Cancel' onClick={()=>props.dialog.action!('close', '')} style={{"margin-left": "10px"}}></Button>
                          <Button text='Import' color={(props.dialog.data$().Selected.length > 0) ? '#019BE7' : '#181818'} onClick={()=>importPlaylists()} style={{"margin-left": "10px"}}></Button>
                        </div>
                      </div>
                    </div>
                  </div>
              </Match>

              <Match when={props.dialog.data$().Status == 'finished'}>
                <div style="text-align: center;">
                  <div>
                    <img src={iconSuccess} style="width: 100px" />
                  </div>
                  <h2>Finished</h2>
                  <div style="margin-bottom: 20px;">
                    Imported {props.dialog.data$().Selected.length} Playlists
                  </div>
                  <div>
                    <Button text='Close' onClick={()=>UIOverlay.dismiss()}></Button>
                  </div>
                </div>
              </Match>
            </Switch>
      </OverlayCustomDialog>
    );
  };
  
  export default OverlayImportPlaylistsDialog;