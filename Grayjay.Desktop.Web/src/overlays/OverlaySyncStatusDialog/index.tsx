
import { Component, Match, Switch } from 'solid-js';
import UIOverlay from '../../state/UIOverlay';
import OverlayCustomDialog from '../OverlayCustomDialog';
import { CustomDialogLocal } from '../OverlayRoot';
import Button from '../../components/buttons/Button';
import LoaderSmall from '../../components/basics/loaders/LoaderSmall';

import iconSuccess from "../../assets/icons/ic_success_small.svg"
import iconError from "../../assets/icons/icon_error.svg"

export interface OverlaySyncStatusDialogProps {
  dialog: CustomDialogLocal
};
const OverlaySyncStatusDialog: Component<OverlaySyncStatusDialogProps> = (props: OverlaySyncStatusDialogProps) => {
  return (
    <OverlayCustomDialog hideHeader={true}>
      <Switch>
      <Match when={props.dialog.data$().Status == 'pairing'}>
          <div style="text-align: center;">
            <div style="width: 100%; display: flex; flex-direction: row; align-items: center; justify-content: center;">
              <LoaderSmall />
            </div>
            <h2>{props.dialog.data$().Message}</h2>
            <div>
              <Button text='Close' onClick={() => UIOverlay.dismiss()}></Button>
            </div>
          </div>
        </Match>
        <Match when={props.dialog.data$().Status == 'success'}>
          <div style="text-align: center;">
            <div style="width: 100%; display: flex; flex-direction: row; align-items: center; justify-content: center;">
              <img src={iconSuccess} style="width: 100px" />
            </div>
            <h2>Success!</h2>
            <div>
              <Button text='Close' onClick={() => UIOverlay.dismiss()}></Button>
            </div>
          </div>
        </Match>
        <Match when={props.dialog.data$().Status == 'error'}>
          <div style="text-align: center;">
            <div style="width: 100%; display: flex; flex-direction: row; align-items: center; justify-content: center;">
              <img src={iconError} style="width: 100px" />
            </div>
            <h2>{props.dialog.data$().Message}</h2>
            <div>
              <Button text='Close' onClick={() => UIOverlay.dismiss()}></Button>
            </div>
          </div>
        </Match>
      </Switch>
    </OverlayCustomDialog>
  );
};

export default OverlaySyncStatusDialog;