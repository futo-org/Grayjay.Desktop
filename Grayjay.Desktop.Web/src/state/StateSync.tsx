import { Accessor, Resource, createResource, createRoot, createSignal } from "solid-js";
import { SourcesBackend } from "../backend/SourcesBackend";
import { Event1 } from "../utility/Event";
import { IFilter, IResultCapabilities, ISourceConfig, ISourceConfigState } from "../backend/models/plugin/ISourceConfigState";
import StateWebsocket from "./StateWebsocket";
import { SettingsBackend } from "../backend/SettingsBackend";
import { HomeBackend } from "../backend/HomeBackend";
import { IPlatformContent } from "../backend/models/content/IPlatformContent";
import { RefreshPager } from "../backend/models/pagers/RefreshPager";
import { IPlatformVideo } from "../backend/models/content/IPlatformVideo";
import { DateTime } from "luxon";
import { SyncBackend } from "../backend/SyncBackend";
import { SyncDevice } from "../backend/models/sync/SyncDevice";

import iconLocal from '../assets/icons/ic_local.svg';
import iconInternet from '../assets/icons/ic_internet.svg';
import iconOffline from '../assets/icons/ic_offline.svg';
import { createResourceDefault } from "../utility";

export interface StateSync {
    devicesOnline$: Resource<SyncDevice[]>,
    getSyncIcon(linkType: number): string
    getSyncLinkName(linkType: number): string
};

function createState() {
    console.log("Initializing Sync");

    const [devicesOnline$, devicesOnlineResource] = createResourceDefault(async () => await SyncBackend.getOnlineDevices());
    
    StateWebsocket.registerHandlerNew("SyncDevicesChanged", (packet)=>{
        console.log("Devices online updated, refetching");
        devicesOnlineResource.refetch();
    }, "stateSyncDevicesChanged");

    const value: StateSync = {
        devicesOnline$: devicesOnline$,

        getSyncIcon(linkType: number): string {
            switch (linkType) {
                default:
                    return iconOffline;
                    break;
                case 1:
                    return iconLocal;
                    break;
                case 2:
                    return iconInternet;
                    break;
            }
        },
        getSyncLinkName(linkType: number): string {
            switch (linkType) {
                default:
                    return "Offline";
                case 1:
                    return "Local";
                case 2:
                    return "Proxied";
            }
        }
    };

    return value;
}

export default createRoot(createState);