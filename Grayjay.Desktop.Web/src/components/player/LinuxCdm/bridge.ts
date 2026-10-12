type Rpc = {
    call(method: string, payload: unknown): Promise<any>;
    register(method: string, handler: (payload: any) => Promise<unknown>): void;
};
export type Progress = { stage: string; message: string; received: number; total?: number };
type Listener = (event: any) => Promise<void>;
const events = new Map<string, Listener>();
const progress = new Map<string, (value: Progress) => void>();
let registered: Rpc | undefined;
export function bridge(): Rpc | undefined {
    const rpc = (window as unknown as { bridge?: { rpc?: Rpc } }).bridge?.rpc;
    if (rpc && rpc !== registered) {
        registered = rpc;
        rpc.register('linuxCdm.event', async payload => { await events.get(payload.token)?.(payload.event); return true; });
        rpc.register('linuxCdm.progress', async payload => { progress.get(payload.token)?.(payload.progress); return true; });
    }
    return rpc;
}
export function listen(token: string, listener: Listener) {
    bridge(); events.set(token, listener);
    return () => events.delete(token);
}
export function listenProgress(token: string, listener: (value: Progress) => void) {
    bridge(); progress.set(token, listener);
    return () => progress.delete(token);
}
export function call<T = any>(method: string, token: string, data?: unknown, session?: string): Promise<T> {
    const rpc = bridge();
    if (!rpc) return Promise.reject(new Error('Protected playback is unavailable.'));
    return rpc.call('linuxCdm.' + method, { token, data, session });
}
export async function capabilities(): Promise<{ enabled: boolean; ready: boolean }> {
    try {
        const value = await bridge()?.call('linuxCdm.capabilities', null);
        return { enabled: value?.enabled === true && value.protocol === 4, ready: value?.ready === true };
    } catch { return { enabled: false, ready: false }; }
}
export function toBase64(data: Uint8Array): string {
    let text = '';
    for (let at = 0; at < data.length; at += 32768) text += String.fromCharCode(...data.subarray(at, at + 32768));
    return btoa(text);
}
export function fromBase64(data: string): Uint8Array<ArrayBuffer> {
    return Uint8Array.from(atob(data), c => c.charCodeAt(0));
}
