
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

export interface OverlayImportDialogProps {
  dialog: CustomDialogLocal
};
const OverlayImportDialog: Component<OverlayImportDialogProps> = (props: OverlayImportDialogProps) => {

    const importOptions: { name: any; description: string; icon: any; id: any; value: boolean; }[] = [];
    if(props.dialog.data$().Settings)
      importOptions.push({
        name: "App Settings",
        description: "General settings that configure how your app works",
        icon: iconGear,
        id: "settings",
        value: true
      });
    if(props.dialog.data$().Plugins && props.dialog.data$().Plugins.Count > 0)
      importOptions.push({
        name: "Plugins",
        description: "Should plugins should be installed (" + props.dialog.data$().Plugins.Count + " items)",
        icon: iconSources,
        id: "plugins",
        value: true
      });
    if(props.dialog.data$().PluginSettings && props.dialog.data$().PluginSettings.Count > 0)
      importOptions.push({
        name: "Plugin Settings",
        description: "Should plugin settings should be imported (" + props.dialog.data$().PluginSettings.Count + " items)",
        icon: iconPluginSettings,
        id: "pluginSettings",
        value: true
      });
    for(let store of props.dialog.data$().Stores) {
      if(store.Count && store.Count > 0)
        importOptions.push({
          name: store.Name,
          description: store.Count + " items",
          icon: store.Icon ?? iconLink,
          id: store.ID,
          value: true
        });
    }
    function startImport(){
        props.dialog.action!('choice', 'import;' + importOptions?.filter(y=>!!y.value).map(x=>x.id).join(","));
    }

    const importStatus = createMemo(()=>{
      const values = props.dialog.data$().Stores;
      if(props.dialog.data$().Settings)
        values.unshift(props.dialog.data$().Settings);
      if(props.dialog.data$().Plugins)
        values.unshift(props.dialog.data$().Plugins);
      if(props.dialog.data$().PluginSettings)
        values.unshift(props.dialog.data$().PluginSettings);
    });

    const [shownExceptions$, setShownException] = createSignal();

    return (
      <OverlayCustomDialog hideHeader={true} hideDialog={props.dialog.data$().Status == "importing" || props.dialog.data$().Status == "finished"}
              onRootClick={(ev)=> props.dialog.data$().Status == "importing" && ev.stopPropagation()}>
        
            <Switch>
              <Match when={props.dialog.data$().Status == 'choice'}>
              <div style="width: 500px" onClick={(ev)=>ev.stopPropagation()}>
                <div style="text-align: center;">
                  <div class={styles.dialogTitle}>Import</div>
                  <div class={styles.dialogSubtitle}>
                    Choose the data you would like to import
                  </div>
                      <div>
                        <For each={importOptions}>{(item) => 
                          <div class={styles.importItem}>
                            <div class={styles.icon}>
                              <img src={item.icon} />
                            </div>
                            <div class={styles.text}>
                              <div class={styles.name}>
                                {item.name}
                              </div>
                              <div class={styles.description}>
                                {item.description}
                              </div>
                              <div class={styles.toggle}>
                                <Toggle value={item.value} onToggle={(x)=>item.value = x} />
                              </div>
                            </div>
                          </div>
                        }</For>
                        
                        <div style="text-align: center; margin-top: 30px;">
                          <Button text='Cancel' onClick={()=>props.dialog.action!('choice', 'cancel')} style={{"margin-left": "10px"}}></Button>
                          <Button text='Import' onClick={()=>startImport()} style={{"margin-left": "10px"}}></Button>
                        </div>
                      </div>
                    </div>
                  </div>
              </Match>
              <Match when={props.dialog.data$().Status == 'enablePlugins'}>
                  <div style="width: 500px" onClick={(ev)=>ev.stopPropagation()}>
                    <div style="text-align: center;">
                      <div>
                        <img src={iconSources} style="width: 100px; margin-left: auto; margin-right: auto; margin-bottom: 30px;" />
                      </div>
                      <div class={styles.dialogTitle} style="text-align: center;">Enable all plugins</div>
                      <div class={styles.dialogSubtitle} style="text-align: center;">
                        Enabling all plugins ensures all required plugins are available during import
                      </div>
                      <div>
                        <div style="text-align: center; margin-top: 30px;">
                          <Button text='No' onClick={()=>props.dialog.action!('import', 'false')} style={{"margin-left": "10px"}}></Button>
                          <Button text='Yes' onClick={()=>props.dialog.action!('import', 'true')} color='#019BE7' style={{"margin-left": "10px"}}></Button>
                        </div>
                      </div>
                    </div>
                  </div>
              </Match>

              <Match when={props.dialog.data$().Status == 'importing'}>
                <div onClick={(ev)=>ev.stopPropagation()}>
                  <div style="text-align: center; width: 350px;">
                    <LoaderSmall style={{"margin-right": "auto", "margin-left": "auto", "width": "100px"}} />
                    <div class={styles.importTitle} style="margin-top: 30px;">
                      {props.dialog.data$().ImportStatus}
                    </div>
                    <div>
                      <For each={props.dialog.data$().Importing}>{(item) =>
                        <div class={styles.importStatus}>
                          <div class={styles.icon}>
                            <img src={item.Icon} />
                          </div>
                          <div class={styles.name}>
                            {item.Name}
                          </div>
                          <div class={styles.status}>
                            <div>
                              <Show when={!item.Importing && !item.Imported}>
                                Queued
                              </Show>
                              <Show when={item.Importing && !item.Imported}>
                                <LoaderSmall style={{width: "12px", display: "inline-block", "margin-right": "10px", "vertical-align": "middle"}}></LoaderSmall>
                                Importing
                              </Show>
                              <Show when={item.Imported && item.Exceptions?.length == 0}>
                                <img src={iconSuccess} />
                                Imported
                              </Show>
                              <Show when={item.Imported && item.Exceptions && item.Exceptions.length > 0}>
                                <img src={iconFail} />
                                Failed
                              </Show>
                            </div>
                          </div>
                        </div>
                      }</For>
                    </div>
                  </div>
                </div>
              </Match>

              <Match when={props.dialog.data$().Status == 'finished'}>
                <div>
                <div style="text-align: center; width: 350px;" onClick={(ev)=>ev.stopPropagation()}>
                    <Switch>
                      <Match when={props.dialog.data$().Importing.find(y=>y.Exceptions.length == 0)}>
                        <div>
                          <img src={iconSuccess} style="width: 150px; margin-left: auto; margin-right: auto;" />
                        </div>
                      </Match>
                      <Match when={props.dialog.data$().Importing.find(y=>y.Exceptions.length > 0)}>
                        <div>
                          <img src={iconFail} style="width: 150px; margin-left: auto; margin-right: auto;" />
                        </div>
                      </Match>
                    </Switch>
                    <div>
                      <For each={props.dialog.data$().Importing}>{(item) =>
                        <div>
                          <div class={styles.importStatus}>
                            <div class={styles.icon}>
                              <img src={item.Icon} />
                            </div>
                            <div class={styles.name}>
                              {item.Name}
                            </div>
                            <div class={styles.status}>
                              <Show when={item.Exceptions.length == 0 && item.Warnings.length == 0}>
                                <div class={styles.info}>
                                  {item.Count} item(s)
                                </div>
                              </Show>
                              <Show when={item.Exceptions.length > 0 || item.Warnings.length > 0}>
                                <div onClick={()=>(shownExceptions$() != item) ? setShownException(item) : setShownException(null)} style="cursor: pointer;">
                                  <Show when={item.Exceptions.length > 0}>
                                    <span style="margin: 2px; color: red;">
                                      {item.Exceptions.length} error(s)
                                    </span>
                                  </Show>
                                  <Show when={item.Warnings.length > 0}>
                                    <span style="margin: 2px; color: orange;">
                                      {item.Warnings.length} warning(s)
                                    </span>
                                  </Show>
                                  <Show when={shownExceptions$() != item}>
                                    <img src={iconChevDown} style="margin-left: 5px;" />
                                  </Show>
                                  <Show when={shownExceptions$() == item}>
                                    <img src={iconChevUp} style="margin-left: 5px" />
                                  </Show>
                                </div>
                              </Show>
                            </div>
                          </div>
                          <Show when={shownExceptions$() == item}>
                            <div>
                              <For each={item.Exceptions}>{(ex)=>
                                <div class={styles.exception}>
                                  {ex}
                                </div>
                              }</For>
                              <For each={item.Warnings}>{(warn)=>
                                <div class={styles.warning}>
                                  {warn}
                                </div>
                              }</For>
                            </div>
                          </Show>
                        </div>
                      }</For>
                    </div>
                  </div>
                  <div style="text-align: center;">
                    <Button text='Ok' onClick={()=>UIOverlay.dismiss()} style={{"margin-top": "10px"}}></Button>
                  </div>
                </div>
              </Match>

            </Switch>
      </OverlayCustomDialog>
    );
  };
  
  export default OverlayImportDialog;