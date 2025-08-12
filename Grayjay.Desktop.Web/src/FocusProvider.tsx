import { createContext, useContext, createSignal, onCleanup, createEffect, Accessor, JSX, batch } from "solid-js";
import { Direction, Press, FocusableOptions, ScopeOptions, uid, isVisible, isFocusable, OpenIntent } from "./nav";
import { useNavigate } from "@solidjs/router";

type NodeId = string;

interface NodeEntry {
    id: NodeId;
    el: HTMLElement;
    scope: string;
    opts: FocusableOptions;
}

interface ScopeEntry {
    id: string;
    parent?: string;
    opts: Required<Pick<ScopeOptions, "orientation" | "wrap" | "trap">> & ScopeOptions;
    nodes: Set<NodeId>;
    activeNode?: NodeId;
}

type Idx = {
    nodes: Map<NodeId, NodeEntry>;
    scopes: Map<string, ScopeEntry>;
    nodeByEl: WeakMap<HTMLElement, NodeId>;
    scopeByEl: WeakMap<HTMLElement, string>;
};

function createIndex(): Idx {
    return {
        nodes: new Map(),
        scopes: new Map(),
        nodeByEl: new WeakMap(),
        scopeByEl: new WeakMap(),
    };
}

export interface FocusAPI {
    registerScope: (el: HTMLElement, opts?: ScopeOptions, parentScopeId?: string) => string;
    unregisterScope: (id: string) => void;
    registerNode: (el: HTMLElement, scopeId: string, opts: FocusableOptions) => string;
    unregisterNode: (id: string) => void;
    setNodeOptions: (id: string, opts: Partial<FocusableOptions>) => void;
    navigate: (dir: Direction) => void;
    press: (kind: Press, openIntent: OpenIntent) => void;
    focusFirstInScope: (scopeId: string) => void;
    setActiveScope: (id: string | null) => void;
    getActiveScope: Accessor<string | null>;
    resolveScopeId: (el: HTMLElement) => string | null;
}

const FocusCtx = createContext<FocusAPI>();

export function useFocus(): FocusAPI {
    const ctx = useContext(FocusCtx);
    if (!ctx) throw new Error("FocusProvider missing");
    return ctx;
}

export function FocusProvider(props: { children: JSX.Element }) {
    const navigate = useNavigate();

    const index = createIndex();
    const [activeScope, setActiveScope] = createSignal<string | null>(null);

    function registerScope(el: HTMLElement, opts?: ScopeOptions, parentScopeId?: string) {
        const id = opts?.id ?? uid("scope");
        const rec: ScopeEntry = {
            id,
            parent: parentScopeId ?? activeScope() ?? undefined,
            opts: {
                orientation: opts?.orientation ?? "spatial",
                wrap: !!(opts?.wrap ?? opts?.loop),
                trap: !!opts?.trap,
                ...opts,
            },
            nodes: new Set(),
        };
        index.scopes.set(id, rec);
        index.scopeByEl.set(el, id);

        if (rec.opts.trap) setActiveScope(id);
        return id;
    }

    function unregisterScope(id: string) {
        const s = index.scopes.get(id);
        if (!s) return;
        for (const nid of s.nodes) index.nodes.delete(nid);
        index.scopes.delete(id);
        if (activeScope() === id) setActiveScope(s.parent ?? null);
    }

    function registerNode(el: HTMLElement, scopeId: string, opts: FocusableOptions) {
        const id = uid("node");
        const entry: NodeEntry = { id, el, scope: scopeId, opts: { order: 0, priority: 0, roving: false, ...opts } };
        index.nodes.set(id, entry);
        index.nodeByEl.set(el, id);
        const scope = index.scopes.get(scopeId);
        if (scope) {
            scope.nodes.add(id);
            if (entry.opts.roving) {
                el.tabIndex = -1;
                if (!scope.activeNode) scope.activeNode = id;
                const inert = !!entry.opts.focusInert?.();
                if (scope.activeNode === id && !inert) el.tabIndex = 0;
            }
        }
        if (!entry.opts.roving) {
            if (!entry.opts.focusInert?.() && el.tabIndex < 0) el.tabIndex = 0;
        }
        return id;
    }

    function unregisterNode(id: string) {
        const rec = index.nodes.get(id);
        if (!rec) return;
        index.nodes.delete(id);
        const scope = index.scopes.get(rec.scope);
        scope?.nodes.delete(id);
        if (scope?.activeNode === id) scope.activeNode = undefined;
    }

    function setNodeOptions(id: string, opts: Partial<FocusableOptions>) {
        const rec = index.nodes.get(id);
        if (!rec) return;
        rec.opts = { ...rec.opts, ...opts };
        const scope = index.scopes.get(rec.scope);
        if (rec.opts.roving) {
            const inert = !!rec.opts.focusInert?.();
            if (scope?.activeNode === rec.id) rec.el.tabIndex = inert ? -1 : 0;
        } else {
            if (!rec.opts.focusInert?.() && rec.el.tabIndex < 0) rec.el.tabIndex = 0;
            if (rec.opts.focusInert?.()) rec.el.tabIndex = -1;
        }
    }

    function candidatesInScope(scopeId: string): NodeEntry[] {
        const s = index.scopes.get(scopeId);
        if (!s) return [];
        return [...s.nodes]
            .map((id) => index.nodes.get(id)!)
            .filter(Boolean)
            .filter((n) => !n.opts.disabled && isVisible(n.el));
    }

    function currentFocused(): NodeEntry | undefined {
        const active = document.activeElement as HTMLElement | null;
        if (!active) return;
        const id = index.nodeByEl.get(active);
        if (!id) return;
        return index.nodes.get(id);
    }

    function scopeOf(el: HTMLElement | null): ScopeEntry | undefined {
        if (!el) return;
        let cur: HTMLElement | null = el;
        while (cur) {
            const sid = index.scopeByEl.get(cur);
            if (sid) return index.scopes.get(sid);
            cur = cur.parentElement;
        }
        const as = activeScope();
        return as ? index.scopes.get(as) : undefined;
    }

    function spatialNext(from: HTMLElement, dir: Direction, scopeId: string): NodeEntry | undefined {
        const set = candidatesInScope(scopeId).filter((n) => n.el !== from);
        if (set.length === 0) return;
        const fromRect = from.getBoundingClientRect();
        const vec: Record<Direction, [number, number]> = {
            left: [-1, 0], right: [1, 0], up: [0, -1], down: [0, 1], next: [1, 0], prev: [-1, 0],
        };
        const [vx, vy] = vec[dir];

        type Cand = { n: NodeEntry; score: number };
        const cands: Cand[] = [];

        for (const n of set) {
            const rect = (n.opts.getRect?.(n.el) ?? n.el.getBoundingClientRect());
            const cx = rect.left + rect.width / 2 - (fromRect.left + fromRect.width / 2);
            const cy = rect.top + rect.height / 2 - (fromRect.top + fromRect.height / 2);
            const dot = cx * vx + cy * vy;
            if (dot <= 0 && (dir !== "next" && dir !== "prev")) continue;

            const fwd = Math.abs(vx) > 0 ? Math.abs(cx) : Math.abs(cy);
            const perp = Math.abs(vx) > 0 ? Math.abs(cy) : Math.abs(cx);

            let score = fwd + perp * 2;

            score -= (n.opts.priority ?? 0) * 1000;
            score += (n.opts.order ?? 0) * 0.001;

            cands.push({ n, score });
        }

        if (cands.length === 0) {
            if (dir === "next" || dir === "prev") {
                const all = candidatesInScope(scopeId).sort((a, b) => (a.opts.order ?? 0) - (b.opts.order ?? 0));
                const idx = all.findIndex((x) => x.el === from);
                if (idx >= 0) {
                    const nextIdx = dir === "next" ? idx + 1 : idx - 1;
                    return all[nextIdx];
                }
            }
            return;
        }

        cands.sort((a, b) => a.score - b.score);
        return cands[0].n;
    }

    function focusNode(node?: NodeEntry) {
        if (!node) return;
        const scope = index.scopes.get(node.scope);
        if (scope) {
            if (scope.activeNode !== node.id) {
                scope.activeNode = node.id;
                for (const id of scope.nodes) {
                    const n = index.nodes.get(id)!;
                    if (!n?.el) continue;
                    if (n.opts.roving) n.el.tabIndex = (n.id === node.id) ? 0 : -1;
                }
            }
        }
        node.el.focus({ preventScroll: true });
        node.el.scrollIntoView({ block: "nearest", inline: "nearest" });
    }

    function findScopeForNavigation(): ScopeEntry | undefined {
        const current = currentFocused();
        if (current) return index.scopes.get(current.scope);
        const as = activeScope();
        if (as) return index.scopes.get(as);
        return [...index.scopes.values()][0];
    }

    function navigateDirection(dir: Direction) {
        const focused = currentFocused();
        const scope = findScopeForNavigation();
        if (!scope) return;

        if (!focused) {
            focusFirstInScope(scope.id);
            return;
        }

        let next = spatialNext(focused.el, dir, scope.id);
        if (!next && (dir === "next" || dir === "prev") && scope.opts.wrap) {
            const all = candidatesInScope(scope.id).sort((a, b) => (a.opts.order ?? 0) - (b.opts.order ?? 0));
            if (all.length) next = dir === "next" ? all[0] : all[all.length - 1];
        }

        if (next) {
            focusNode(next);
            return;
        }

        let parentId = scope.parent;
        while (parentId) {
            const parent = index.scopes.get(parentId);
            if (!parent) break;
            const cand = focused ? spatialNext(focused.el, dir, parent.id) : undefined;
            if (cand) {
                focusNode(cand);
                return;
            }
            parentId = parent.parent;
        }
    }

    function press(kind: Press, openIntent: OpenIntent) {
        const node = currentFocused();
        if (!node) {
            if (kind === "back") {
                //TODO: Fix
                //navigate(-1);
            }
            return;
        }

        if (kind === "press") node.opts.onPress?.(node.el, openIntent);
        else if (kind === "back") {
            const backResult = node.opts.onBack?.(node.el, openIntent);
            if (backResult !== true) {
                //TODO: Fix
                //navigate(-1);
            }
        }
        else if (kind === "options") node.opts.onOptions?.(node.el, openIntent);
    }

    function focusFirstInScope(scopeId: string) {
        const s = index.scopes.get(scopeId);
        if (!s) return;

        const el = s.opts.defaultFocus?.();
        if (el && isFocusable(el)) {
            const id = index.nodeByEl.get(el);
            const n = id ? index.nodes.get(id) : undefined;
            if (n) return focusNode(n);
        }

        const first = candidatesInScope(scopeId)
            .filter(n => !n.opts.focusInert?.())
            .sort((a, b) => (b.opts.priority ?? 0) - (a.opts.priority ?? 0) || (a.opts.order ?? 0) - (b.opts.order ?? 0))[0];

        if (first) focusNode(first);
    }

    function resolveScopeId(el: HTMLElement): string | null {
            return index.scopeByEl.get(el) ?? null;
    }

    function isEditable(el: EventTarget | null): el is HTMLInputElement | HTMLTextAreaElement | HTMLElement {
        if (!el || !(el as HTMLElement).tagName) return false;
        const t = el as HTMLElement;
        return t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable === true;
    }

    function inputType(el: HTMLInputElement) {
        return (el.getAttribute('type') || 'text').toLowerCase();
    }
    function isSingleLineTextInput(el: HTMLInputElement) {
        return ['text','search','email','url','tel','password'].includes(inputType(el));
    }
    function isNumericishInput(el: HTMLInputElement) {
        return ['number','range','date','time','month','week','datetime-local','color'].includes(inputType(el));
    }
    function hasSelection(el: HTMLInputElement | HTMLTextAreaElement) {
        return (el.selectionStart ?? 0) !== (el.selectionEnd ?? 0);
    }
    function caretAtBoundary(el: HTMLInputElement | HTMLTextAreaElement, dir: 'left'|'right') {
        const start = el.selectionStart ?? 0;
        const end = el.selectionEnd ?? start;
        const len = el.value?.length ?? 0;
        if (dir === 'left') return !hasSelection(el) && start <= 0;
        return !hasSelection(el) && end >= len;
    }
    function textareaCanMove(el: HTMLTextAreaElement, dir: 'up'|'down') {
        const start = el.selectionStart ?? 0;
        const end = el.selectionEnd ?? start;
        if (dir === 'up') return el.value.slice(0, start).includes('\n');
        return el.value.slice(end).includes('\n');
    }
    function isTypingKey(e: KeyboardEvent) {
        if (e.key.length === 1) return true;
        return ['Backspace','Delete','Home','End','PageUp','PageDown'].includes(e.key);
    }

    function editableWantsKey(e: KeyboardEvent, trap: boolean) {
        const t = e.target as HTMLElement;
        if (!isEditable(t)) return false;
        if (e.ctrlKey || e.metaKey || e.altKey || isTypingKey(e)) return true;
        if (['w','a','s','d','W','A','S','D'].includes(e.key)) return true;

        if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') {
            const el = t as HTMLInputElement | HTMLTextAreaElement;
            return !caretAtBoundary(el, e.key === 'ArrowLeft' ? 'left' : 'right');
        }

        if (e.key === 'ArrowUp' || e.key === 'ArrowDown') {
            if (t.tagName === 'INPUT') {
                const el = t as HTMLInputElement;
                if (isNumericishInput(el)) return true;
                return false;
            }
            if (t.tagName === 'TEXTAREA') {
                const ta = t as HTMLTextAreaElement;
                return textareaCanMove(ta, e.key === 'ArrowUp' ? 'up' : 'down');
            }
            return true;
        }

        return true;
    }

    function onKeyDown(e: KeyboardEvent) {
        const as = activeScope();
        const trap = as ? (index.scopes.get(as)?.opts.trap ?? false) : false;
        const editable = isEditable(e.target);

        if (e.key === 'Tab' && trap) {
            navigateDirection(e.shiftKey ? 'prev' : 'next');
            e.preventDefault();
            return;
        }

        if (editable && editableWantsKey(e, trap)) return;

        switch (e.key) {
            case 'ArrowUp': navigateDirection('up'); e.preventDefault(); break;
            case 'ArrowDown': navigateDirection('down'); e.preventDefault(); break;
            case 'ArrowLeft': navigateDirection('left'); e.preventDefault(); break;
            case 'ArrowRight': navigateDirection('right'); e.preventDefault(); break;
            case 'Enter':
            case ' ':
                press('press', OpenIntent.Keyboard); e.preventDefault(); break;
            case 'Escape':
                press('back', OpenIntent.Keyboard); e.preventDefault(); break;
            case 'o':
                if (!editable && !e.altKey && !e.metaKey) { press('options', OpenIntent.Keyboard); e.preventDefault(); }
                break;
            default:
                if (!editable) {
                    if (e.key === 'w') { navigateDirection('up'); e.preventDefault(); }
                    else if (e.key === 's') { navigateDirection('down'); e.preventDefault(); }
                    else if (e.key === 'a') { navigateDirection('left'); e.preventDefault(); }
                    else if (e.key === 'd') { navigateDirection('right'); e.preventDefault(); }
                }
                break;
        }
    }


    let raf = 0;
    let lastMove = 0;
    const initialDelay = 220; // ms before repeat
    const repeatDelay = 90;
    const axisThreshold = 0.45;
    const btn = {
        A: 0, B: 1, X: 2, Y: 3,
        L: 4, R: 5, LT: 6, RT: 7,
        SELECT: 8, START: 9,
        UP: 12, DOWN: 13, LEFT: 14, RIGHT: 15,
    } as const;

    type PadState = {
        dirHeld?: Direction;
        lastFire?: number;
        pressed: Set<number>;
    };
    const padState: PadState = { pressed: new Set() };

    function pollGamepads(ts: number) {
        const pads = navigator.getGamepads?.() ?? [];
        const gp = pads.find(Boolean);
        if (gp) {
            const lx = gp.axes[0] ?? 0;
            const ly = gp.axes[1] ?? 0;
            const now = ts;

            const horiz = Math.abs(lx) > axisThreshold ? (lx > 0 ? "right" : "left") as Direction : undefined;
            const vert    = Math.abs(ly) > axisThreshold ? (ly > 0 ? "down" : "up") as Direction : undefined;
            const stickDir = vert ?? horiz;

            const dpadDir =
                gp.buttons[btn.UP]?.pressed ? "up" :
                gp.buttons[btn.DOWN]?.pressed ? "down" :
                gp.buttons[btn.LEFT]?.pressed ? "left" :
                gp.buttons[btn.RIGHT]?.pressed ? "right" : undefined;

            const dir = dpadDir ?? stickDir;

            if (dir) {
                if (padState.dirHeld !== dir) {
                    padState.dirHeld = dir;
                    padState.lastFire = now;
                    navigateDirection(dir);
                } else {
                    const delay = padState.lastFire ? (now - padState.lastFire) : Infinity;
                    if (delay >= (initialDelay)) {
                        navigateDirection(dir);
                        padState.lastFire = now - (initialDelay - repeatDelay);
                    }
                }
            } else {
                padState.dirHeld = undefined;
                padState.lastFire = undefined;
            }

            const pressBtn = gp.buttons[btn.A]?.pressed;
            const backBtn = gp.buttons[btn.B]?.pressed;
            const optionsBtn = gp.buttons[btn.X]?.pressed;
            const startBtn = gp.buttons[btn.START]?.pressed;

            if (pressBtn && !padState.pressed.has(btn.A)) { press("press", OpenIntent.Gamepad); padState.pressed.add(btn.A); }
            if (!pressBtn) padState.pressed.delete(btn.A);

            if (backBtn && !padState.pressed.has(btn.B)) { press("back", OpenIntent.Gamepad); padState.pressed.add(btn.B); }
            if (!backBtn) padState.pressed.delete(btn.B);

            if (optionsBtn && !padState.pressed.has(btn.X)) { press("options", OpenIntent.Gamepad); padState.pressed.add(btn.X); }
            if (!optionsBtn) padState.pressed.delete(btn.X);

            if (startBtn && !padState.pressed.has(btn.START)) { padState.pressed.add(btn.START); }
            if (!startBtn) padState.pressed.delete(btn.START);
        }
        raf = requestAnimationFrame(pollGamepads);
    }

    createEffect(() => {
        window.addEventListener("keydown", onKeyDown, { capture: true });
        raf = requestAnimationFrame(pollGamepads);
        onCleanup(() => {
            window.removeEventListener("keydown", onKeyDown, { capture: true } as any);
            cancelAnimationFrame(raf);
        });
    });


    const api: FocusAPI = {
        registerScope,
        unregisterScope,
        registerNode,
        unregisterNode,
        setNodeOptions,
        navigate: navigateDirection,
        press,
        focusFirstInScope,
        setActiveScope,
        getActiveScope: activeScope,
        resolveScopeId
    };

    return <FocusCtx.Provider value={api}>{props.children}</FocusCtx.Provider>;
}