import Globals from "../../../globals";

export interface UmpFormatInfo {
    key: string;
    itag: number;
    xtags: string;
    mimeType: string;
    codecs: string;
    codecName: string;
    bitrate: number;
    width: number;
    height: number;
    fps: number;
    audioChannels: number;
    audioSampleRate: number;
    language?: string;
    languageName?: string;
    original: boolean;
    isDrc: boolean;
    label: string;
}

export interface UmpInfo {
    id: string;
    isLive: boolean;
    durationMs: number;
    mediaBaseMs: number;
    windowStartMs?: number;
    windowEndMs?: number;
    liveEdgeStartMs?: number;
    videoFormats: UmpFormatInfo[];
    audioFormats: UmpFormatInfo[];
    activeVideoKey?: string;
    activeAudioKey?: string;
    subtitleUrl?: string;
}

export type UmpErrorKind = "reload" | "blocked" | "substituted" | "unsupported" | "error";

export interface UmpPlayerCallbacks {
    onError?: (message: string, fatal: boolean, kind: UmpErrorKind) => void;
    onFormatsChanged?: (video: UmpFormatInfo[], audio: UmpFormatInfo[]) => void;
    onActiveFormatChanged?: (role: "video" | "audio", format: UmpFormatInfo) => void;
    onCueEnter?: (id: string, text: string) => void;
    onCueExit?: (id: string) => void;
}

type Role = "video" | "audio";

const VOD_TARGET_AHEAD_S = 30;
const LIVE_TARGET_AHEAD_S = 12;
const KEEP_BEHIND_S = 30;
const IDLE_POLL_MS = 250;
const MAX_TRANSIENT_ERRORS = 5;
const LIVE_INFO_POLL_MS = 5000;
const GAP_CHECK_MS = 250;
const MAX_GAP_JUMP_S = 0.5;

class UmpFatalError extends Error {
    constructor(message: string, public kind: UmpErrorKind) {
        super(message);
    }
}

function sleep(ms: number, signal?: AbortSignal): Promise<void> {
    return new Promise((resolve) => {
        const timeout = setTimeout(resolve, ms);
        signal?.addEventListener("abort", () => {
            clearTimeout(timeout);
            resolve();
        }, { once: true });
    });
}

function isAbort(e: any) {
    return e?.name === "AbortError";
}

export function umpFullMime(format: { mimeType: string, codecs: string }) {
    return format.codecs ? `${format.mimeType}; codecs="${format.codecs}"` : format.mimeType;
}

export function umpIsSupported(format: UmpFormatInfo) {
    try {
        return typeof MediaSource !== "undefined" && MediaSource.isTypeSupported(umpFullMime(format));
    } catch {
        return false;
    }
}

function codecFamily(codecs: string) {
    const c = (codecs ?? "").toLowerCase();
    if (c.startsWith("avc")) return "avc";
    if (c.startsWith("vp9") || c.startsWith("vp09")) return "vp9";
    if (c.startsWith("av01")) return "av1";
    if (c.startsWith("mp4a")) return "aac";
    if (c.startsWith("opus")) return "opus";
    return c.split(".")[0];
}

class TrackLoader {
    private sourceBuffer?: SourceBuffer;
    private currentKey?: string;
    private currentMime?: string;
    private nextSeq?: number;
    private lastAppendedEndS?: number;
    private loadS: number;
    private abort = new AbortController();
    private wake?: () => void;
    private transientErrors = 0;
    private running = false;
    hasAppended = false;
    ended = false;

    get loadPositionS() {
        return this.loadS;
    }

    constructor(private player: UmpPlayer, readonly role: Role, startS: number) {
        this.loadS = startS;
    }

    attach(sourceBuffer: SourceBuffer, mime: string) {
        this.sourceBuffer = sourceBuffer;
        this.currentMime = mime;
    }

    get buffer() {
        return this.sourceBuffer;
    }

    reset(positionS: number) {
        this.abort.abort();
        this.abort = new AbortController();
        this.nextSeq = undefined;
        this.lastAppendedEndS = undefined;
        this.loadS = positionS;
        this.ended = false;
        this.hasAppended = false;
        this.transientErrors = 0;
        this.poke();
    }

    poke() {
        const w = this.wake;
        this.wake = undefined;
        w?.();
    }

    private waitForPoke(ms: number) {
        return new Promise<void>((resolve) => {
            const timeout = setTimeout(() => {
                this.wake = undefined;
                resolve();
            }, ms);
            this.wake = () => {
                clearTimeout(timeout);
                resolve();
            };
        });
    }

    stop() {
        this.running = false;
        this.abort.abort();
        this.poke();
    }

    private bufferedEndAround(time: number): number | undefined {
        const buffered = this.sourceBuffer?.buffered;
        if (!buffered) return undefined;
        for (let i = 0; i < buffered.length; i++) {
            if (time >= buffered.start(i) - 0.1 && time <= buffered.end(i) + 0.1)
                return buffered.end(i);
        }
        return undefined;
    }

    hasBuffered(time: number) {
        return this.bufferedEndAround(time) !== undefined;
    }

    async run() {
        this.running = true;
        while (this.running && !this.player.destroyed) {
            try {
                if (this.ended || !this.sourceBuffer) {
                    await this.waitForPoke(IDLE_POLL_MS * 4);
                    continue;
                }

                const video = this.player.video;
                const current = video.currentTime;
                const bufferedEnd = this.bufferedEndAround(current);
                const target = this.player.isLive ? LIVE_TARGET_AHEAD_S : VOD_TARGET_AHEAD_S;
                if (bufferedEnd !== undefined) {
                    if (bufferedEnd - current >= target) {
                        await this.waitForPoke(IDLE_POLL_MS);
                        continue;
                    }
                    const contiguous = this.lastAppendedEndS !== undefined && Math.abs(bufferedEnd - this.lastAppendedEndS) <= 0.25;
                    if (!contiguous)
                        this.nextSeq = undefined;
                    this.loadS = bufferedEnd;
                } else if (this.loadS + 0.5 < current) {
                    this.nextSeq = undefined;
                    this.loadS = current;
                }

                await this.loadNext(this.abort.signal);
                this.transientErrors = 0;
            } catch (e: any) {
                if (isAbort(e) || this.player.destroyed || !this.running)
                    continue;
                if (e instanceof UmpFatalError) {
                    this.player.fail(e.message, e.kind);
                    return;
                }
                this.transientErrors++;
                console.warn(`UMP ${this.role} loader error (${this.transientErrors}/${MAX_TRANSIENT_ERRORS})`, e);
                if (this.transientErrors >= MAX_TRANSIENT_ERRORS) {
                    this.player.fail(`UMP ${this.role} failed: ${e?.message ?? e}`, "error");
                    return;
                }
                await sleep(Math.min(500 * this.transientErrors, 3000), this.abort.signal);
            }
        }
    }

    private async loadNext(signal: AbortSignal) {
        const player = this.player;
        const params = new URLSearchParams({
            id: player.id,
            role: this.role,
            loadMs: Math.floor(this.loadS * 1000).toString(),
            playheadMs: Math.floor(player.video.currentTime * 1000).toString()
        });
        if (this.nextSeq !== undefined && this.currentKey !== undefined) {
            params.set("seq", this.nextSeq.toString());
            params.set("key", this.currentKey);
        }

        const resp = await player.fetch(`/Ump/Segment?${params.toString()}`, signal);
        if (resp.status === 504)
            return;
        if (resp.status === 204) {
            if (resp.headers.get("X-End") === "1") {
                this.ended = true;
                player.onTrackEnded();
            }
            return;
        }
        if (!resp.ok)
            throw new Error(`Segment request failed with HTTP ${resp.status}`);

        const key = resp.headers.get("X-Key") ?? "";
        const mime = resp.headers.get("X-Mime") ?? "";
        const codecs = resp.headers.get("X-Codecs") ?? "";
        const seq = parseInt(resp.headers.get("X-Seq") ?? "-1");
        const startUs = parseInt(resp.headers.get("X-Start-Us") ?? "0");
        const durationUs = parseInt(resp.headers.get("X-Duration-Us") ?? "0");
        const isEnd = resp.headers.get("X-End") === "1";
        const data = await resp.arrayBuffer();
        if (signal.aborted) return;

        if (key !== this.currentKey) {
            await this.switchFormat(key, mime, codecs, signal);
            if (signal.aborted) return;
        }

        await player.append(this.sourceBuffer!, data, signal);
        if (signal.aborted) return;

        this.nextSeq = seq + 1;
        const startS = startUs / 1_000_000;
        const endS = (startUs + durationUs) / 1_000_000;
        this.lastAppendedEndS = this.bufferedEndAround((startS + endS) / 2) ?? endS;
        this.loadS = Math.max(endS, this.lastAppendedEndS);
        this.hasAppended = true;

        if (isEnd) {
            this.ended = true;
            player.onTrackEnded();
        }
    }

    private async switchFormat(key: string, mime: string, codecs: string, signal: AbortSignal) {
        const player = this.player;
        const fullMime = umpFullMime({ mimeType: mime, codecs });
        const resp = await player.fetch(`/Ump/Init?id=${player.id}&role=${this.role}&key=${encodeURIComponent(key)}`, signal);
        if (resp.status === 504)
            throw new Error("Init segment timed out");
        if (!resp.ok && resp.status !== 204)
            throw new Error(`Init request failed with HTTP ${resp.status}`);

        if (this.currentMime !== fullMime && this.sourceBuffer && typeof this.sourceBuffer.changeType === "function") {
            await player.waitIdle(this.sourceBuffer);
            this.sourceBuffer.changeType(fullMime);
        }
        this.currentMime = fullMime;

        if (resp.status !== 204) {
            const init = await resp.arrayBuffer();
            if (signal.aborted) return;
            await player.append(this.sourceBuffer!, init, signal);
        }
        this.currentKey = key;
        const format = (this.role === "video" ? player.info?.videoFormats : player.info?.audioFormats)?.find(x => x.key === key);
        if (format) player.callbacks.onActiveFormatChanged?.(this.role, format);
    }
}

export class UmpPlayer {
    readonly id: string;
    info?: UmpInfo;
    destroyed = false;
    isLive = false;

    private mediaSource?: MediaSource;
    private objectUrl?: string;
    private loaders: TrackLoader[] = [];
    private videoFormats: UmpFormatInfo[] = [];
    private audioFormats: UmpFormatInfo[] = [];
    private selectedVideoKey?: string;
    private selectedAudioKey?: string;
    private liveTimer?: any;
    private gapTimer?: any;
    private trackElement?: HTMLTrackElement;
    private cueCounter = 0;
    private activeCues = new Map<TextTrackCue, string>();
    private failed = false;
    private configured = false;
    private readonly onSeekingHandler = () => this.onSeeking();
    private readonly onTimeUpdateHandler = () => this.loaders.forEach(x => x.poke());
    private readonly onWaitingHandler = () => this.jumpGap();

    private jumpGap() {
        if (this.destroyed || this.video.seeking || this.video.readyState >= HTMLMediaElement.HAVE_FUTURE_DATA)
            return;
        const t = this.video.currentTime;
        const buffered = this.video.buffered;
        for (let i = 0; i < buffered.length; i++) {
            const start = buffered.start(i);
            if (t >= start && t < buffered.end(i))
                return;
            if (start > t && start - t <= MAX_GAP_JUMP_S) {
                this.video.currentTime = start + 0.01;
                return;
            }
        }
    }

    constructor(readonly video: HTMLVideoElement, infoUrl: string, readonly callbacks: UmpPlayerCallbacks = {}) {
        const url = new URL(infoUrl, window.location.origin);
        this.id = url.searchParams.get("id") ?? "";
    }

    fetch(url: string, signal?: AbortSignal, init?: RequestInit) {
        return window.fetch(url, {
            ...init,
            signal,
            headers: { ...(init?.headers ?? {}), "WindowID": Globals.WindowID }
        }).then(async (resp) => {
            if (resp.status === 410) {
                let body: any = undefined;
                try { body = await resp.json(); } catch { }
                throw new UmpFatalError(body?.message ?? "UMP stream is no longer available", (body?.kind ?? "error") as UmpErrorKind);
            }
            if (resp.status === 404)
                throw new UmpFatalError("UMP playback session not found", "reload");
            return resp;
        });
    }

    private async getInfo(waitLive: boolean): Promise<UmpInfo> {
        const resp = await this.fetch(`/Ump/Info?id=${this.id}&waitLive=${waitLive}`);
        if (!resp.ok) throw new Error(`UMP info failed with HTTP ${resp.status}`);
        return await resp.json() as UmpInfo;
    }

    getVideoFormats() {
        return this.videoFormats;
    }

    getAudioFormats() {
        return this.audioFormats;
    }

    private pickAudio(): UmpFormatInfo[] {
        const selected = this.selectedAudioKey ? this.audioFormats.find(x => x.key === this.selectedAudioKey) : undefined;
        if (selected) return [selected];
        const supported = this.audioFormats.filter(x => !x.isDrc);
        const pool = supported.length > 0 ? supported : this.audioFormats;
        const original = pool.filter(x => x.original);
        const candidates = original.length > 0 ? original : pool;
        const rank = (f: UmpFormatInfo) => codecFamily(f.codecs) === "opus" ? 0 : codecFamily(f.codecs) === "aac" ? 1 : 2;
        const best = [...candidates].sort((a, b) => rank(a) - rank(b) || b.bitrate - a.bitrate)[0];
        return best ? [best] : [];
    }

    private pickVideo(): UmpFormatInfo[] {
        if (this.videoFormats.length === 0) return [];
        const families = new Map<string, UmpFormatInfo[]>();
        for (const f of this.videoFormats) {
            const family = codecFamily(f.codecs);
            if (!families.has(family)) families.set(family, []);
            families.get(family)!.push(f);
        }
        const familyRank = (family: string) => family === "vp9" ? 0 : family === "avc" ? 1 : family === "av1" ? 2 : 3;
        const bestFamily = [...families.entries()].sort((a, b) => {
            const maxA = Math.max(...a[1].map(x => x.height));
            const maxB = Math.max(...b[1].map(x => x.height));
            return (maxB - maxA) || (familyRank(a[0]) - familyRank(b[0]));
        })[0][1];

        const byQuality = (list: UmpFormatInfo[]) => [...list].sort((a, b) => (b.height - a.height) || (b.fps - a.fps) || (b.bitrate - a.bitrate));

        const selected = this.selectedVideoKey ? this.videoFormats.find(x => x.key === this.selectedVideoKey) : undefined;
        if (selected) return [selected];

        const screenHeight = Math.max(window.screen?.height ?? 1080, 720) * (window.devicePixelRatio ?? 1);
        const capped = bestFamily.filter(x => x.height <= Math.max(screenHeight, 1080));
        return byQuality(capped.length > 0 ? capped : bestFamily);
    }

    private async configure(startS: number) {
        const video = this.pickVideo();
        const audio = this.pickAudio();
        const resp = await this.fetch(`/Ump/Configure?id=${this.id}`, undefined, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                videoKeys: video.map(x => x.key),
                audioKeys: audio.map(x => x.key),
                viewportWidth: Math.round((this.video.clientWidth || 1920) * (window.devicePixelRatio ?? 1)),
                viewportHeight: Math.round((this.video.clientHeight || 1080) * (window.devicePixelRatio ?? 1)),
                startMs: Math.floor(startS * 1000)
            })
        });
        if (resp.status === 400) {
            let body: any = undefined;
            try { body = await resp.json(); } catch { }
            throw new UmpFatalError(body?.message ?? "No playable UMP formats", "unsupported");
        }
        if (!resp.ok) throw new Error(`UMP configure failed with HTTP ${resp.status}`);
        this.configured = true;
    }

    async start(startS: number) {
        try {
            const mediaSource = new MediaSource();
            this.mediaSource = mediaSource;
            this.objectUrl = URL.createObjectURL(mediaSource);
            const opened = new Promise<void>((resolve) => mediaSource.addEventListener("sourceopen", () => resolve(), { once: true }));
            this.video.src = this.objectUrl;

            const info = await this.getInfo(false);
            if (this.destroyed) return;
            this.info = info;
            this.isLive = info.isLive;
            this.videoFormats = info.videoFormats.filter(umpIsSupported);
            this.audioFormats = info.audioFormats.filter(umpIsSupported);
            if (this.videoFormats.length === 0 && this.audioFormats.length === 0)
                throw new UmpFatalError("None of the stream formats can be decoded by this player", "unsupported");
            this.callbacks.onFormatsChanged?.(this.videoFormats, this.audioFormats);

            await this.configure(this.isLive ? 0 : startS);
            if (this.destroyed) return;

            let positionS = startS;
            if (this.isLive) {
                const liveInfo = await this.getInfo(true);
                if (this.destroyed) return;
                this.info = liveInfo;
                positionS = (liveInfo.liveEdgeStartMs ?? 0) / 1000;
            }

            await opened;
            if (this.destroyed) return;

            if (!this.isLive && this.info.durationMs > 0)
                mediaSource.duration = this.info.durationMs / 1000;
            else if (this.isLive)
                mediaSource.duration = Infinity;

            const videoPick = this.pickVideo()[0];
            const audioPick = this.pickAudio()[0];
            if (videoPick) {
                const loader = new TrackLoader(this, "video", positionS);
                const mime = umpFullMime(videoPick);
                loader.attach(mediaSource.addSourceBuffer(mime), mime);
                this.loaders.push(loader);
            }
            if (audioPick) {
                const loader = new TrackLoader(this, "audio", positionS);
                const mime = umpFullMime(audioPick);
                loader.attach(mediaSource.addSourceBuffer(mime), mime);
                this.loaders.push(loader);
            }

            if (this.isLive) {
                const offset = -(this.info.mediaBaseMs ?? 0) / 1000;
                for (const loader of this.loaders)
                    loader.buffer!.timestampOffset = offset;
                this.updateLiveWindow(this.info);
                this.liveTimer = setInterval(() => this.pollLive(), LIVE_INFO_POLL_MS);
            }

            if (this.info.subtitleUrl)
                this.attachSubtitles(this.info.subtitleUrl);

            this.video.addEventListener("seeking", this.onSeekingHandler);
            this.video.addEventListener("timeupdate", this.onTimeUpdateHandler);
            this.video.addEventListener("waiting", this.onWaitingHandler);
            this.gapTimer = setInterval(() => this.jumpGap(), GAP_CHECK_MS);
            if (positionS > 0)
                this.video.currentTime = positionS;
            for (const loader of this.loaders)
                loader.run();
        } catch (e: any) {
            if (e instanceof UmpFatalError)
                this.fail(e.message, e.kind);
            else
                this.fail(`UMP start failed: ${e?.message ?? e}`, "error");
        }
    }

    private attachSubtitles(url: string) {
        const track = document.createElement("track");
        track.kind = "subtitles";
        track.src = url;
        track.default = true;
        this.video.appendChild(track);
        this.trackElement = track;
        const textTrack = track.track;
        textTrack.mode = "hidden";
        textTrack.addEventListener("cuechange", () => {
            const active = new Set<TextTrackCue>();
            const cues = textTrack.activeCues;
            if (cues) {
                for (let i = 0; i < cues.length; i++)
                    active.add(cues[i]);
            }
            for (const [cue, id] of [...this.activeCues.entries()]) {
                if (!active.has(cue)) {
                    this.activeCues.delete(cue);
                    this.callbacks.onCueExit?.(id);
                }
            }
            for (const cue of active) {
                if (!this.activeCues.has(cue)) {
                    const id = `ump-cue-${this.cueCounter++}`;
                    this.activeCues.set(cue, id);
                    this.callbacks.onCueEnter?.(id, (cue as VTTCue).text ?? "");
                }
            }
        });
    }

    private async pollLive() {
        if (this.destroyed) return;
        try {
            const info = await this.getInfo(false);
            this.info = { ...this.info!, ...info };
            this.updateLiveWindow(info);
        } catch (e: any) {
            if (e instanceof UmpFatalError)
                this.fail(e.message, e.kind);
        }
    }

    private updateLiveWindow(info: UmpInfo) {
        if (info.windowStartMs === undefined || info.windowEndMs === undefined || info.windowStartMs === null || info.windowEndMs === null)
            return;
        const start = Math.max(0, info.windowStartMs / 1000);
        const end = info.windowEndMs / 1000;
        if (end <= start)
            return;
        try {
            if (this.mediaSource?.readyState === "open" && typeof this.mediaSource.setLiveSeekableRange === "function")
                this.mediaSource.setLiveSeekableRange(start, end);
        } catch (e) {
            console.warn("Failed to set live seekable range", e);
        }
    }

    private onSeeking() {
        if (this.destroyed || this.loaders.length === 0) return;
        const target = this.video.currentTime;
        if (this.loaders.every(x => !x.hasAppended && Math.abs(x.loadPositionS - target) < 1))
            return;
        const allBuffered = this.loaders.every(x => x.hasBuffered(target));
        if (allBuffered) {
            this.loaders.forEach(x => x.poke());
            return;
        }
        this.fetch(`/Ump/Seek?id=${this.id}&ms=${Math.floor(target * 1000)}`, undefined, { method: "POST" }).catch((e) => {
            if (e instanceof UmpFatalError) this.fail(e.message, e.kind);
        });
        for (const loader of this.loaders)
            loader.reset(target);
    }

    async setVideoFormat(key?: string) {
        if (this.selectedVideoKey === key) return;
        this.selectedVideoKey = key;
        await this.applySelection("video", 2);
    }

    async setAudioFormat(key?: string) {
        if (this.selectedAudioKey === key) return;
        this.selectedAudioKey = key;
        await this.applySelection("audio", 0.5);
    }

    private async applySelection(role: Role, keepAheadS: number) {
        if (!this.configured || this.destroyed) return;
        try {
            await this.configure(this.video.currentTime);
            const current = this.video.currentTime;
            const loader = this.loaders.find(x => x.role === role);
            if (loader?.buffer) {
                await this.waitIdle(loader.buffer);
                const buffered = loader.buffer.buffered;
                const keepUntil = current + keepAheadS;
                if (buffered.length > 0 && buffered.end(buffered.length - 1) > keepUntil) {
                    loader.buffer.remove(keepUntil, Infinity);
                    await this.waitIdle(loader.buffer);
                }
                loader.reset(Math.min(keepUntil, buffered.length > 0 ? buffered.end(buffered.length - 1) : current));
            }
        } catch (e: any) {
            if (e instanceof UmpFatalError) this.fail(e.message, e.kind);
            else console.warn(`Failed to change UMP ${role} format`, e);
        }
    }

    waitIdle(sourceBuffer: SourceBuffer) {
        if (!sourceBuffer.updating) return Promise.resolve();
        return new Promise<void>((resolve) => sourceBuffer.addEventListener("updateend", () => resolve(), { once: true }));
    }

    private async evict(sourceBuffer: SourceBuffer, aggressive: boolean) {
        const current = this.video.currentTime;
        const keep = aggressive ? 5 : KEEP_BEHIND_S;
        const buffered = sourceBuffer.buffered;
        if (buffered.length === 0) return;
        const start = buffered.start(0);
        const removeEnd = current - keep;
        if (removeEnd > start + 1) {
            await this.waitIdle(sourceBuffer);
            sourceBuffer.remove(start, removeEnd);
            await this.waitIdle(sourceBuffer);
        }
        if (aggressive && buffered.length > 0) {
            const end = buffered.end(buffered.length - 1);
            if (end > current + 30) {
                await this.waitIdle(sourceBuffer);
                sourceBuffer.remove(current + 30, end);
                await this.waitIdle(sourceBuffer);
            }
        }
    }

    async append(sourceBuffer: SourceBuffer, data: ArrayBuffer, signal: AbortSignal) {
        if (this.destroyed || signal.aborted) return;
        await this.evict(sourceBuffer, false);
        for (let attempt = 0; attempt < 3; attempt++) {
            if (this.destroyed || signal.aborted) return;
            await this.waitIdle(sourceBuffer);
            try {
                sourceBuffer.appendBuffer(data);
                await this.waitIdle(sourceBuffer);
                return;
            } catch (e: any) {
                if (e?.name === "QuotaExceededError") {
                    await this.evict(sourceBuffer, true);
                    continue;
                }
                throw e;
            }
        }
        throw new Error("Source buffer is full");
    }

    onTrackEnded() {
        if (this.destroyed || !this.mediaSource) return;
        if (this.loaders.every(x => x.ended) && this.mediaSource.readyState === "open") {
            Promise.all(this.loaders.map(x => x.buffer ? this.waitIdle(x.buffer) : Promise.resolve())).then(() => {
                try {
                    if (this.mediaSource?.readyState === "open" && this.loaders.every(x => x.ended))
                        this.mediaSource.endOfStream();
                } catch (e) {
                    console.warn("endOfStream failed", e);
                }
            });
        }
    }

    fail(message: string, kind: UmpErrorKind) {
        if (this.failed || this.destroyed) return;
        this.failed = true;
        console.error("UMP playback failed", { message, kind });
        for (const loader of this.loaders)
            loader.stop();
        this.callbacks.onError?.(message, true, kind);
    }

    destroy() {
        if (this.destroyed) return;
        this.destroyed = true;
        if (this.liveTimer) clearInterval(this.liveTimer);
        if (this.gapTimer) clearInterval(this.gapTimer);
        this.video.removeEventListener("seeking", this.onSeekingHandler);
        this.video.removeEventListener("timeupdate", this.onTimeUpdateHandler);
        this.video.removeEventListener("waiting", this.onWaitingHandler);
        for (const loader of this.loaders)
            loader.stop();
        this.loaders = [];
        for (const id of this.activeCues.values())
            this.callbacks.onCueExit?.(id);
        this.activeCues.clear();
        this.trackElement?.remove();
        this.trackElement = undefined;
        try {
            if (this.mediaSource?.readyState === "open")
                this.mediaSource.endOfStream();
        } catch { }
        if (this.objectUrl)
            URL.revokeObjectURL(this.objectUrl);
        this.mediaSource = undefined;
        window.fetch(`/Ump/Close?id=${this.id}`, { method: "POST", headers: { "WindowID": Globals.WindowID } }).catch(() => { });
    }
}
