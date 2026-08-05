import { Accessor, Resource, createResource, createRoot } from "solid-js";
import { BlockedChannelsBackend, IBlockChannelRequest } from "../backend/BlockedChannelsBackend";
import StateWebsocket from "./StateWebsocket";
import StateGlobal from "./StateGlobal";

export interface StateBlockedChannels {
    blocked$: Resource<IBlockedChannel[]>,
    isBlocked(url: string | undefined): boolean,
    getBlocked(url: string | undefined): IBlockedChannel | undefined,
    block(url: string, name?: string, thumbnail?: string, pluginId?: string): Promise<void>,
    unblock(url: string): Promise<void>,
    clearAll(): Promise<void>,
    refresh(): void
};

function createState() {
    const [blocked$, blockedResource] = createResource<IBlockedChannel[]>(async () => {
        return await BlockedChannelsBackend.list();
    });

    StateWebsocket.registerHandlerNew("BlockedChannelsChanged", (packet) => {
        console.log("Blocked channels changed, refetching");
        blockedResource.refetch();
        try {
            StateGlobal.reloadHome();
        } catch (e) {
            console.warn("Failed to reload home after blocked channels change", e);
        }
    }, "blockedChannels");

    const value: StateBlockedChannels = {
        blocked$: blocked$,

        isBlocked(url: string | undefined) {
            if (!url)
                return false;
            const normalized = url.toLowerCase();
            return (blocked$() ?? []).some(x =>
                x.url.toLowerCase() === normalized ||
                (x.urlAlternatives ?? []).some(alt => alt.toLowerCase() === normalized)
            );
        },

        getBlocked(url: string | undefined) {
            if (!url)
                return undefined;
            const normalized = url.toLowerCase();
            return (blocked$() ?? []).find(x =>
                x.url.toLowerCase() === normalized ||
                (x.urlAlternatives ?? []).some(alt => alt.toLowerCase() === normalized)
            );
        },

        async block(url: string, name?: string, thumbnail?: string, pluginId?: string) {
            const request: IBlockChannelRequest = { url, name, thumbnail, pluginId };
            await BlockedChannelsBackend.add(request);
        },

        async unblock(url: string) {
            await BlockedChannelsBackend.remove(url);
        },

        async clearAll() {
            const blocked = blocked$() ?? [];
            for (const channel of blocked) {
                await BlockedChannelsBackend.remove(channel.url);
            }
        },

        refresh() {
            blockedResource.refetch();
        }
    };

    return value;
}

export default createRoot(createState);
