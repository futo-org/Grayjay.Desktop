import { For, type Component, createResource, createSignal, createEffect, JSX, onCleanup, Show } from 'solid-js';
import styles from './index.module.css';
import { SourcesBackend } from '../../backend/SourcesBackend';
import { Backend } from '../../backend/Backend';
import StateGlobal from '../../state/StateGlobal';
import iconThumb from '../../assets/icons/icon_thumb.svg'
import SourceDetails from '../subpages/SourceDetails';
import SettingsContainer from '../../components/containers/SettingsContainer';
import { SettingsBackend } from '../../backend/SettingsBackend';
import Button from '../../components/buttons/Button';
import { ImportBackend } from '../../backend/ImportBackend';
import UIOverlay from '../../state/UIOverlay';
import { Event0 } from '../../utility/Event';
import { ISettingsField } from '../../backend/models/settings/SettingsObject';
import ScrollContainer from '../../components/containers/ScrollContainer';
import { SyncBackend } from '../../backend/SyncBackend';
import { focusable } from '../../focusable'; void focusable;
import { createResourceDefault } from '../../utility';


export interface SettingsPageProps {
  settingsMenuStyle?: JSX.CSSProperties,
  settingsContainerStyle?: JSX.CSSProperties,
  onClosingEvent?: Event0
};

const SettingsPage: Component<SettingsPageProps> = (props) => {

  const [filterGroup$, setFilterGroup] = createSignal<string | undefined>();
  const [settings$] = createResourceDefault(async () => [], async () => await SettingsBackend.settings());

  let lastBoundEvent: Event0 | undefined = undefined;
  createEffect(()=>{
    if(lastBoundEvent != props.onClosingEvent) {
      lastBoundEvent = props.onClosingEvent;
      lastBoundEvent?.registerOne(this, ()=>{
        onClosing();
      });
    }
  })

  let didChange = false;
  function onFieldChanged(setting: ISettingsField, val: any) {
    console.log("Setting [" + setting.title + "] changed", val);
    didChange = true;
  }
  function onClosing(){
    console.log("Settings closing");
    if(didChange){
      console.log("Settings changed before close, saving");
      SettingsBackend.settingsSave(settings$()?.object)
    }
  }

  return (
    <div class={styles.container} style="height: calc(100% - 20px); overflow-y: hidden;">
      <div class={styles.settingsMenu} style={props.settingsMenuStyle}>
          <h1 style="flex-shrink: 0">Settings</h1>
            <ScrollContainer wrapperStyle={{"padding-right": "4px"}}>
              <div classList={{[styles.settingsMenuItem]: true, [styles.active]: !filterGroup$()}} onClick={()=>setFilterGroup(undefined)} use:focusable={{
                onPress: () => setFilterGroup(undefined)
              }}>
                All
              </div>
              <For each={settings$()?.fields?.filter(x=>x.type == 'group') ?? []}>{item => 
                <div classList={{[styles.settingsMenuItem]: true, [styles.active]: item.property == filterGroup$()}} onClick={()=>setFilterGroup(item.property)} use:focusable={{
                  onPress: () => setFilterGroup(item.property)
                }}>
                  {item.title}
                </div>
              }</For>
            </ScrollContainer>
            <div class={styles.bottomMenu}>
              <div style="margin-top: 10px; margin-right: 20px;">
                <Button onClick={()=>{UIOverlay.dismiss(); UIOverlay.overlayImportSelect()}} style={{width: '100%'}} text='Import' icon='' focusableOpts={{
                  onPress: () => { UIOverlay.dismiss(); UIOverlay.overlayImportSelect() }
                }}></Button>
              </div>
              <Show when={false}>
                <div style="margin-top: 10px; margin-right: 20px;">
                  <Button onClick={()=>Backend.GET('/Dialog/Test')} style={{width: '100%'}} text='Test' icon=''></Button>
                </div>
              </Show>
            </div>
      </div>
      <div class={styles.settingsContainer} style={props.settingsContainerStyle}>
        <ScrollContainer>
          <SettingsContainer settings={settings$()} filterGroup={filterGroup$()} onFieldChanged={onFieldChanged} />
        </ScrollContainer>
      </div>
    </div>
  );
};

export default SettingsPage;
