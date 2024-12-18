import { createContext, useContext, JSX, ParentComponent, createSignal, Accessor, onMount, onCleanup } from "solid-js";
import StateWebsocket from "../state/StateWebsocket";
import { CastingBackend } from "../backend/CastingBackend";
import { Duration } from "luxon";

export enum CastProtocolType {
    Chromecast,
    Airplay,
    FCast
};

export interface CastingDeviceInfo {
    id: string;
    name: string;
    type: CastProtocolType;
    addresses: string[];
    port: number;
};

export enum CastingDialogState {
    Closed = 0,
    DeviceList,
    AddDeviceManually,
    ActiveDevice
};

export enum CastConnectionState {
    Disconnected = 0,
    Connecting,
    Connected
}

export interface CastingContextState {
    dialogState: CastingDialogState;
};

export interface CastingContextValue {
    activeDevice: {
        device: Accessor<CastingDeviceInfo | undefined>,
        isPlaying: Accessor<boolean>,
        duration: Accessor<Duration>,
        time: Accessor<Duration>,
        volume: Accessor<number>,
        speed: Accessor<number>
        state: Accessor<CastConnectionState>
    };
    dialogState: Accessor<CastingDialogState>;
    discoveredDevices: Accessor<CastingDeviceInfo[]>;
    pinnedDevices: Accessor<CastingDeviceInfo[]>;
    actions: {
        open: () => void;
        openAddDeviceManually: () => void;
        close: () => void;
        connect: (id: string) => void;
        disconnect: () => void;
        addPinnedDevice: (castingDeviceInfo: CastingDeviceInfo) => void;
        removePinnedDevice: (castingDeviceInfo: CastingDeviceInfo) => void;
    }
};

const CastingContext = createContext<CastingContextValue>();
export interface CastingContextProps {
    children: JSX.Element;
};

export const CastingProvider: ParentComponent<CastingContextProps> = (props) => {
    const [dialogState, setDialogState] = createSignal<CastingDialogState>(CastingDialogState.Closed);
    const [discoveredDevices, setDiscoveredDevices] = createSignal<CastingDeviceInfo[]>([]);
    const [pinnedDevices, setPinnedDevices] = createSignal<CastingDeviceInfo[]>([]);

    const [activeDevice, setActiveDevice] = createSignal<CastingDeviceInfo>();
    const [isPlaying, setIsPlaying] = createSignal(false);
    const [duration, setDuration] = createSignal(Duration.fromMillis(0));
    const [time, setTime] = createSignal(Duration.fromMillis(0));
    const [volume, setVolume] = createSignal(1);
    const [speed, setSpeed] = createSignal(1);
    const [state, setState] = createSignal(CastConnectionState.Disconnected);

    const open = () => {
        if (activeDevice())
            setDialogState(CastingDialogState.ActiveDevice);
        else
            setDialogState(CastingDialogState.DeviceList);
    };
    const openAddDeviceManually = () => {
        setDialogState(CastingDialogState.AddDeviceManually);
    };
    const close = () => {
        setDialogState(CastingDialogState.Closed);
    };
    const connect = async (id: string) => {
        const device = discoveredDevices().find(d => d.id === id) ?? pinnedDevices().find(d => d.id === id);
        if (!device) {
            return;
        }

        setActiveDevice(device);
        setDialogState(CastingDialogState.ActiveDevice);

        await CastingBackend.connect(id);
    };
    const disconnect = async () => {
        setActiveDevice(undefined);
        setDialogState(CastingDialogState.Closed);

        await CastingBackend.disconnect();
    };
    const addPinnedDevice = async (deviceInfo: CastingDeviceInfo) => {
        await CastingBackend.addPinnedDevice(deviceInfo);
        setPinnedDevices([ ... pinnedDevices(), deviceInfo ]);
    };
    const removePinnedDevice = async (deviceInfo: CastingDeviceInfo) => {
        await CastingBackend.removePinnedDevice(deviceInfo);
        setPinnedDevices(pinnedDevices().filter(v => v.id !== deviceInfo.id));
    };

    const value: CastingContextValue = {
        dialogState,
        discoveredDevices,
        pinnedDevices,
        activeDevice: {
            device: activeDevice,
            isPlaying,
            duration,
            time,
            volume,
            speed,
            state
        },
        actions: {
            open,
            openAddDeviceManually,
            close,
            connect,
            disconnect,
            addPinnedDevice,
            removePinnedDevice
        }
    };

    onMount(async () => {
        try {
            setDiscoveredDevices(await CastingBackend.discoveredDevices());
        } catch (e) {
            console.warn("Failed to get discovered devices.", e);
        }

        try {
            setPinnedDevices(await CastingBackend.pinnedDevices());
        } catch (e) {
            console.warn("Failed to get pinned devices.", e);
        }

        console.info("Registered required websocket handlers.");

        StateWebsocket.registerHandlerNew("activeDeviceChanged", (packet) => setActiveDevice(packet.payload), this);
        StateWebsocket.registerHandlerNew("activeDeviceIsPlayingChanged", (packet) => setIsPlaying(packet.payload), this);
        StateWebsocket.registerHandlerNew("activeDeviceDurationChanged", (packet) => setDuration(Duration.fromMillis(packet.payload * 1000)), this);
        StateWebsocket.registerHandlerNew("activeDeviceTimeChanged", (packet) => setTime(Duration.fromMillis(packet.payload * 1000)), this);
        StateWebsocket.registerHandlerNew("activeDeviceVolumeChanged", (packet) => setVolume(packet.payload), this);
        StateWebsocket.registerHandlerNew("activeDeviceSpeedChanged", (packet) => setSpeed(packet.payload), this);
        StateWebsocket.registerHandlerNew("activeDeviceStateChanged", (packet) => setState(packet.payload), this);
        StateWebsocket.registerHandlerNew("discoveredDevicesUpdated", (packet) => setDiscoveredDevices(packet.payload), this);
    });

    onCleanup(() => {
        StateWebsocket.unregisterHandler("activeDeviceChanged", this);
        StateWebsocket.unregisterHandler("activeDeviceIsPlayingChanged", this);
        StateWebsocket.unregisterHandler("activeDeviceDurationChanged", this);
        StateWebsocket.unregisterHandler("activeDeviceTimeChanged", this);
        StateWebsocket.unregisterHandler("activeDeviceVolumeChanged", this);
        StateWebsocket.unregisterHandler("activeDeviceSpeedChanged", this);
        StateWebsocket.unregisterHandler("activeDeviceStateChanged", this);
        StateWebsocket.unregisterHandler("discoveredDevicesUpdated", this);
    });

    return (
        <CastingContext.Provider value={value}>
            {props.children}
        </CastingContext.Provider>
    );
}

export function useCasting() { return useContext(CastingContext); }