
import { Component } from 'solid-js';
import UIOverlay from '../../state/UIOverlay';
import OverlayCustomDialog from '../OverlayCustomDialog';
import { CustomDialogLocal } from '../OverlayRoot';
import Button from '../../components/buttons/Button';

import iconDeviceUnknown from "../../assets/icons/ic_device_unknown.svg"

export interface OverlaySyncConfirmDialogProps {
  dialog: CustomDialogLocal
};
const OverlaySyncConfirmDialog: Component<OverlaySyncConfirmDialogProps> = (props: OverlaySyncConfirmDialogProps) => {
  function confirm() {
    props.dialog.action!('confirm', '');
  }

  function cancel() {
    props.dialog.action!('cancel', '');
  }

  return (
    <OverlayCustomDialog hideHeader={true} onRootClick={() => cancel()}>
      <div style="text-align: center;">
        <div>
          <img src={iconDeviceUnknown} style="width: 100px" />
        </div>
        <h2>Synchronisation Pairing Request</h2>
        <div>Allow {props.dialog.data$().PublicKey} to synchronize your data?</div>
        <div style="text-align: right; margin-top: 12px;">
          <Button text='Cancel' onClick={(ev) => {
            cancel();
            ev.stopPropagation();
          }} style={{"margin-right": "8px"}}></Button>
          <Button text='Confirm' onClick={(ev) => {
            confirm();
            ev.stopPropagation();
          }}></Button>
        </div>
      </div>
    </OverlayCustomDialog>
  );
};

export default OverlaySyncConfirmDialog;