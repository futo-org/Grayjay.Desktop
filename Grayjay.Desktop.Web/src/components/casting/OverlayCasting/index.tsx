import { Component, For, Match, Show, Switch, createEffect, createMemo, createSignal } from 'solid-js'

import styles from './index.module.css';
import { CastConnectionState, CastProtocolType, CastingDeviceInfo, CastingDialogState, useCasting } from '../../../contexts/Casting';
import iconClose from '../../../assets/icons/icon24_close.svg';
import iconPlus from '../../../assets/icons/plus.svg';
import iconPin from '../../../assets/icons/pinned.svg';
import iconPinFill from '../../../assets/icons/pinned-fill.svg';
import iconNoDevicesFound from '../../../assets/icons/nodevicesfound.svg';
import iconHelp from '../../../assets/icons/help_blue.svg';
import iconChromecastActive from '../../../assets/icons/ic_chromecast_active.svg';
import iconAirplayActive from '../../../assets/icons/ic_airplay_active.svg';
import iconFCastActive from '../../../assets/icons/ic_fcast_active.svg';
import iconChromecastInactive from '../../../assets/icons/ic_chromecast_inactive.svg';
import iconAirplayInactive from '../../../assets/icons/ic_airplay_inactive.svg';
import iconFCastInactive from '../../../assets/icons/ic_fcast_inactive.svg';
import BorderButton from '../../buttons/BorderButton';
import CircleLoader from '../../basics/loaders/CircleLoader';
import ButtonGroup from '../../ButtonGroup';
import InputText from '../../basics/inputs/InputText';
import ButtonFlex from '../../buttons/ButtonFlex';
import UIOverlay from '../../../state/UIOverlay';
import { CastingBackend } from '../../../backend/CastingBackend';
import { Portal } from 'solid-js/web';
import Button from '../../buttons/Button';

const getDeviceIcon = (device?: CastingDeviceInfo, active?: boolean) => {
    if (!device) {
        return undefined;
    }

    switch (device.type) {
        case CastProtocolType.Airplay:
            return active === true ? iconAirplayActive : iconAirplayInactive;
        case CastProtocolType.Chromecast:
            return active === true ? iconChromecastActive : iconChromecastInactive;
        case CastProtocolType.FCast:
            return active === true ? iconFCastActive : iconFCastInactive;
    }

    return undefined;
};

const getDeviceTypeName = (device?: CastingDeviceInfo) => {
    if (!device) {
        return undefined;
    }

    switch (device.type) {
        case CastProtocolType.Airplay:
            return "Airplay";
        case CastProtocolType.Chromecast:
            return "Chromecast";
        case CastProtocolType.FCast:
            return "FCast";
    }

    return undefined;
};

interface CastingDeviceViewProps {
    device: CastingDeviceInfo;
    pinned?: boolean;
    detected?: boolean;
}

const CastingDeviceView: Component<CastingDeviceViewProps> = (props) => {
    const casting = useCasting();
    const icon$ = createMemo(() => getDeviceIcon(props.device, casting?.discoveredDevices().some(d => d.id === props.device.id)));
    const name = getDeviceTypeName(props.device);

    const unpin = (ev: MouseEvent) => {
        casting?.actions?.removePinnedDevice(props.device);
        ev.preventDefault();
        ev.stopPropagation();
    };

    const pin = (ev: MouseEvent) => {
        casting?.actions?.addPinnedDevice(props.device);
        ev.preventDefault();
        ev.stopPropagation();
    };

    return (
        <Show when={icon$() && name}>
            <div class={styles.containerDevice} onClick={async () => {
                if (props.device.id) {
                    await casting?.actions.connect(props.device.id);
                }
            }}>
                <img src={icon$()} /> 
                <div style="display: flex; flex-direction: column; flex-grow: 1">
                    <div class={styles.deviceName}>{props.device.name}</div>
                    <div class={styles.deviceType}>{name}</div>
                </div>
                <Show when={props.pinned} fallback={<img src={iconPin} onclick={pin} />}>
                    <img src={iconPinFill} onclick={unpin} />
                </Show>
            </div>
        </Show>
    );
};

const DeviceList: Component = () => {
    const casting = useCasting();

    const pinnedDevices$ = createMemo(() =>
    {
        const pinnedDevices = casting?.pinnedDevices();
        if (!pinnedDevices)
            return undefined;

        return pinnedDevices
            .slice(0)
            .sort((a, b) => a.name.localeCompare(b.name));
    });

    const discoveredDevices$ = createMemo(() =>
    {
        const pinnedDevices = pinnedDevices$();
        if (!pinnedDevices)
            return casting?.discoveredDevices();

        return casting?.discoveredDevices()
            .filter(d => !pinnedDevices.some(e => d.id === e.id))
            .sort((a, b) => a.name.localeCompare(b.name));
    });

    return (
        <>
            <Show when={(pinnedDevices$()?.length ?? 0) == 0 && (discoveredDevices$()?.length ?? 0) == 0}>
                <div class={styles.containerNoDevicesFound}>
                    <img src={iconNoDevicesFound} />
                    <div class={styles.containerNoDevicesFoundTitle}>No devices found so far <CircleLoader style={{"width": "16px", "height": "16px", "margin-left": "8px"}} /></div>
                    <BorderButton small={true}
                        icon={iconPlus}
                        text='Add manually'
                        style={{
                            "margin-top": "30px",
                            "margin-bottom": "20px"
                        }}
                        onClick={() => {
                            casting?.actions.openAddDeviceManually();
                        }} />
                </div>
            </Show>
            <Show when={(pinnedDevices$()?.length ?? 0) > 0 || (discoveredDevices$()?.length ?? 0) > 0}>
                <div class={styles.containerHeader}>
                    Available devices <CircleLoader style={{"width": "16px", "height": "16px", "margin-left": "8px"}} />
                </div>
                <div class={styles.containerDevices}>
                    <For each={pinnedDevices$()}>{(item, i) => {
                        return (
                            <CastingDeviceView device={item} pinned={true} />
                        );
                    }}</For>
                    <For each={discoveredDevices$()}>{(item, i) => {
                        return (
                            <CastingDeviceView device={item} />
                        );
                    }}</For>
                </div>

                <div style="height: 1px; width: 100%; background-color: #2E2E2E; margin-top: 16px;"></div>

                <div style="display: flex; flex-direction: row; width: 100%; margin-top: 20px;">
                    <div class={styles.containerUnableToSeeDevice}>
                        <div>Unable to see the device you’re looking for?</div>
                        <div>Try to add the device manually</div>
                    </div>
                    <div style="flex-grow: 1"></div>
                    <BorderButton small={true}
                        icon={iconPlus}
                        text='Add manually'
                        onClick={() => {
                            casting?.actions.openAddDeviceManually();
                        }} />
                </div>
            </Show>
        </>
    );
};


const AddDeviceManually: Component = () => {
    const casting = useCasting();

    const [selectedType$, setSelectedType] = createSignal("FCast");

    const [name$, setName] = createSignal<string>();
    const nameErrorMessage$ = createMemo<string | undefined>(() => {
        const name = name$();
        if (!name || name.length < 1)
            return "Name must be at least 1 character.";

        return undefined;
    });

    const [ip$, setIP] = createSignal<string>();
    const ipErrorMessage$ = createMemo<string | undefined>(() => {
        const ip = ip$();
        if (!ip)
            return "Empty string is not valid.";

        const regexIpv6 = /(([0-9a-fA-F]{1,4}:){7,7}[0-9a-fA-F]{1,4}|([0-9a-fA-F]{1,4}:){1,7}:|([0-9a-fA-F]{1,4}:){1,6}:[0-9a-fA-F]{1,4}|([0-9a-fA-F]{1,4}:){1,5}(:[0-9a-fA-F]{1,4}){1,2}|([0-9a-fA-F]{1,4}:){1,4}(:[0-9a-fA-F]{1,4}){1,3}|([0-9a-fA-F]{1,4}:){1,3}(:[0-9a-fA-F]{1,4}){1,4}|([0-9a-fA-F]{1,4}:){1,2}(:[0-9a-fA-F]{1,4}){1,5}|[0-9a-fA-F]{1,4}:((:[0-9a-fA-F]{1,4}){1,6})|:((:[0-9a-fA-F]{1,4}){1,7}|:)|fe80:(:[0-9a-fA-F]{0,4}){0,4}%[0-9a-zA-Z]{1,}|::(ffff(:0{1,4}){0,1}:){0,1}((25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9])\.){3,3}(25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9])|([0-9a-fA-F]{1,4}:){1,4}:((25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9])\.){3,3}(25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9]))/;
        const regexIpv4 = /(\b25[0-5]|\b2[0-4][0-9]|\b[01]?[0-9][0-9]?)(\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)){3}/;

        return (regexIpv4.test(ip) || regexIpv6.test(ip)) ? undefined : "Not a valid IPv4 or IPv6 format.";
    });

    const [port$, setPort] = createSignal<string>("46899");
    const portErrorMessage$ = createMemo<string | undefined>(() => {
        const port = port$();
        if (!port)
            return "Empty string is not valid.";
    
        const n = Number.parseInt(port, 10);
        if (Number.isNaN(n))
            return "Not a valid number.";
    
        if (n < 1 || n > 65535)
            return "Port number must be between 1 and 65535.";
    
        if (!Number.isInteger(n))
            return "Port number must be an integer.";
    
        return undefined;
    });

    const hasError$ = createMemo(() => {
        return nameErrorMessage$() || ipErrorMessage$() || portErrorMessage$();
    });

    createEffect(() => {
        if (selectedType$() === "FCast") {
            const port = port$();
            if (!port || port.length < 1) {
                setPort("46899");
            }
        }
    });

    return (
        <div class={styles.containerAddManually}>
            <ButtonGroup items={["FCast", "Chromecast", "AirPlay"]} defaultSelectedItem={selectedType$()} onItemChanged={v => setSelectedType(v)} style={{"margin-top": "32px"}} />
            <div class={styles.containerHeader} style="margin-top: 24px">Enter device details</div>
            <div style="display: flex; width: 100%; flex-direction: column;">
                <div style="position: relative; width: 100%; margin-top: 12px;">
                    <InputText label="Device name" small={true} style={{"width": "100%"}} value={name$()} onTextChanged={(v) => setName(v)} error={nameErrorMessage$()} />
                </div>
                <div style="display: flex; width: 100%; flex-direction: row; margin-top: 12px;">
                    <div style="position: relative; min-width: 460px; flex-grow: 1;">
                        <InputText label="Device IP" small={true} style={{"width": "100%"}} value={ip$()} onTextChanged={(v) => setIP(v)} error={ipErrorMessage$()} />
                    </div> 
                    <div style="position: relative; margin-left: 12px; width: 200px;">
                        <InputText label="Port" small={true} style={{"width": "100%"}} value={port$()} onTextChanged={(v) => setPort(v)} error={portErrorMessage$()} />
                    </div> 
                </div>
            </div>
            <div style="display: flex; width: 100%; flex-direction: row; align-items: center; margin-top: 24px">
                <div class={styles.helpContainer}>
                    <img class={styles.helpIcon} src={iconHelp} />
                    <div class={styles.helpText}>Help</div>
                </div>
                <div style="flex-grow: 1"></div>
                <ButtonFlex small={true} onClick={async () => {
                    if (hasError$()) {
                        UIOverlay.dialog({
                            title: "Invalid",
                            description: "Cannot add pinned device because there are errors on the input.",
                            buttons: [{title: "OK", onClick:()=>{}}]
                        });
                        return;
                    }

                    const ip = ip$();
                    const name = name$();
                    if (!ip || !name) {
                        return;
                    }

                    await casting?.actions.addPinnedDevice({
                        id: name,
                        name,
                        port: Number.parseInt(port$()),
                        type: CastProtocolType.FCast,
                        addresses: [ ip ]
                    });

                    casting?.actions.open();
                }}
                    text='Add device'
                    color='#019BE7'
                    style={{
                        "height": "48px",
                        "width": "180px",
                        "flex-shrink": "0"
                    }}></ButtonFlex>
            </div>
        </div>
    );
};

const ActiveDeviceView: Component = () => {
    const casting = useCasting();
    const icon$ = createMemo(() => {
        const activeDevice = casting?.activeDevice?.device();
        return getDeviceIcon(casting?.activeDevice?.device(), activeDevice ? casting?.discoveredDevices().some(d => d.id === activeDevice.id) : undefined)
    });
    
    return (
        <div style="display: flex; flex-direction: column; width: 100%;">
            <div class={styles.containerHeader}>
                Casting to <Show when={casting?.activeDevice.state() != CastConnectionState.Connected}><CircleLoader style={{"width": "16px", "height": "16px", "margin-left": "8px"}} /></Show>
            </div>
            <div style="display: flex; flex-direction: row; width: 100%; align-items: center; margin-top: 12px;">
                <img src={icon$()} /> 
                <div style="display: flex; flex-direction: column; flex-grow: 1; margin-left: 10px;">
                    <div class={styles.deviceName}>{casting?.activeDevice?.device()?.name}</div>
                    <div class={styles.deviceType}>{getDeviceTypeName(casting?.activeDevice?.device())}</div>
                </div>
                <BorderButton small={true}
                    text='Disconnect'
                    style={{"margin-left": "12px"}}
                    onClick={async () => {
                        await CastingBackend.mediaStop();
                        casting?.actions.disconnect();
                    }} />
            </div>
        </div>
    );
};

const OverlayCasting: Component = () => {
    const casting = useCasting();

    return (
        <>
            <Show when={casting?.dialogState() !== CastingDialogState.Closed}>
                <div class={styles.containerCastingBackground} onClick={() => casting?.actions.close()}>
                    <div class={styles.containerCasting} onClick={(ev) => {
                        ev.preventDefault();
                        ev.stopPropagation();
                    }}>
                        <div class={styles.containerCastingHeader}>
                            <div class={styles.containerCastingHeaderTitle}>Casting</div>
                            <div style="flex-grow: 1"></div>
                            <div class={styles.closeButton} onClick={() => casting?.actions.close()}>
                                <img src={iconClose} />
                            </div>
                        </div>

                        <div>
                            <Show when={casting?.dialogState() == CastingDialogState.DeviceList}>
                                <DeviceList />
                            </Show>
                            <Show when={casting?.dialogState() == CastingDialogState.AddDeviceManually}>
                                <AddDeviceManually />
                            </Show>
                            <Show when={casting?.dialogState() == CastingDialogState.ActiveDevice}>
                                <ActiveDeviceView />
                            </Show>
                        </div>
                    </div>
                </div>
            </Show>
            <Portal>
                <Show when={casting?.activeDevice.device() && casting?.activeDevice.state() === CastConnectionState.Connecting}>
                    <div class={styles.containerCastingBackground} onClick={(ev) => {
                        ev.preventDefault();
                        ev.stopPropagation();
                    }}>
                        <div class={styles.containerCasting}>
                            <div class={styles.containerCastingHeader}>
                                <div class={styles.containerCastingHeaderTitle}>Casting</div>
                                <div style="flex-grow: 1"></div>
                            </div>
                            
                            <div style="display: flex; width: 100%; flex-direction: column; justify-content: center; align-items: center; margin-top: 32px">
                                <div style="color: #FFF; font-family: Inter; font-size: 24px; font-style: normal; font-weight: 600;">Connecting to casting device</div>
                                <CircleLoader style={{"margin-top": "16px"}} />
                                <div style="color: #8C8C8C; text-align: center; leading-trim: both; text-edge: cap; font-family: Inter; font-size: 16px; font-style: normal; font-weight: 400; line-height: normal; text-align: center; margin-top: 32px">Make sure you are on the same network VPNs and guest networks can cause issues</div>
                            </div>

                            <div style="display: flex; width: 100%; flex-direction: column; justify-content: center; align-items: end; margin-top: 32px">
                                <ButtonFlex small={true} onClick={async () => {
                                        casting?.actions.disconnect();
                                    }}
                                    text='Disconnect'
                                    style={{ border: "1px solid rgba(1, 155, 231, 0)", "margin-left": "16px", "flex-shrink": "0" }}></ButtonFlex>
                            </div>
                        </div>
                    </div>
                </Show>
            </Portal>
        </>
    );
};

export default OverlayCasting;