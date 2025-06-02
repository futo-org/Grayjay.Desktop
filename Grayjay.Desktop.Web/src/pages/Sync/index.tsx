import { createMemo, createResource, createSignal, For, onCleanup, onMount, Show, untrack, type Component } from 'solid-js';
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
import CenteredLoader from '../../components/basics/loaders/CenteredLoader';
import { SettingsBackend } from '../../backend/SettingsBackend';
import StateGlobal from '../../state/StateGlobal';

const SyncPage: Component = () => {
  const [devices$, devicesActions] = createResourceDefault(async () => {
    return SyncBackend.getDevices();
  });  
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
        status = "Direct";
        break;
      case 2:
        icon = iconInternet;
        status = "Relayed";
        break;
    }
  
    return (<div style="display: flex; flex-direction: row; border-radius: 6px; background: #1B1B1B; padding: 14px 18px 14px; box-sizing: border-box; gap: 12px;  margin-left: 24px; margin-right: 24px; align-items: center; width: calc(100% - 48px);">
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

  const renderEnableSyncPrompt = () => {
    return (
      <div style="width: 100%; height: 100%; display: flex; flex-direction: column; justify-content: center; align-items: center;">
        <div class={styles.container} style="display: flex; flex-direction: column; justify-content: center; align-items: center;">
          <div class={styles.dialogHeader} style={{"margin-left": "0px"}}>
            <div class={styles.headerText}  style="display: flex; flex-direction: column; justify-content: center; align-items: center;">
              Please enable sync to use this feature
              <ButtonFlex style={{ width: "170px", "margin-top": "20px" }} small={true} text="Enable" color="#019BE7" onClick={ async () => {
                const s = StateGlobal.settings$();
                if (s == null) {
                  return;
                }
                s.object.synchronization.enabled = true;
                await SettingsBackend.settingsSave(s.object);
              }} />
            </div>
          </div>
        </div>
      </div>
    )
  };
  
  const renderQrCodeOverlay = () => {
    return (
      <ScrollContainer style={{
        "display": "flex",
        "justify-content": "center",
        "flex-grow": 1, 
        "width": "100%",
        "height": "100%",
        "align-items": "center"
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
          {JSON.stringify(pairingUrl$)}
          <Show when={pairingUrl$() && pairingUrl$()?.length} fallback={
            <div style="color: red">An error has occurred while trying to fetch the pairing URL. Please make sure Sync is enabled.</div>
          }>
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
          </Show>
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

  const synchronizationEnabled = createMemo(() => {
    return StateGlobal.settings$()?.object?.synchronization?.enabled;
  });

  const [status$] = createResourceDefault(() => [], async () => await SyncBackend.status());

  return (
    <Show when={!devices$.loading} fallback={ <CenteredLoader /> }>
      <div style="width: 100%; height: 100%; display: flex; flex-direction: column;">
        <NavigationBar isRoot={true} style={{"flex-shrink": 0}} />
        <div style="display: flex; flex-direction: row; align-items: center; flex-wrap: wrap; margin-left: 24px; margin-right: 24px; gap: 12px">
          <Show when={status$()?.serverSocketFailedToStart === true}>
            <div class={styles.errorCard}>Failed to start server socket, is the port in use?</div>
          </Show>
          <Show when={status$()?.serverSocketStarted === false}>
            <div class={styles.warningCard}>Listener not started, local connections will not work</div>
          </Show>
          <Show when={status$()?.relayConnected === false}>
            <div class={styles.warningCard}>Not connected to relay, remote connections will work</div>
          </Show>
        </div>
        <Show when={synchronizationEnabled()} fallback={
          renderEnableSyncPrompt()
        }>
          <>
            <Show when={(devices$()?.length ?? 0) > 0} fallback={
              <div style="width: 100%; height: 100%; display: flex; flex-direction: column; justify-content: flex-end; align-items: flex-end;">
                <ButtonFlex text={"Add device"}
                  icon={iconAdd}
                  onClick={() => {
                    UIOverlay.overlayNewDeviceSync();
                  }}
                  style={{cursor: "pointer", "flex-shrink": 0, "margin-right": "20px"}} 
                  small={true}
                  color={"linear-gradient(267deg, #01D6E6 -100.57%, #0182E7 90.96%)"} />

                {renderQrCodeOverlay()}
              </div>
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
              <ScrollContainer style={{"max-height": "300px", "gap": "8px", "margin-top": "8px", "display": "flex", "flex-direction": "column", "justify-content": "flex-start", "align-items": "center"}}>
                <For each={devices$()}>{(item) => renderDevice(item.publicKey, item.displayName ?? item.publicKey, item.metadata, item.linkType)}</For>
              </ScrollContainer>
            </Show>
            <Show when={isQrVisible$()}>
              <div style="position: absolute; width: 100%; height: 100%; top: 0px; left: 0px; background-color: rgba(10,10,10,.95)" onClick={() => setIsQrVisible(false)}>{renderQrCodeOverlay()}</div>
            </Show>
          </>
        </Show>
      </div>
    </Show>
  );
};

export default SyncPage;
