
import { Component, For, Match, Show, Switch, batch, createEffect, createMemo, createResource, createSignal, onCleanup, onMount } from 'solid-js';
import styles from './index.module.css';
import chevron_right from '../../../../assets/icons/icon_chevron_right.svg';
import SettingsContainer from '../../../components/containers/SettingsContainer';
import { ISettingsField, ISettingsObject } from '../../../backend/models/settings/SettingsObject';
import { ISettingsFieldReadOnly } from '../../../backend/models/settings/fields/SettingsFieldReadOnly';
import { ISettingsFieldDropDown } from '../../../backend/models/settings/fields/SettingsFieldDropDown';
import { ISettingsFieldToggle } from '../../../backend/models/settings/fields/SettingsFieldToggle';
import { ISettingsFieldButton } from '../../../backend/models/settings/fields/SettingsFieldButton';
import { ISettingsFieldGroup } from '../../../backend/models/settings/fields/SettingsFieldGroup';
import { SourcesBackend } from '../../../backend/SourcesBackend';
import StateGlobal from '../../../state/StateGlobal';
import { SettingsBackend } from '../../../backend/SettingsBackend';
import UIOverlay from '../../../state/UIOverlay';
import Button from '../../../components/buttons/Button';
import { ImportBackend } from '../../../backend/ImportBackend';
import StateWebsocket from '../../../state/StateWebsocket';
import ScrollContainer from '../../../components/containers/ScrollContainer';
import { LocalBackend } from '../../../backend/LocalBackend';
import { createResourceDefault } from '../../../utility';


export interface SourceDetailsProps {
  configId: string
};
const SourceDetails: Component<SourceDetailsProps> = (props: SourceDetailsProps) => {
    let containerRef: HTMLDivElement;

    let sourceAppSettingsId: string | undefined = undefined;
    let sourceAppSettingsChanged = false;
    let sourceSettingsId: string | undefined = undefined;
    let sourceSettingsChanged = false;

    const [sourceResource$, sourceResource] = createResourceDefault(()=>props.configId,async (y) => (props.configId ? await SourcesBackend.sourceDetails(props.configId) : null))
    const [sourceAppSettings$] = createResourceDefault(()=>props.configId,async (y) =>{
      const id = props.configId;
      if(!id)
        return undefined;
      let settings = await SourcesBackend.sourceAppSettings(id);
      sourceAppSettingsId = id;
      sourceAppSettingsChanged = false;
      return settings;
    });
    const [sourceSettings$] = createResourceDefault(()=>props.configId,async (y) => {
      const id = props.configId;
      let settings = await SourcesBackend.sourceSettings(id)
      sourceSettingsId = id;
      sourceSettingsChanged = false;
      return settings;
    });
    const [ready$, setReady] = createSignal(false);

    StateWebsocket.registerHandlerNew("PluginUpdated", (packet)=>{
      if(props.configId == packet.payload) {
        console.log("Plugin changed during viewing");
        sourceResource.refetch();        
      }
    }, "SourceDetailsChanged");

    onCleanup(()=>{
      StateWebsocket.unregisterHandler("PluginUpdated", "SourceDetailsChanged");
    });
    
    function onSourceAppSettingsChanged(field: ISettingsField, newVal: any) {
      if(sourceAppSettingsId) {
        console.log("App Setting [" + field.property + "] changed to", newVal);
        sourceAppSettingsChanged = true;
      }
    }
    function onSourceSettingsChanged(field: ISettingsField, newVal: any) {
      if(sourceSettingsId) {
        console.log("Setting [" + field.property + "] changed to", newVal);
        sourceSettingsChanged = true;
      }
    }

    createEffect(()=>{
      const oldId = sourceAppSettingsId;
      console.log("SourceDetails ConfigID: " + props.configId + ((oldId) ? ", Old: " + oldId : ""));
      if(oldId) {
        if(sourceAppSettingsChanged) {
          console.log("Source app settings changed: ", sourceSettings$());
          saveAppSettings();
        }
        if(sourceSettingsChanged) {
          console.log("Source settings changed: ", sourceSettings$());
          saveSettings();
        }
      }
      if(props.configId && props.configId.length > 0) {
        setReady(true);
      }
    });

    onCleanup(()=>{
      if(sourceResource$()) {
        console.log("SourceDetails saving");
        if(sourceAppSettingsChanged)
          saveAppSettings();
        if(sourceSettingsChanged)
          saveSettings();
      }
    });
    function saveAppSettings() {
      console.log("Saving source app settings");
      const settingsApp = sourceAppSettings$();
      if(settingsApp?.onSave)
        settingsApp?.onSave();
    }
    function saveSettings() {
      console.log("Saving source settings");
      const settings = sourceSettings$();
      if(settings?.onSave)
        settings?.onSave();
    }

    function login(id: string){
      SourcesBackend.login(id)
    }
    async function logout(id: string){
      await SourcesBackend.logout(id);
      sourceResource.refetch();
    }

    //TODO: Better update procedure
    function reinstallFromRemote(){
      const url = sourceResource$()?.config.sourceUrl;
      if(url)
        UIOverlay.installPluginPrompt(url);
    }
    function uninstall(){
      SourcesBackend.sourceDelete(sourceResource$()?.config.id ?? "");
    }

    async function importSubscriptions(id: string) {
        try {
          const urls = await ImportBackend.getUserSubscriptions(id);
          ImportBackend.importSubscriptions(urls);
        }
        catch(ex) {
          UIOverlay.dialog({ title: "Failed to get subscriptions", description: ex + "", buttons: []})
        }
    }
    async function importPlaylists(id: string) {
      try {
        const urls = await ImportBackend.getUserPlaylists(id);
        ImportBackend.importPlaylists(urls);
      }
      catch(ex) {
        UIOverlay.dialog({ title: "Failed to get subscriptions", description: ex + "", buttons: []})
      }
    }

    let scrollContainerRef: HTMLDivElement | undefined;

    return (
      <Show when={ready$()}>
        <div style="height: 100%; display: flex; flex-direction: column;">
          <div class={styles.header}>
            <div class={styles.icon}>
              <img src={StateGlobal.getSourceConfig(props.configId)?.absoluteIconUrl} />
            </div>
            <div class={styles.descriptor}>
              <div class={styles.title}>
                {sourceResource$()?.config?.name}
              </div>
              <div class={styles.description}>
                {sourceResource$()?.config?.description}
              </div>
              <div class={styles.meta}>
                Version {sourceResource$()?.config?.version} • by <a href="" onClick={async () => {
                  const authorUrl = sourceResource$()?.config?.authorUrl;
                  if (!authorUrl) {
                    return;
                  }
                  await LocalBackend.open(authorUrl);
                }}>{sourceResource$()?.config?.author}</a>
              </div>
            </div>
          </div>
          <div class={styles.settings}>
            <Show when={sourceResource$()?.hasUpdate}>
              <Button style={{width: "calc(100% - 30px)", "margin-bottom": "10px"}} text='Update' onClick={()=>reinstallFromRemote()}></Button>
            </Show>
            <Show when={!!sourceResource$()?.config?.authentication}>
              <Show when={!sourceResource$()?.hasLoggedIn}>
                <Button style={{width: "calc(100% - 30px)", "margin-bottom": "10px"}} text='Login' onClick={()=>login(sourceResource$()!.config.id)}></Button>
              </Show>
              <Show when={!!sourceResource$()?.hasLoggedIn}>
                <Button style={{width: "calc(100% - 30px)", "margin-bottom": "10px"}} text='Logout' onClick={()=>logout(sourceResource$()!.config.id)}></Button>
              </Show>
              <Show when={!!sourceResource$()?.state?.capabilities?.hasGetUserSubscriptions && !!sourceResource$()?.hasLoggedIn}>
                <Button style={{width: "calc(100% - 30px)", "margin-bottom": "10px"}} text='Import Subscriptions' onClick={()=>importSubscriptions(sourceResource$()!.config.id)}></Button>
              </Show>
              <Show when={!!sourceResource$()?.state?.capabilities?.hasGetUserPlaylists && !!sourceResource$()?.hasLoggedIn}>
                <Button style={{width: "calc(100% - 30px)", "margin-bottom": "10px"}} text='Import Playlists' onClick={()=>importPlaylists(sourceResource$()!.config.id)}></Button>
              </Show>
            </Show>
            <ScrollContainer ref={scrollContainerRef} style={{"flex-grow": 1, "width": "100%"}}>
              <SettingsContainer settings={sourceAppSettings$()} onFieldChanged={onSourceAppSettingsChanged} />
              <Show when={sourceSettings$() && (sourceSettings$()?.fields?.length ?? 0) > 0}>
                <h2 style="margin-top: 56px;">Plugin Settings</h2>
                <SettingsContainer settings={sourceSettings$()} onFieldChanged={onSourceSettingsChanged} />
              </Show>
              
              <Button style={{width: "calc(100% - 54px)", "margin-bottom": "10px", "margin-left": "24px"}} text='Uninstall' color='#550000' onClick={()=>uninstall()}></Button>
            </ScrollContainer>
          </div>
        </div>
      </Show>
    );
  };
  
  export default SourceDetails;