
import { Component, For, Match, Show, Switch, batch, createEffect, createMemo, createResource, createSignal, onCleanup, onMount } from 'solid-js';
import styles from './index.module.css';
import chevron_right from '../../../../assets/icons/icon_chevron_right.svg';
import UIOverlay from '../../state/UIOverlay';
import { Event0, Event1 } from '../../utility/Event'
import InputText from '../../components/basics/inputs/InputText';
import Dropdown from '../../components/basics/inputs/Dropdown';
import OverlayCustomDialog from '../OverlayCustomDialog';
import iconWebBlue from "../../assets/icons/icon_link_blue.svg"
import iconLink from "../../assets/icons/icon_link.svg"
import iconError from "../../assets/icons/icon_error_warning.svg"
import IPluginPrompt from '../../backend/models/plugin/IPluginPrompt';
import { focusScope } from '../../focusScope'; void focusScope;
import { focusable } from '../../focusable'; void focusable;
import SettingsPage from '../../pages/Settings';


export interface OverlaySettingsProps {
};
const OverlaySettings: Component<OverlaySettingsProps> = (props: OverlaySettingsProps) => {

    const onCloseEvent = new Event0();

    function onClosed() {
      onCloseEvent?.invoke();
      console.log("Settings closed");
    }

    return (
      <OverlayCustomDialog onRootClick={()=>{onClosed(); UIOverlay.dismiss();}} onCloseClick={()=>UIOverlay.dismiss()}>
        <div style="margin-top: -90px; width: 800px; height: 80vh; max-height: 700px" onClick={(ev)=>ev.stopPropagation()} onMouseDown={(ev) => ev.stopPropagation()} use:focusScope={{
          initialMode: 'trap'
        }}>
          <SettingsPage settingsContainerStyle={{top: '80px', height: 'calc(100% - 80px)'}} onClosingEvent={onCloseEvent} />
        </div>
      </OverlayCustomDialog>
    );
  };
  
  export default OverlaySettings;