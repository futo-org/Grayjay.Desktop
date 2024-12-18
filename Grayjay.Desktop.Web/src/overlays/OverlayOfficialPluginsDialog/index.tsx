
import { Accessor, Component, For, Index, JSX, Match, Show, Suspense, Switch, batch, createEffect, createMemo, createResource, createSignal, onCleanup, onMount } from 'solid-js';
import styles from './index.module.css';
import iconClose from '../../assets/icons/icon24_close.svg';
import UIOverlay from '../../state/UIOverlay';

import iconCheck from '../../assets/icons/icon_checkmark.svg'
import { createResourceDefault, toHumanBitrate } from '../../utility';
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
import { SourcesBackend } from '../../backend/SourcesBackend';
import { ISourceConfig } from '../../backend/models/plugin/ISourceConfigState';
import Checkbox from '../../components/basics/inputs/Checkbox';
import { DialogButton, DialogDescriptor, DialogInputText, IDialogOutput } from '../OverlayDialog';

export interface OverlayOfficialPluginsDialogProps {
  
};
const OverlayOfficialPluginsDialog: Component<OverlayOfficialPluginsDialogProps> = (props: OverlayOfficialPluginsDialogProps) => {
  
    const [sources$, sourcesResource] = createResourceDefault(async () => [], async () => await SourcesBackend.officialSources());
    const [selected$, setSelected] = createSignal<string[]>([]);
    const [errors$, setErrors] = createSignal<string[]>([]);

    const [installing$, setInstalling] = createSignal<boolean>(false);


    async function install() {
      setInstalling(true);
      try {
        const result = await SourcesBackend.installOfficialSources(selected$());

        if(result.exceptions && result.exceptions.length > 0) {
          setErrors(result.exceptions);
          sourcesResource.refetch();
          setInstalling(false);
        }
        else
          UIOverlay.dismiss();
      }
      catch(ex) {
        sourcesResource.refetch();
        setInstalling(false);
      }
    }

    function toggleSource(source: ISourceConfig){
      if(!selected$())
        return;
      if(selected$().indexOf(source.id!) >= 0)
        setSelected(selected$().filter(x=>x != source.id));
      else
        setSelected(selected$().concat([source.id]));
    }

    function installPeertubeSource() {
      UIOverlay.dismiss();
      setTimeout(()=>{
        UIOverlay.dialog({
          title: "PeerTube Instance",
          description: "Enter the peertube instance url, make sure you trust the server.",
          input: new DialogInputText("PeerTube instance url"),
          buttons: [
            {
              title: "Cancel",
              style: "none",
              onClick() {
                
              }
            } as DialogButton,
            {
              title: "Install",
              style: "primary",
              async onClick(output: IDialogOutput) {
                if(output.text) {
                  const result = await SourcesBackend.sourceInstallPeerTubePrompt(output.text);
                  UIOverlay.installPluginDialog(result);
                }
              }
            } as DialogButton]
        } as DialogDescriptor)
      }, 100)
    }

    return (
      <div class={styles.container}> 
        <Show when={!installing$()}>
          <div class={styles.dialogHeader}>
            <div class={styles.headerText}>
              Official Plugins
            </div>
            <div class={styles.headerSubText}>
              These are official plugins you can quickly install.
            </div>
            <div class={styles.closeButton} onClick={()=>UIOverlay.dismiss()}>
              <img src={iconClose} />
            </div>
          </div>
          <div>
            <For each={errors$()}>{error =>
              <div class={styles.error}>
                {error}
              </div>
            }</For>
          </div>
          <div class={styles.sources} style="position: relative;">
            <For each={sources$()}>{ config =>
              <div class={styles.source} onClick={()=>toggleSource(config)}>
                <div class={styles.checkContainer}>
                  <Checkbox value={selected$().indexOf(config.id) >= 0} onChecked={(val)=>{toggleSource(config)}} 
                    style={{height: "100%", width: "100%"}}
                    checkBoxStyle={{height: "100%", width: "100%"}}
                    checkStyle={{height: "90%", width: "90%"}} />
                </div>
                <div class={styles.imageContainer}>
                  <img class={styles.image} src={config.absoluteIconUrl} />
                </div>
                <div class={styles.name}>
                  {(config.name != "PeerTube") ? config.name : "FUTO PeerTube"}
                </div>
              </div>
            }</For>
            <div classList={{[styles.source]: true, [styles.otherButton]: true}} onClick={()=>installPeertubeSource()}>
                <div class={styles.imageContainer}>
                  <img class={styles.image} src="https://plugins.grayjay.app/PeerTube/peertube.png" />
                </div>
                <div class={styles.name} style="font-size: 12px">
                  {"PeerTube Instance"}
                </div>
            </div>
          </div>
          
          <div style="height: 1px; background-color: rgba(255, 255, 255, 0.09); margin-top: 10px; margin-bottom: 10px;"></div>
            <div style="text-align: center">
                <Button text={"Install Selected"}
                  onClick={()=>selected$() && selected$().length > 0 && install()}
                  style={{"margin-left": "10px", cursor: ("pointer")}} 
                  color={((selected$() && selected$().length > 0) ? "linear-gradient(267deg, #01D6E6 -100.57%, #0182E7 90.96%)" : "")} />
            </div>
        </Show>
        <Show when={installing$()}>
          <div class={styles.dialogHeader}>
            <div class={styles.headerText}>
              Installing plugins
            </div>
            <div class={styles.headerSubText}>
              Busy installing the plugins you selected.
            </div>
            <div class={styles.closeButton} onClick={()=>UIOverlay.dismiss()}>
              <img src={iconClose} />
            </div>
          </div>
          <div>
            <Loader style={{"margin-left": "auto", "margin-right": "auto", "margin-top": "10px"}} />
          </div>
        </Show>
      </div>
    );
  };
  
  export default OverlayOfficialPluginsDialog;