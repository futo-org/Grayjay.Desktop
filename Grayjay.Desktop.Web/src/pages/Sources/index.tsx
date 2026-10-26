import { For, type Component, createResource, createSignal, createEffect, Show, createMemo } from 'solid-js';
import styles from './index.module.css';
import { SourcesBackend } from '../../backend/SourcesBackend';
import { Backend } from '../../backend/Backend';
import StateGlobal from '../../state/StateGlobal';
import iconThumb from '../../assets/icons/icon_thumb.svg'
import iconChevRight from '../../assets/icons/icon_chevron_right.svg'
import iconLink from '../../assets/icons/icon_link.svg'
import iconSources from '../../assets/icons/ic_circles.svg'
import iconGrayjay from '../../assets/grayjay.svg'
import SourceDetails from '../subpages/SourceDetails';
import Toggle from '../../components/basics/inputs/Toggle';
import UIOverlay from '../../state/UIOverlay';
import OverlaySourceInstall from '../../overlays/OverlaySourceInstall';
import { DialogButton, DialogDescriptor, DialogInputText, IDialogOutput } from '../../overlays/OverlayDialog';
import { Event0 } from '../../utility/Event';
import ExceptionModel from '../../backend/exceptions/ExceptionModel';

import iconCodeEditBlue from "../../assets/icons/icon_code_edit_blue.svg"
import iconError from "../../assets/icons/icon_error.svg"
import iconCheck from "../../assets/icons/icon_checkmark.svg"
import iconWarning from "../../assets/icons/icon_warning.svg"
import { useNavigate } from '@solidjs/router';
import StateWebsocket from '../../state/StateWebsocket';
import { ISourceConfig } from '../../backend/models/plugin/ISourceConfigState';
import EmptyContentView from '../../components/EmptyContentView';
import ScrollContainer from '../../components/containers/ScrollContainer';
import { createResourceDefault, swap } from '../../utility';
import VirtualDragDropList, { DragSession } from '../../components/containers/VirtualDragDropList';
import { focusable } from '../../focusable';import Button from '../../components/buttons/Button';
 void focusable;

const SourcesPage: Component = () => {
  const nav = useNavigate();
  
  const [enabledSources$, eSourcesRes] = createResourceDefault(async () => [], async () => await SourcesBackend.enabledSources());
  const [disabledSources$, dSourcesRes] = createResourceDefault(async () => [], async () => await SourcesBackend.disabledSources());

  const [selectedSignal$, setSelectedSignal] = createSignal("");

  StateWebsocket.registerHandlerNew("PluginAvailable", (packet)=>{
    eSourcesRes.refetch();
    dSourcesRes.refetch();
  }, "sources");
  StateWebsocket.registerHandlerNew("PluginEnabled", (packet)=>{
    eSourcesRes.refetch();
    dSourcesRes.refetch();
  }, "sources");

  createEffect(()=>{
      const currentResources = enabledSources$();
      if(currentResources && currentResources.length > 0 && selectedSignal$() == "")
        setSelectedSignal(currentResources[0].id);
  });

  async function enableSource(source: ISourceConfig) {
    const enabled = await UIOverlay.catchDialogExceptions(async ()=>{
    
                    return await SourcesBackend.enableSource(source.id);
    }, ()=>{
      eSourcesRes.refetch();
    }, ()=>{
      enableSource(source);
    });
    eSourcesRes.refetch();
    dSourcesRes.refetch();
  }
  async function disableSource(source: ISourceConfig) {
    const disabled = await SourcesBackend.disableSource(source.id);
    eSourcesRes.refetch();
    dSourcesRes.refetch();
  }

  async function installSource() {
    console.info("installSource called");


    const urlPrompt = await UIOverlay.dialog({
      icon: iconLink,
      title: "Install from URL",
      description: "Install a source by pasting the URL in the box below.",
      buttons: [
        { 
          title: "Cancel", 
          style: "none", 
          onClick: (output: IDialogOutput)=>{}
        },
        { 
          title: "Continue", 
          style: "primary", 
          onClick: async (output: IDialogOutput)=>{
            if(output.text) {



            }
          }
        }],
      input: new DialogInputText("Config Url")
    });
    if(urlPrompt?.button == 1 && urlPrompt.text) {
      UIOverlay.installPluginPrompt(urlPrompt.text);
    }
  }

  function selectSource(source: ISourceConfig) {
      setSelectedSignal(source.id);
  }

  let dragSession: DragSession | undefined;
  let scrollContainerRef: HTMLDivElement | undefined;

  const enabledCount = createMemo(() => enabledSources$()?.length ?? 0);
  const disabledCount = createMemo(() => disabledSources$()?.length ?? 0);
  const actionsBaseIndex = createMemo(() => enabledCount() + disabledCount());

  return (
    <div style="height: 100%; overflow: hidden; display: flex; flex-direction: column;">
      <Show when={enabledSources$() && disabledSources$() && (enabledSources$()!.length + disabledSources$()!.length > 0)}>
        <div style="flex-shrink: 0">
          <div class={styles.pageTitle}>Manage sources</div>
        </div>
        <div style="flex-grow: 1; position: relative; display: flex; overflow: hidden;">
          <div class={styles.panelLeft}>
            <ScrollContainer ref={scrollContainerRef}>
              <div>
                <VirtualDragDropList items={enabledSources$()}
                  itemHeight={88}
                  onSwap={(index1, index2) => {
                    swap(enabledSources$()!, index1, index2);
                    SourcesBackend.sourcesReorder(enabledSources$()?.map(v => v.id) ?? [])
                  }}
                  builder={(index, item, containerRef, dragControls) => {
                      const source = createMemo(() => item() as ISourceConfig | undefined);
                      return (
                        <Show when={source()}>
                          <div class={styles.source} classList={{[styles.enabled]: source()!.id == selectedSignal$()}} onClick={()=>selectSource(source()!)} onFocus={() => selectSource(source()!)} use:focusable={{
                            groupId: 'sources',
                            groupType: 'vertical',
                            groupIndices: [index()],
                            groupRememberLast: true,
                            onPress: () => disableSource(source()!),
                            onAction: () => {
                              if (!dragSession) {
                                dragSession = dragControls.startProgrammaticDrag();
                              } else {
                                dragSession.end();
                                dragSession = undefined;
                              }
                            },
                            onActionLabel: "Reorder",
                            onDirection: (el, dir) => {
                              if (!dragSession || !dragSession.isActive()) return false;
                              if (dir === "up") {
                                dragSession.moveBy(-1);
                                return true;
                              }
                              else if (dir === "down") {
                                dragSession.moveBy(1);
                                return true;
                              }
                            }
                          }}>
                            <div class={styles.thumb}  onMouseDown={(e) => {
                              dragControls.startPointerDrag?.(e.pageY, containerRef!.getBoundingClientRect().top, e.target as HTMLElement);
                              e.preventDefault();
                              e.stopPropagation();
                            }}>
                              <img src={iconThumb} />
                            </div>
                               <div class={styles.image}>
                                 {/* Try dynamic icon from plugin.getIcon(), fallback to static absoluteIconUrl (derived from JSON iconUrl) */}
                                 <img src={StateGlobal.getDynamicIcon(source()!.id).then(icon => icon ?? StateGlobal.getSourceConfig(source()!.id)?.absoluteIconUrl)} />
                               </div>

                            <div class={styles.name}>
                              {source()!.name}
                            </div>
                            <div class={styles.actions}>
                              <Toggle onToggle={() => disableSource(source()!)} value={true} adjustInternally={false} />
                              <img class={styles.chev} src={iconChevRight} />
                            </div>
                          </div>
                        </Show>

                      );
                  }}
                  outerContainerRef={scrollContainerRef} />
              </div>
              <div>
              <Show when={(disabledSources$()?.length ?? 0) > 0}>
                <h3 style="margin-left: 24px;">Disabled</h3>
              </Show>
                <For each={disabledSources$()}>
                  {(source, i) =>
                    <div class={styles.source} classList={{[styles.enabled]: source.id == selectedSignal$()}} onClick={()=>selectSource(source)} onFocus={() => selectSource(source)} use:focusable={{
                      groupId: "sources",
                      groupType: "vertical",
                      groupIndices: [enabledCount() + i()],
                      groupRememberLast: true,
                      onPress: () => enableSource(source)
                    }}>
                       <div class={styles.image}>
                         {/* Try dynamic icon from plugin.getIcon(), fallback to static absoluteIconUrl (derived from JSON iconUrl) */}
                         <img src={StateGlobal.getDynamicIcon(source.id).then(icon => icon ?? StateGlobal.getSourceConfig(source.id)?.absoluteIconUrl)} />
                       </div>

                      <div class={styles.name}>
                        {source.name}
                      </div>
                      <div class={styles.actions} >
                        <Toggle onToggle={() => enableSource(source)} value={false} adjustInternally={false} />
                        <img class={styles.chev} src={iconChevRight} />
                      </div>
                    </div>
                  }
                </For>
              </div>
              <div
                style={{
                  "margin-top": "24px",
                  "margin-bottom": "24px",
                  "padding-left": "24px",
                  "padding-right": "24px",
                  display: "flex",
                  "flex-direction": "column",
                  gap: "10px",
                }}
              >
                <Button
                  text="Install Source"
                  color="#019BE7"
                  style={{
                    width: "100%",
                    height: "56px",
                    padding: "18px",
                    display: "flex",
                    "justify-content": "center",
                    "align-items": "center",
                  }}
                  onClick={() => installSource()}
                  focusableOpts={{
                    groupId: "sources",
                    groupType: "vertical",
                    groupIndices: [actionsBaseIndex()],
                    groupRememberLast: true,
                    onPress: installSource,
                  }}
                />

                <Button
                  text="Install Official Sources"
                  color="#019BE7"
                  style={{
                    width: "100%",
                    height: "56px",
                    padding: "18px",
                    display: "flex",
                    "justify-content": "center",
                    "align-items": "center",
                  }}
                  onClick={() => UIOverlay.overlayOfficialPlugins()}
                  focusableOpts={{
                    groupId: "sources",
                    groupType: "vertical",
                    groupIndices: [actionsBaseIndex() + 1],
                    groupRememberLast: true,
                    onPress: () => UIOverlay.overlayOfficialPlugins(),
                  }}
                />
              </div>

            </ScrollContainer>
          </div>
          <div class={styles.panelRight}>
            <SourceDetails configId={selectedSignal$()} />
          </div>
        </div>
      </Show>
      <Show when={enabledSources$() && disabledSources$() && (enabledSources$()!.length + disabledSources$()!.length == 0)}>
        <EmptyContentView icon={iconSources} title='You have no sources' description='Please install some sources to use Grayjay.' actions={[
            {
              icon: iconGrayjay,
              title: "Install Official Sources",
              action: ()=>{
                UIOverlay.overlayOfficialPlugins();
              }
            },
            {
              icon: iconSources,
              title: "Install Other Source",
              action: ()=>installSource()
            }
        ]} />
      </Show>
    </div>
  );
};

export default SourcesPage;
