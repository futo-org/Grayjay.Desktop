import { createResource, createSignal, For, onCleanup, onMount, Show, type Component } from 'solid-js';
import { SyncBackend } from '../../backend/SyncBackend';
import QRCode from 'qrcode';
import styles from './index.module.css';
import UIOverlay from '../../state/UIOverlay';
import ButtonFlex from '../../components/buttons/ButtonFlex';
import ScrollContainer from '../../components/containers/ScrollContainer';
import iconAdd from '../../assets/icons/icon24_add.svg';
import iconQr from '../../assets/icons/ic_qr.svg';
import iconDevice from '../../assets/icons/ic_device.svg';
import iconLocal from '../../assets/icons/ic_local.svg';
import iconInternet from '../../assets/icons/ic_internet.svg';
import iconOffline from '../../assets/icons/ic_offline.svg';
import iconClear from '../../assets/icons/close_FILL0_wght300_GRAD0_opsz24.svg';
import StateWebsocket from '../../state/StateWebsocket';
import NavigationBar from '../../components/topbars/NavigationBar';
import { createResourceDefault } from '../../utility';

const SyncPage: Component = () => {
  const [devices$, devicesActions] = createResourceDefault(SyncBackend.getDevices);  
  const renderDevice = (publicKey: string, title: string, subtitle: string, linkType: number) => {
    let icon: string;
    let status: string;
    switch (linkType) {
      default:
        icon = iconOffline;
        status = "Offline";
        break;
      case 1:
        icon = iconLocal;
        status = "Local";
        break;
      case 2:
        icon = iconInternet;
        status = "Proxied";
        break;
    }
  
    return (<div style="display: flex; flex-direction: row; border-radius: 6px; background: #1B1B1B; padding: 14px 18px 14px; gap: 12px;  margin-left: 24px; margin-right: 24px; align-items: center;">
      <img src={iconDevice} style="width: 44px;" />
      <div style="display: flex; flex-direction: column; flex-grow: 1; align-items: flex-start; justify-content: center;">
          <div style="overflow: hidden; color: white; text-align: center; text-overflow: ellipsis; font-family: Inter; font-size: 14px; font-style: normal; font-weight: 500; line-height: normal;">{title}</div>
          <div style="color: #595959; font-family: Inter; font-size: 10px; font-style: normal; font-weight: 500; line-height: normal;">{subtitle}</div>
      </div>
  
      <div style="border-radius: 4px; border: 1px solid #2E2E2E; display: flex; padding: 6px 8px; align-items: center; gap: 4px; flex-shrink: 0; height: fit-content;">
        <img src={icon} style="width: 20x; cursor: pointer;" />
        <div style="color: #BFBFBF; font-family: Inter; font-size: 10px; font-style: normal; font-weight: 500; line-height: normal;">{status}</div>
      </div>
      <img src={iconClear} style="width: 30px; cursor: pointer; flex-shrink: 0;" onClick={async () => await SyncBackend.removeDevice(publicKey)} />
    </div>);
  };

  const [isQrVisible$, setIsQrVisible] = createSignal(false);
  const [pairingUrl$] = createResourceDefault(SyncBackend.getPairingUrl);
  const [qrCode$] = createResourceDefault(pairingUrl$, async (pairingUrl) => {
    if (!pairingUrl || pairingUrl.length < 1) {
      return undefined;
    }

    return await QRCode.toDataURL(pairingUrl);
  });
  
  const renderQrCodeOverlay = () => {
    return (
      <ScrollContainer style={{
        "display": "flex",
        "justify-content": "center",
        "flex-grow": 1,
        "align-items": "flex-start"
      }}>
        <div class={styles.container}>
          <div class={styles.dialogHeader} style={{"margin-left": "0px"}}>
            <div class={styles.headerText}>
              Link new device
            </div>
            <div class={styles.headerSubText}>
              Link your device by scanning the QR code or copying the text below
            </div>
          </div>
          <div>
            <img src={qrCode$()} style="width: 100%" />
          </div>
          <div class={styles.pairingUrl} onClick={async () => {
            const pairingUrl = pairingUrl$();
            if (!pairingUrl || pairingUrl.length < 1) {
              return;
            }

            await navigator.clipboard.writeText(pairingUrl);
            UIOverlay.toast("Text copied to clipboard!");
          }}>
            {pairingUrl$()}
          </div>
        </div>
      </ScrollContainer>
    );
  };

  onMount(async () => {
    console.info("Registered required websocket handlers.");
    StateWebsocket.registerHandlerNew("SyncDevicesChanged", (packet) => {
      devicesActions.refetch();
    }, this);
  });

  onCleanup(() => {
      StateWebsocket.unregisterHandler("activeDeviceChanged", this);
  });

  return (
    <div style="width: 100%; height: 100%; display: flex; flex-direction: column;">
      <NavigationBar isRoot={true} style={{"flex-shrink": 0}} />
      <Show when={(devices$()?.length ?? 0) > 0} fallback={
        renderQrCodeOverlay()
      }>
        <div class={styles.header}>
          <div style="display: flex; flex-direction: column; flex-grow: 1;">
            <div class={styles.headerText}>
              Sync devices
            </div>
            <div class={styles.headerSubText}>
              Sync your subscriptions, history, playlists, watch later, as well as other functionality across all devices. Keep your data up to date automatically with Grayjay.
            </div>
          </div>
          <ButtonFlex text={"Show QR"}
            icon={iconQr}
            onClick={() => {
              setIsQrVisible(true);
            }}
            style={{cursor: "pointer", "flex-shrink": 0, "margin-right": "8px"}} 
            small={true} />
          <ButtonFlex text={"Add device"}
            icon={iconAdd}
            onClick={() => {
              UIOverlay.overlayNewDeviceSync();
            }}
            style={{cursor: "pointer", "flex-shrink": 0}} 
            small={true}
            color={"linear-gradient(267deg, #01D6E6 -100.57%, #0182E7 90.96%)"} />
        </div>
        <div style="margin-left: 24px; margin-top: 24px;">
          My devices
        </div>
        <ScrollContainer style={{"max-height": "300px", "gap": "8px", "margin-top": "8px", "display": "flex", "flex-direction": "column"}}>
          <For each={devices$()}>{(item) => renderDevice(item.publicKey, item.displayName ?? item.publicKey, item.metadata, item.linkType)}</For>
        </ScrollContainer>
      </Show>
      <Show when={isQrVisible$()}>
        <div style="position: absolute; width: 100%; height: 100%; top: 0px; left: 0px; background-color: rgba(10,10,10,.95)" onClick={() => setIsQrVisible(false)}>{renderQrCodeOverlay()}</div>
      </Show>
    </div>
  );
};

export default SyncPage;
