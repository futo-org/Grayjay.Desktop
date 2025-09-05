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
import { BuyBackend } from "../backend/BuyBackend";
import { createResourceDefault } from "../utility";
import { DeveloperBackend } from "../backend/DeveloperBackend";
import { isDev } from "solid-js/web";
import Globals from "../globals";
import { WindowBackend } from "../backend/WindowBackend";
import { LocalBackend } from "../backend/LocalBackend";
import { ToastDescriptor } from "../overlays/OverlayRoot";

export interface StateGlobal {
    settings$: Resource<any>,
    sources$: Resource<ISourceConfig[]>,
    toasts$: Accessor<{ descriptor: ToastDescriptor, expired$: Accessor<boolean> }[]>,
    sourceStates$: Resource<ISourceConfigState[]>,
    lastHomeTime$: Accessor<DateTime|undefined>,
    home$: Resource<RefreshPager<IPlatformVideo>>,
    didPurchase$: Resource<boolean>,
    isDeveloper$: Resource<boolean>,
    onGlobalClick: Event1<MouseEvent>,
    toast(toastDescriptor: ToastDescriptor): void,
    reloadHome(): void,
    getSourceConfig: (id: string | undefined) => ISourceConfig | undefined,
    getSourceState: (id: string | undefined) => ISourceConfigState | undefined,
    getCommonSearchCapabilities: (sourceIds: string[]) => IResultCapabilities | undefined,
    getCommonSearchChannelContentsCapabilities: (sourceIds: string[]) => IResultCapabilities | undefined
};

function createState() {
    console.log("Initializing Global");

    const [settings$, settingsResource] = createResourceDefault(async () => {
        const settings = await SettingsBackend.settings();
        console.log("New Settings:", settings);
        return settings;
    });
    const [sources$, sourcesResource] = createResourceDefault(async () => await SourcesBackend.sources());
    const [sourceStates$, sourceStatesResource] = createResourceDefault(async () => await SourcesBackend.sourceStates());
    const [lastHomeTime$, setLastHomeTime] = createSignal<DateTime>();
    const [home$, homeResource] = createResourceDefault(async () => {
        setLastHomeTime(DateTime.now());
        return await HomeBackend.homePagerLazy();
    });
    const [didPurchase$, didPurchaseResource] = createResourceDefault(async () => {
        return await BuyBackend.didPurchase();
    });
    const [isDeveloper$, isDeveloperResource] = createResourceDefault(async () => {
        return await DeveloperBackend.isDeveloper();
    });
    /*
    StateWebsocket.registerHandlerNew("PluginEnabled", (packet)=>{
        sourcesResource.refetch();
        sourceStatesResource.refetch();
    }, "webGlobal");
    */
    StateWebsocket.registerHandlerNew("LicenseStatusChanged", (packet)=>{
        console.log("license updated, refetching");
        didPurchaseResource.refetch();
    }, "webGlobal");
    StateWebsocket.registerHandlerNew("SettingsChanged", (packet)=>{
        console.log("Settings updated, refetching");
        settingsResource.refetch();
    }, "webGlobal");
    StateWebsocket.registerHandlerNew("EnabledClientsChanged", (packet)=>{
        console.log("Clients changed, refetching");
        sourcesResource.refetch();
        sourceStatesResource.refetch();
    }, "webGlobal");

    const getCommonSearchCapabilitiesType = (sourceIds: string[], capabilitiesGetter: (sourceId: string) => IResultCapabilities | undefined): IResultCapabilities | undefined => {
        try {
            console.log("Platform - getCommonSearchCapabilities");

            const sources = (sources$() ?? []).filter(source => sourceIds.includes(source.id));
            const c = sources[0] || null;
            if (!c) return undefined;
            const cap = capabilitiesGetter(c.id);
            if (!cap) return undefined;

            let sorts = [...cap.sorts];
            let filters = [...cap.filters];
            let types = [...cap.types];

            for (let i = 1; i < sources.length; i++) {
                const clientSearchCapabilities = capabilitiesGetter(sources[i].id);
                if (!clientSearchCapabilities) {
                    return { filters: [], sorts: [], types: types };
                }

                sorts = sorts.filter(sort => clientSearchCapabilities.sorts.includes(sort));
                for (const type of clientSearchCapabilities.types) {
                    if (!types.some((v) => v === type)) {
                        types.push(type);
                    }
                }

                filters = filters.map(filter => {
                    const matchingFilterGroup = clientSearchCapabilities.filters.find(f => (f.id ?? f.name) === (filter.id ?? filter.name));
                    if (!matchingFilterGroup) return null;
                    return {
                        ...filter,
                        filters: filter.filters.filter(a => matchingFilterGroup.filters.some(b => (a.id ?? a.name) === (b.id ?? b.name)))
                    };
                }).filter((filter): filter is IFilter => filter !== null);
            }

            return { filters, sorts, types };
        } catch (e) {
            console.warn("Failed to get common search capabilities.", e);
            return undefined;
        }
    };

    const toast = (toastDescriptor: ToastDescriptor) => {
        toastDescriptor.id = (Math.random() + 1).toString(36).substring(7);

        const [expired$, setExpired] = createSignal(false);
        setToasts([{ descriptor: toastDescriptor, expired$: expired$ }].concat(toasts$()));
        setTimeout(()=>{
        setExpired(true);
        setTimeout(()=>{
            setToasts(toasts$().filter((x)=> x.descriptor !== toastDescriptor));
        }, 600)
        }, (toastDescriptor.long ? 3000 : 1500));
    };

    const [toasts$, setToasts] =  createSignal<{ descriptor: ToastDescriptor, expired$: Accessor<boolean> }[]>([]);
    StateWebsocket.registerHandlerNew("Toast", (packet)=>{
      try {
        const toastDescriptor = packet.payload as ToastDescriptor;
        if(toastDescriptor) {
            toast(toastDescriptor);
        }
      }
      catch{}
    }, "toasts");

    const value: StateGlobal = {
        settings$: settings$,
        sources$: sources$,
        toasts$: toasts$,
        sourceStates$: sourceStates$,
        isDeveloper$: isDeveloper$,

        lastHomeTime$: lastHomeTime$,
        home$: home$,
        didPurchase$: didPurchase$,
        
        onGlobalClick: new Event1<MouseEvent>(),


        toast: toast,
        reloadHome(){
            homeResource.refetch();
        },

        getSourceConfig(id: string | undefined) {
            if(!id)
                return undefined;
            return sources$()?.find(x=>x.id == id);
        },
        getSourceState(id: string | undefined) {
            if(!id)
                return undefined;
            return sourceStates$()?.find(x=>x.config.id == id);
        },
        getCommonSearchCapabilities(sourceIds: string[]): IResultCapabilities | undefined {
            return getCommonSearchCapabilitiesType(sourceIds, (sourceId) => this.getSourceState(sourceId)?.capabilitiesSearch);
        },
        getCommonSearchChannelContentsCapabilities(sourceIds: string[]): IResultCapabilities | undefined {
            return getCommonSearchCapabilitiesType(sourceIds, (sourceId) => this.getSourceState(sourceId)?.capabilitiesChannel);
        }    
    };


    //Lets assume when global is returned, next tick frontend is ready-ish
    setTimeout(()=>{
        try {
            WindowBackend.ready();
        }
        catch(ex){
            console.log("Failed to call ready");
        }
    }, 100);

    return value;
}

export default createRoot(createState);