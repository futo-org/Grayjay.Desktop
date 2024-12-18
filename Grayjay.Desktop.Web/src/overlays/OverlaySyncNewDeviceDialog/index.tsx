
import { Component, createEffect, createMemo, createResource, createSignal } from 'solid-js';
import styles from './index.module.css';
import iconClose from '../../assets/icons/icon24_close.svg';
import UIOverlay from '../../state/UIOverlay';
import InputTextArea from '../../components/basics/inputs/InputTextArea';
import Button from '../../components/buttons/Button';
import { SyncBackend } from '../../backend/SyncBackend';
import { createResourceDefault } from '../../utility';

export interface OverlaySyncNewDeviceDialogProps {

};
const OverlaySyncNewDeviceDialog: Component<OverlaySyncNewDeviceDialogProps> = (props: OverlaySyncNewDeviceDialogProps) => {
  const [deviceInfo$, setDeviceInfo] = createSignal<string>();

  const [isDeviceInfoValid$, _] = createResourceDefault(deviceInfo$(), (v) => {
    if (!v || v.length < 1) {
      return false;
    }

    try {
      SyncBackend.validateSyncDeviceInfoFormat(JSON.stringify(v));
      return true;
    } catch {
      return false;
    }
  });

  const errorMessage$ = createMemo(() => {
    return deviceInfo$() && deviceInfo$()?.length && !isDeviceInfoValid$() ? "Device information is not valid" : undefined;
  });
  
  return (
    <div class={styles.container}>
      <div class={styles.dialogHeader} style={{"margin-left": "0px"}}>
        <div class={styles.headerText}>
          Link new device
        </div>
        <div class={styles.headerSubText}>
          Link new device by pasting the code here
        </div>
        <div class={styles.closeButton} onClick={() => UIOverlay.dismiss()}>
          <img src={iconClose} />
        </div>
      </div>
      <div>
        <InputTextArea label="Paste device info here" small={true} style={{ "width": "100%" }} value={deviceInfo$()} onTextChanged={(v) => setDeviceInfo(v)} error={errorMessage$()} />
      </div>
      <div style="text-align: right; margin-top: 12px;">
        <Button text={"Cancel"}
          onClick={() => {
            UIOverlay.dismiss();
          }}
          style={{"margin-left": "auto", cursor: ("pointer")}}  />
        <Button text={"Link device"}
          onClick={async () => {
            if (deviceInfo$() && deviceInfo$()?.length && isDeviceInfoValid$()) {
              UIOverlay.dismiss();
              await SyncBackend.addDevice(deviceInfo$());
            }
          }}
          style={{"margin-left": "10px", cursor: ("pointer")}} 
          color={deviceInfo$() && deviceInfo$()?.length && isDeviceInfoValid$() ? "linear-gradient(267deg, #01D6E6 -100.57%, #0182E7 90.96%)" : undefined} />
      </div>
    </div>
  );
};

export default OverlaySyncNewDeviceDialog;