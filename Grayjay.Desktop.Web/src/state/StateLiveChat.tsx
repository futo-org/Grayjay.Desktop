import { createStore } from 'solid-js/store';
import { onCleanup } from 'solid-js';
import StateWebsocket from './StateWebsocket';

export enum LiveEventType { 
    UNKNOWN = 0, 
    COMMENT = 1, 
    EMOJIS = 4, 
    DONATION = 5, 
    VIEWCOUNT = 10, 
    RAID = 100 
}

export interface LiveCommentEvent {
    type: LiveEventType.COMMENT;
    name: string;
    message: string;
    thumbnail?: string;
    colorName?: string;
    badges?: string[]; 
}
export interface LiveDonationEvent {
    type: LiveEventType.DONATION;
    name: string;
    message: string;
    amount: string;
    colorDonation?: string;
    expire?: number;
    receivedAt?: number; 
}
export interface LiveEmojisEvent { 
    type: LiveEventType.EMOJIS;
    emojis: Record<string,string>; 
}
export interface LiveRaidEvent { 
    type: LiveEventType.RAID;
    targetName: string; 
    targetUrl: string; 
    targetThumbnail: string; 
}
export interface LiveViewCountEvent { 
    type: LiveEventType.VIEWCOUNT; 
    viewCount: number; 
}

export type LiveChatEvent =
    | LiveCommentEvent
    | LiveDonationEvent
    | LiveEmojisEvent
    | LiveRaidEvent
    | LiveViewCountEvent;

export interface LiveChatStore {
    messages: LiveChatEvent[];
    viewerCount: number;
    donations: Record<string, LiveDonationEvent>;
    emojis: Record<string, string>;
    raid: LiveRaidEvent | null;
}

const [store, setStore] = createStore<LiveChatStore>({
    messages: [],
    viewerCount: 0,
    donations: {},
    emojis: {},
    raid: null
});

let websocketHandlerRegistered = false;
export function ensureLiveChatWebsocket() {
    if (websocketHandlerRegistered) return;
    websocketHandlerRegistered = true;

    StateWebsocket.registerHandlerNew('LiveEvents', (p) => {
        const newEvents = p.payload as LiveChatEvent[];
        let newMessages: LiveChatEvent[] = [];
        for (const ev of newEvents) {
            switch (ev.type) {
                case LiveEventType.COMMENT:
                    newMessages.push(ev);
                    break;
                case LiveEventType.DONATION: {
                    const key = `${ev.name}${ev.amount}${ev.message}`;
                    if (!store.donations[key]) {
                        ev.receivedAt = Date.now();
                        setStore('donations', key, ev);
                    }
                    break;
                }
                case LiveEventType.RAID:
                    setStore('raid', ev as LiveRaidEvent);
                    break;
                case LiveEventType.VIEWCOUNT:
                    setStore('viewerCount', (ev as LiveViewCountEvent).viewCount);
                    break;
                case LiveEventType.EMOJIS:
                    setStore('emojis', prev => ({ ...prev, ...(ev as LiveEmojisEvent).emojis }));
                    break;
            }
        }

        if (newMessages.length) {
            setStore('messages', msgs => {
                const combined = [...msgs, ...newMessages];
                return combined.length > 200 ? combined.slice(combined.length - 200) : combined;
            });
        }
    }, 'liveEvents');

    StateWebsocket.registerHandlerNew('LiveEventsClear', (p) => {
        setStore({
            messages: [],
            viewerCount: 0,
            donations: {},
            emojis: {},
            raid: null
        });
    }, 'liveEventsClear');

    onCleanup(() => {
        StateWebsocket.unregisterHandler('LiveEvents', 'liveEvents');
        websocketHandlerRegistered = false;
    });
}

export default {
    store,
    ensureLiveChatWebsocket
};
