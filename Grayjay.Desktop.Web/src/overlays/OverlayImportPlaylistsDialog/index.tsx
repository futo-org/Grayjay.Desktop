
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

import { focusable } from '../../focusable'; void focusable;
import Toggle from '../../components/basics/inputs/Toggle';
import { createStore, reconcile } from 'solid-js/store';

interface Playlist {
  url: string;
  name: string;
  thumbnail: string;
}

export interface OverlayImportPlaylistsDialogProps {
  dialog: CustomDialogLocal
};
const OverlayImportPlaylistsDialog: Component<OverlayImportPlaylistsDialogProps> = (props: OverlayImportPlaylistsDialogProps) => {
    const [view, setView] = createStore<{ Playlists: Playlist[] }>({ Playlists: [] });
    createEffect(() => {
      const d = props.dialog.data$();
      setView('Playlists', reconcile(d.Playlists ?? [], { key: 'url' }));
    });

    const selectedSet = createMemo(() => new Set<string>(props.dialog.data$().Selected ?? []));
    const selectedCount = createMemo(
      () => view.Playlists.reduce((n, c) => n + (selectedSet().has(c.url) ? 1 : 0), 0)
    );

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

    createEffect(() => console.info("OverlayImportPlaylistsDialog data", props.dialog.data$()));

    const globalBack = () => (UIOverlay.dismiss(), true);
    return (
      <OverlayCustomDialog hideHeader={true} focusScope={true}>
        
            <div>
              <Show when={props.dialog.data$().Status == 'selection'}>
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
                          <For each={view.Playlists}>{ playlist =>
                            <div class={styles.channel} use:focusable={{
                              onPress: () => toggleSub(playlist.url),
                              onBack: globalBack
                            }}>
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
                          <Show when={selectedCount() === view.Playlists.length && view.Playlists.length > 0}>
                            <Button
                              text='Deselect All'
                              onClick={deselectAll}
                              focusableOpts={{ onPress: deselectAll, onBack: globalBack }}
                            />
                          </Show>
                          <Show when={selectedCount() !== view.Playlists.length}>
                            <Button
                              text='Select All'
                              onClick={selectAll}
                              focusableOpts={{ onPress: selectAll, onBack: globalBack }}
                            />
                          </Show>
                          <Button text='Cancel' onClick={()=>props.dialog.action!('close', '')} style={{"margin-left": "10px"}} focusableOpts={{
                            onPress: () => props.dialog.action!('close', ''),
                            onBack: globalBack
                          }}></Button>
                          <Button text='Import' color={(props.dialog.data$().Selected.length > 0) ? '#019BE7' : '#181818'} onClick={()=>importPlaylists()} style={{"margin-left": "10px"}} focusableOpts={{
                            onPress: () => importPlaylists(),
                            onBack: globalBack
                          }}></Button>
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
                    Imported {props.dialog.data$().Selected.length} Playlists
                  </div>
                  <div>
                    <Button text='Close' onClick={()=>UIOverlay.dismiss()} focusableOpts={{
                      onPress: globalBack,
                      onBack: globalBack
                    }}></Button>
                  </div>
                </div>
              </Show>
            </div>
      </OverlayCustomDialog>
    );
  };
  
  export default OverlayImportPlaylistsDialog;