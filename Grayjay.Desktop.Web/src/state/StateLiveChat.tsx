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
    thumbnail?: string;
    message: string;
    colorName?: string;
    badges?: string[];
}
export interface LiveDonationEvent {
    type: LiveEventType.DONATION;
    name: string;
    thumbnail?: string;
    message: string;
    amount: string;
    colorDonation?: string;
    expire?: number;
    receivedAt?: number;
}
export interface LiveEmojisEvent { 
    type: LiveEventType.EMOJIS;
    emojis: Record<string, string>;
}
export interface LiveRaidEvent { 
    type: LiveEventType.RAID;
    targetName: string;
    targetUrl: string;
    targetThumbnail: string;
    isOutgoing: boolean;
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
let _expirationInterval: ReturnType<typeof setInterval> | null = null;

export function ensureLiveChatWebsocket() {
    if (websocketHandlerRegistered) return;
    websocketHandlerRegistered = true;

    if (!_expirationInterval) {
        _expirationInterval = setInterval(() => {
            const now = Date.now();
            for (const key in store.donations) {
                const d = store.donations[key]!;
                if (d.expire && d.receivedAt! + d.expire <= now) {
                    setStore('donations', key, undefined!);
                }
            }
        }, 1_000);
    }
    
    //startMockDonations();

    StateWebsocket.registerHandlerNew('LiveEvents', (p) => {
        const newEvents = p.payload as LiveChatEvent[];
        let newMessages: LiveChatEvent[] = [];
        for (const ev of newEvents) {
            switch (ev.type) {
                case LiveEventType.COMMENT:
                    newMessages.push(ev);
                    break;
                case LiveEventType.DONATION: {
                    const donation = ev as LiveDonationEvent;
                    const key = `${donation.name}-${donation.amount}-${donation.message}`;
                    if (!store.donations[key]) {
                        donation.receivedAt = Date.now();
                        setStore('donations', key, donation);
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
        if (_expirationInterval) {
            clearInterval(_expirationInterval);
            _expirationInterval = null;
        }
    });
}

let _mockInterval: ReturnType<typeof setInterval> | null = null;

export function startMockDonations(intervalSeconds: number = 5) {
  if (_mockInterval) clearInterval(_mockInterval);

  _mockInterval = setInterval(() => {
    const ev: LiveDonationEvent = {
      type: LiveEventType.DONATION,
      name: 'Test Donor',
      message: `Test donation at ${new Date().toLocaleTimeString()}`,
      amount: `$${(Math.random() * 9 + 1).toFixed(2)}`,
      colorDonation: '#FFD700',
      expire: 5000,
      receivedAt: Date.now()
    };
    const key = `${ev.name}-${ev.amount}-${ev.receivedAt}`;
    setStore('donations', key, ev);
  }, intervalSeconds * 1000);
}

export default {
  store,
  ensureLiveChatWebsocket
};