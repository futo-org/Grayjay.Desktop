
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


export interface OverlayImportDialogProps {
  dialog: CustomDialogLocal
};
const OverlayImportDialog: Component<OverlayImportDialogProps> = (props: OverlayImportDialogProps) => {

    return (
      <OverlayCustomDialog hideHeader={true}>
        <div style="width: 750px" onClick={(ev)=>ev.stopPropagation()}>
          <div style="text-align: center;">
            <h2 style="text-align: center;">Import [{props.dialog.data$().storeName}]</h2>
            <Switch>

              <Match when={props.dialog.data$().status == 'choice'}>
                <div>
                  <div style="margin: 10px;">
                    <p>
                      Do you want to import this store?
                    </p>
                    <p style="color: red;">
                      Sources required need to be enabled
                    </p>
                  </div>
                  <div style="text-align: center;">
                    <Button text='Cancel' onClick={()=>props.dialog.action!('choice', 'cancel')} style={{"margin-left": "10px"}}></Button>
                    <Button text='Import' onClick={()=>props.dialog.action!('choice', 'import')} style={{"margin-left": "10px"}}></Button>
                  </div>
                </div>
              </Match>

              <Match when={props.dialog.data$().status == 'importing'}>
                <div>
                  <div style="text-align: center">
                    <LoaderSmall style={{"margin-right": "auto", "margin-left": "auto"}} />
                    <p>{props.dialog?.data$()?.progress}/{props.dialog?.data$()?.total}</p>
                  </div>
                </div>
              </Match>

              <Match when={props.dialog.data$().status == 'finished'}>
                <div>
                  <h2>Finished</h2>
                  <div>
                    {props.dialog?.data$()?.messages}
                  </div>
                  <div>
                    <Button text='Close' onClick={()=>UIOverlay.dismiss()}></Button>
                  </div>
                </div>
              </Match>

            </Switch>
          </div>
        </div>
      </OverlayCustomDialog>
    );
  };
  
  export default OverlayImportDialog;