
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


export interface OverlaySourceInstallProps {
  prompt: IPluginPrompt,
  onInstall: (config: ISourceConfig)=>void
};
const OverlayDialog: Component<OverlaySourceInstallProps> = (props: OverlaySourceInstallProps) => {

    return (
      <OverlayCustomDialog onRootClick={()=>{UIOverlay.dismiss();}} onCloseClick={()=>UIOverlay.dismiss()}>
        <div style="margin-top: -90px; width: 750px" onClick={(ev)=>ev.stopPropagation()} onMouseDown={(ev) => ev.stopPropagation()}>
          <div class={styles.header}>
            <div class={styles.icon}>
              <img src={props.prompt.config?.absoluteIconUrl} />
            </div>
            <div class={styles.descriptor}>
              <div class={styles.title}>
                {props.prompt.config.name}
              </div>
              <div class={styles.description}>
                {props.prompt.config.description}
              </div>
              <div class={styles.meta}>
                Version {props.prompt.config.version} • by <a href="">{props.prompt.config.author}</a>
              </div>
            </div>
          </div>
          <div class={styles.linkContainer}>
            <For each={[
              { title: "Config", url: props.prompt.config.sourceUrl },
              { title: "Repository", url: props.prompt.config.scriptUrl },
              props.prompt.config.repositoryUrl ? { title: "Repository", url: props.prompt.config.repositoryUrl } : null,
              props.prompt.config.platformUrl ? { title: "Platform", url: props.prompt.config.platformUrl } : null
            ].filter(x=>x)}>{ item =>
              <div class={styles.link}>
                <img class={styles.linkIcon} src={iconWebBlue} />
                <div class={styles.linkTexts}>
                  <div class={styles.linkTitle}>
                    {item.title}
                  </div>
                  <div class={styles.linkUrl}>
                    {item.url}
                  </div>
                </div>
              </div>
            }</For>
          </div>
          <div class={styles.permissionContainer}>
            <div class={styles.permissionHeader}>
              <div class={styles.headerTitle}>
                Permissions
              </div>
              <div class={styles.headerDescription}>
                These are the permissions the plugin requires to function
              </div>
            </div>
            <div class={styles.permission}>
              <div class={styles.permissionDescriptor}>
                <img class={styles.permissionIcon} src={iconLink} />
                <div class={styles.permissionTexts}>
                  <div class={styles.permissionTitle}>
                    Web Access
                  </div>
                  <div class={styles.permissionDescription}>
                    The plugin will have access to the following domains.
                  </div>
                </div>
              </div>
              <div class={styles.permissionValue}>
                <For each={props.prompt.config.allowUrls}>{ url =>
                  <div class={styles.tag}>
                    {url}
                  </div>
                }</For>
              </div>
            </div>
          </div>
          <Show when={props.prompt.warnings && props.prompt.warnings.length > 0}>
            <div class={styles.permissionContainer}>
                <div class={styles.permissionHeader}>
                  <div class={styles.headerTitle}>
                    Security Warnings
                  </div>
                  <div class={styles.headerDescription}>
                    Before installing this plugin, take note of the security alerts provided below and consider them carefully.
                  </div>
                </div>
                <For each={props.prompt.warnings}>{ warning =>
                  <div class={styles.warning}>
                    <div class={styles.warningIconContainer}>
                      <img class={styles.warningIcon} src={iconError} />
                    </div>
                    <div class={styles.warningTexts}>
                      <div class={styles.warningTitle}>
                        {warning.title}
                      </div>
                      <div class={styles.warningDescription}>
                        {warning.description}
                      </div>
                    </div>
                  </div>
                }</For>
            </div>
          </Show>
          <div class={styles.footer}>
              <div class={styles.install} onClick={()=>{UIOverlay.dismiss(); props.onInstall(props.prompt.config);}}>
                Install
              </div>
          </div>
        </div>
      </OverlayCustomDialog>
    );
  };
  
  export default OverlayDialog;