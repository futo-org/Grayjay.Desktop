import { Backend } from "./Backend";
import { IPlatformVideo } from "./models/content/IPlatformVideo";

export interface IOrderedPlatformVideo extends IPlatformVideo {
    index: number;
}

export interface NavIntent {
    url?: string;
    route?: string;
    timestamp: number;
}

export abstract class WindowBackend {
    static readonly NAV_INTENT_KEY = 'grayjay_new_window_nav_queue';
    static readonly NAV_INTENT_VALIDITY_MS = 5000;

    private static _cmdClickPending = false;

    static markCmdClick(active: boolean): void {
        WindowBackend._cmdClickPending = active;
    }

    static consumeCmdClick(): boolean {
        if (WindowBackend._cmdClickPending) {
            WindowBackend._cmdClickPending = false;
            return true;
        }
        return false;
    }

    static async startWindow(): Promise<Boolean> {
        return await Backend.GET("/window/startWindow")
    }

    static async openInNewWindow(payload: { url?: string; route?: string }): Promise<Boolean> {
        const queue = WindowBackend.readIntentQueue();
        queue.push({ ...payload, timestamp: Date.now() });
        localStorage.setItem(WindowBackend.NAV_INTENT_KEY, JSON.stringify(queue));
        return await WindowBackend.startWindow();
    }

    static consumeNavIntent(): NavIntent | undefined {
        const now = Date.now();
        const valid = WindowBackend.readIntentQueue().filter(i => now - i.timestamp < WindowBackend.NAV_INTENT_VALIDITY_MS);
        const next = valid.shift();
        if (valid.length === 0) {
            localStorage.removeItem(WindowBackend.NAV_INTENT_KEY);
        } else {
            localStorage.setItem(WindowBackend.NAV_INTENT_KEY, JSON.stringify(valid));
        }
        return next;
    }

    private static readIntentQueue(): NavIntent[] {
        try {
            const raw = localStorage.getItem(WindowBackend.NAV_INTENT_KEY);
            const parsed = raw ? JSON.parse(raw) : [];
            return Array.isArray(parsed) ? parsed : [];
        } catch (e) {
            console.warn("Corrupt new-window nav intent queue, resetting", e);
            localStorage.removeItem(WindowBackend.NAV_INTENT_KEY);
            return [];
        }
    }

    static async ready(): Promise<boolean> {
        return await Backend.GET("/window/Ready");
    }

    static async delay(ms: number): Promise<boolean> {
        return await Backend.GET("/window/Delay?ms=" + ms);
    }

    static async echo(str: string): Promise<boolean> {
        await Backend.GET("/window/Echo?str=" + str);
        return true;
    }

    static async closeWindow(): Promise<boolean> {
        await Backend.GET("/window/Close");
        return true;
    }

    static async inputSource(): Promise<string | undefined> {
        const inputSource = await Backend.GET_text("/window/InputSource");
        let s: string | undefined | null = inputSource;
        if (s && s.length)
            return s;
        return undefined;
    }
}