import { createContext, useContext, createSignal, onCleanup, createEffect, Accessor, JSX } from "solid-js";
import { Direction, Press, FocusableOptions, ScopeOptions, uid, isVisible, isFocusable, OpenIntent } from "./nav";
import { useLocation, useNavigate } from "@solidjs/router";
import { useVideo, VideoState } from "./contexts/VideoProvider";

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
    hadFocus?: boolean;
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

export function useFocus(): FocusAPI | undefined {
    return useContext(FocusCtx);
}

export function FocusProvider(props: { children: JSX.Element }) {
    const navigate = useNavigate();
    const video = useVideo();
    const location = useLocation();
    const index = createIndex();
    const [activeScope, setActiveScope] = createSignal<string | null>(null);

    function registerScope(el: HTMLElement, opts?: ScopeOptions, parentScopeId?: string) {
        const id = opts?.id ?? uid("scope");
        const rec: ScopeEntry = {
            id,
            parent: parentScopeId ?? activeScope() ?? undefined,
            opts: {
                orientation: opts?.orientation ?? "spatial",
                wrap: !!opts?.wrap,
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
        for (const nid of s.nodes) {
            const n = index.nodes.get(nid);
            if (n) index.nodeByEl.delete(n.el);
            index.nodes.delete(nid);
        }
        index.scopes.delete(id);
        if (activeScope() === id) {
            const parentId = s.parent ?? null;
            setActiveScope(parentId);
            if (parentId) {
                queueMicrotask(() => focusFirstInScope(parentId));
            }
        }
    }

    function registerNode(el: HTMLElement, scopeId: string, opts: FocusableOptions) {
        const id = uid("node");
        const entry: NodeEntry = { id, el, scope: scopeId, opts: { priority: 0, ...opts } };
        index.nodes.set(id, entry);
        index.nodeByEl.set(el, id);

        const scope = index.scopes.get(scopeId);
        if (scope) scope.nodes.add(id);

        const inert = !!entry.opts.focusInert?.();
        if (inert) el.tabIndex = -1;
        else if (el.tabIndex < 0) el.tabIndex = 0;

        return id;
    }


    function unregisterNode(id: string) {
        const rec = index.nodes.get(id);
        if (!rec) return;
        index.nodeByEl.delete(rec.el);
        index.nodes.delete(id);
        const scope = index.scopes.get(rec.scope);
        scope?.nodes.delete(id);
        if (scope?.activeNode === id) scope.activeNode = undefined;
    }

    function setNodeOptions(id: string, opts: Partial<FocusableOptions>) {
        const rec = index.nodes.get(id);
        if (!rec) return;
        rec.opts = { ...rec.opts, ...opts };
        const inert = !!rec.opts.focusInert?.();
        if (inert) rec.el.tabIndex = -1;
        else if (rec.el.tabIndex < 0) rec.el.tabIndex = 0;
    }

    function candidatesInScope(scopeId: string): NodeEntry[] {
        const s = index.scopes.get(scopeId);
        if (!s) return [];
        return [...s.nodes]
            .map((id) => index.nodes.get(id)!)
            .filter(Boolean)
            .filter((n) => !n.opts.disabled && isVisible(n.el) && !n.opts.focusInert?.());
    }

    function findNodeFromElement(el: HTMLElement | null): NodeEntry | undefined {
        let cur: HTMLElement | null = el;
        while (cur) {
            const id = index.nodeByEl.get(cur);
            if (id) return index.nodes.get(id);
            cur = cur.parentElement;
        }
        return undefined;
    }

    function currentFocused(): NodeEntry | undefined {
        return findNodeFromElement(document.activeElement as HTMLElement | null);
    }

    function rectOf(n: NodeEntry): DOMRect {
        return (n.opts.getRect?.(n.el) ?? n.el.getBoundingClientRect());
    }

    function sweepCandidates(scopeId: string, axis: 'x'|'y'|'auto' = 'auto', fromEl?: HTMLElement): NodeEntry[] {
        const s = index.scopes.get(scopeId);
        if (!s) return [];
        const axisResolved: 'x'|'y' =
            axis === 'auto' ? (s.opts.orientation === 'horizontal' ? 'x' : 'y') : axis;

        const EPS = 2;

        let cands = candidatesInScope(scopeId);
        if (fromEl) {
          const cont = nearestScrollContainer(fromEl);
          const same = cands.filter(n => nearestScrollContainer(n.el) === cont);
          if (same.length) cands = same;
        }

        return cands
            .sort((A, B) => {
            const a = rectOf(A);
            const b = rectOf(B);
            if (axisResolved === 'y') {
                if (Math.abs(a.top - b.top) > EPS) return a.top - b.top;
                return a.left - b.left;
            } else {
                if (Math.abs(a.left - b.left) > EPS) return a.left - b.left;
                return a.top - b.top;
            }
            });
    }

    function overlapRatioY(a: DOMRect, b: DOMRect) {
        const top = Math.max(a.top, b.top);
        const bottom = Math.min(a.bottom, b.bottom);
        const overlap = Math.max(0, bottom - top);
        return overlap / Math.min(a.height, b.height);
    }

    function overlapRatioX(a: DOMRect, b: DOMRect) {
        const left = Math.max(a.left, b.left);
        const right = Math.min(a.right, b.right);
        const overlap = Math.max(0, right - left);
        return overlap / Math.min(a.width, b.width);
    }

    function isScrollContainer(el: Element | null): boolean {
        if (!el || !(el instanceof HTMLElement)) return false;
        const s = getComputedStyle(el);
        const oy = s.overflowY;
        const ox = s.overflowX;
        const scrollY = (oy === 'auto' || oy === 'scroll') && el.scrollHeight > el.clientHeight;
        const scrollX = (ox === 'auto' || ox === 'scroll') && el.scrollWidth > el.clientWidth;
        return scrollY || scrollX;
    }
        
    function nearestScrollContainer(el: HTMLElement | null): HTMLElement {
        let cur: HTMLElement | null = el?.parentElement ?? null;
        while (cur) {
            if (isScrollContainer(cur)) break;
            cur = cur.parentElement;
        }
        return (cur ?? (document.scrollingElement as HTMLElement) ?? document.documentElement);
    }

    function containerViewportRect(container: HTMLElement): DOMRect {
        const root = document.scrollingElement as HTMLElement | null;
        if (container === document.documentElement || container === root) {
            return new DOMRect(0, 0, window.innerWidth, window.innerHeight);
        }
        return container.getBoundingClientRect();
    }

    function isPartiallyVisibleInContainer(el: HTMLElement, container: HTMLElement, minPx = 2): boolean {
        const er = el.getBoundingClientRect();
        const cr = containerViewportRect(container);
        const iw = Math.min(er.right, cr.right) - Math.max(er.left, cr.left);
        const ih = Math.min(er.bottom, cr.bottom) - Math.max(er.top, cr.top);
        return iw >= minPx && ih >= minPx;
    }

    function scrollIntoViewWithin(container: HTMLElement, el: HTMLElement, dir?: Direction, margin = 12) {
        const cr = containerViewportRect(container);
        const er = el.getBoundingClientRect();
        if (!dir || dir === 'up' || dir === 'down') {
            if (er.top < cr.top + margin) container.scrollTop += er.top - (cr.top + margin);
            else if (er.bottom > cr.bottom - margin) container.scrollTop += er.bottom - (cr.bottom - margin);
        }
        if (!dir || dir === 'left' || dir === 'right') {
            if (er.left < cr.left + margin) container.scrollLeft += er.left - (cr.left + margin);
            else if (er.right > cr.right - margin) container.scrollLeft += er.right - (cr.right - margin);
        }
    }
    function canScroll(container: HTMLElement, dir: Direction) {
        if (dir === 'up') return container.scrollTop > 0;
        if (dir === 'down') return container.scrollTop < (container.scrollHeight - container.clientHeight);
        if (dir === 'left') return container.scrollLeft > 0;
        if (dir === 'right') return container.scrollLeft < (container.scrollWidth - container.clientWidth);
        return false;
    }
    function nudgeScroll(container: HTMLElement, dir: Direction, stepPx: number) {
        if (dir === 'up') container.scrollTop = Math.max(0, container.scrollTop - stepPx);
        if (dir === 'down') container.scrollTop = Math.min(container.scrollHeight - container.clientHeight, container.scrollTop + stepPx);
        if (dir === 'left') container.scrollLeft = Math.max(0, container.scrollLeft - stepPx);
        if (dir === 'right') container.scrollLeft = Math.min(container.scrollWidth - container.clientWidth, container.scrollLeft + stepPx);
    }

    function spatialNext(from: HTMLElement, dir: Direction, scopeId: string): NodeEntry | undefined {
        const all = candidatesInScope(scopeId).filter(n => n.el !== from);
        if (!all.length) return;
        if (dir === 'next' || dir === 'prev') return;

        const fromRect = from.getBoundingClientRect();
        const cx0 = fromRect.left + fromRect.width / 2;
        const cy0 = fromRect.top + fromRect.height / 2;

        const navContainer = nearestScrollContainer(from);
        const navContRect = containerViewportRect(navContainer);

        const vec: Record<Direction, [number, number]> = {
            left: [-1, 0], right: [1, 0], up: [0, -1], down: [0, 1], next: [1, 0], prev: [-1, 0],
        };
        const [vx, vy] = vec[dir];
        const isH = (dir === 'left' || dir === 'right');

        const EDGE_PX = 8;
        const atTop = navContainer.scrollTop <= 1 || fromRect.top <= navContRect.top + EDGE_PX;
        const atBottom = (navContainer.scrollHeight - navContainer.clientHeight - navContainer.scrollTop) <= 1 || fromRect.bottom >= navContRect.bottom - EDGE_PX;
        const atLeft = navContainer.scrollLeft <= 1 || fromRect.left <= navContRect.left + EDGE_PX;
        const atRight = (navContainer.scrollWidth - navContainer.clientWidth - navContainer.scrollLeft) <= 1 || fromRect.right >= navContRect.right - EDGE_PX;

        const wantEscape =
            (dir === 'up' && atTop) ||
            (dir === 'down' && atBottom) ||
            (dir === 'left' && atLeft) ||
            (dir === 'right' && atRight);

        type Cand = {
            n: NodeEntry;
            cx: number;
            cy: number;
            prim: number;
            perp: number;
            rowOverlap: number;
            colOverlap: number;
            score: number;
        };

        const forward: Cand[] = [];
        for (const n of all) {
            const r = rectOf(n);
            const cx = (r.left + r.width    / 2) - cx0;
            const cy = (r.top    + r.height / 2) - cy0;

            const dot = cx * vx + cy * vy;
            if (dot <= 0) continue;

            const rowOverlap = overlapRatioY(fromRect, r);
            const colOverlap = overlapRatioX(fromRect, r);

            const prim = isH ? Math.abs(cx) : Math.abs(cy);
            const perp = isH ? Math.abs(cy) : Math.abs(cx);

            forward.push({ n, cx, cy, prim, perp, rowOverlap, colOverlap, score: 0 });
        }
        if (!forward.length) return;

        const STRICT_OVERLAP = 0.45;
        const RELAX_OVERLAP = 0.20;
        const MAX_ANGLE_TAN = Math.tan(55 * Math.PI / 180);

        let pool: Cand[] = [];
        if (isH) {
            pool = forward.filter(c => c.rowOverlap >= STRICT_OVERLAP);
            if (!pool.length) pool = forward.filter(c => c.rowOverlap >= RELAX_OVERLAP);
            if (!pool.length) pool = forward.filter(c => c.perp <= c.prim * MAX_ANGLE_TAN);
        } else {
            pool = forward.filter(c => c.colOverlap >= STRICT_OVERLAP);
            if (!pool.length) pool = forward.filter(c => c.colOverlap >= RELAX_OVERLAP);
            if (!pool.length) pool = forward.filter(c => c.perp <= c.prim * MAX_ANGLE_TAN);
        }
        if (!pool.length) return;

        const sameCont = pool.filter(c => nearestScrollContainer(c.n.el) === navContainer);
        const otherCont = pool.filter(c => nearestScrollContainer(c.n.el) !== navContainer);

        let finalPool: Cand[] = [];
        if (sameCont.length) {
            finalPool = sameCont;
        } else if (wantEscape) {
            finalPool = otherCont;
        } else {
            finalPool = otherCont.filter(c => isPartiallyVisibleInContainer(c.n.el, nearestScrollContainer(c.n.el)));
            if (!finalPool.length) return;
        }

        const SAME_CONT_BIAS = -200;
        const CROSS_VISIBLE_BIAS = -150;
        const PERP_COEF = 0.25;
        const DIST_COEF = 0.02;

        for (const c of finalPool) {
            let s = c.prim * 1.0 + c.perp * PERP_COEF + Math.hypot(c.cx, c.cy) * DIST_COEF;
            const cCont = nearestScrollContainer(c.n.el);
            if (cCont === navContainer) s += SAME_CONT_BIAS;
            else if (isPartiallyVisibleInContainer(c.n.el, cCont)) s += CROSS_VISIBLE_BIAS;
            s -= (c.n.opts.priority ?? 0) * 1000;
            c.score = s;
        }

        finalPool.sort((a, b) => a.score - b.score || a.prim - b.prim || a.perp - b.perp);
        return finalPool[0]?.n;
    }

    function adoptActiveNode(scope: ScopeEntry, nodeId: NodeId) {
        if (scope.activeNode === nodeId) return;
        scope.activeNode = nodeId;
        scope.hadFocus = true;
    }

    function focusNode(node?: NodeEntry) {
        if (!node) return;
        const scope = index.scopes.get(node.scope);
        if (scope) {
            adoptActiveNode(scope, node.id);
        }
        node.el.focus();
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

        const trap = !!scope.opts.trap;

        if (!focused) {
            focusFirstInScope(scope.id);
            return;
        }

        if (dir === 'left' || dir === 'right' || dir === 'up' || dir === 'down') {
            let next = spatialNext(focused.el, dir, scope.id);
            if (!next) {
                const cont = nearestScrollContainer(focused.el);
                if (canScroll(cont, dir)) {
                    const r = focused.el.getBoundingClientRect();
                    const step = (dir === 'up' || dir === 'down')
                        ? Math.max(24, r.height * 0.9)
                        : Math.max(24, r.width    * 0.9);
                    nudgeScroll(cont, dir, step);
                    next = spatialNext(focused.el, dir, scope.id);
                }
            }
            if (next) {
                const cont = nearestScrollContainer(next.el);
                if (!isPartiallyVisibleInContainer(next.el, cont)) {
                    scrollIntoViewWithin(cont, next.el, dir);
                }
                focusNode(next);
                return;
            }
            if (trap) return;
        }

        if (dir === 'next' || dir === 'prev') {
            const list = sweepCandidates(scope.id, 'auto', focused.el);
            if (!list.length) return;

            const idx = list.findIndex(n => n.id === focused.id);
            let target: NodeEntry | undefined;

            if (idx >= 0) {
            const step = dir === 'next' ? 1 : -1;
            const nextIdx = idx + step;
            if (nextIdx >= 0 && nextIdx < list.length) {
                target = list[nextIdx];
            } else if (scope.opts.wrap) {
                target = dir === 'next' ? list[0] : list[list.length - 1];
            }
            } else {
            target = (dir === 'next') ? list[0] : list[list.length - 1];
            }

            if (target) { focusNode(target); return; }
            if (trap) return;
        }

        let parentId = scope.parent;
        while (parentId) {
            const parent = index.scopes.get(parentId);
            if (!parent) break;

            if (dir === 'left' || dir === 'right' || dir === 'up' || dir === 'down') {
                const cand = spatialNext(focused.el, dir, parent.id);
                if (cand) { focusNode(cand); return; }
            } else {
                const list = sweepCandidates(parent.id, 'auto', focused.el);
                if (list.length) {
                    focusNode(dir === 'next' ? list[0] : list[list.length - 1]);
                    return;
                }
            }

            parentId = parent.parent;
        }
    }

    function back(): boolean {
        const state = video?.state();
        console.log("back", {state, history, location, pathname: location.pathname});
        if (state === VideoState.Maximized) {
            video!.actions.minimizeVideo();
            return true;
        }
        
        if (state === VideoState.Minimized) {
            video!.actions.closeVideo();
            return true;
        } 
        
        
        const rootPaths: string[] = [
            "/web/",
            "/web",
            "/web/index.html",
            "/web/home",
            "/web/index",
            "/web/subscriptions",
            "/web/creators",
            "/web/playlists",
            "/web/watchLater",
            "/web/sources",
            "/web/downloads",
            "/web/history",
            "/web/sync",
            "/web/buy",
            "/web/settings"
        ];

        if (!rootPaths.some(v => v === location.pathname)) {
            navigate(-1);
            return true;
        }

        return false;
    }

    function press(kind: Press, openIntent: OpenIntent): boolean {
        const node = currentFocused();
        if (!node) {
            if (kind === "back") {
                return back();
            }
            return false;
        }

        if (kind === "press") {
            return node.opts.onPress?.(node.el, openIntent) ?? false;
        } 
        
        if (kind === "back") {
            const backResult = node.opts.onBack?.(node.el, openIntent);
            if (backResult !== true) {
                return back();
            }
            return false;
        }

        if (kind === "options") {
            return node.opts.onOptions?.(node.el, openIntent) ?? false;
        }

        return false;
    }

    function focusFirstInScope(scopeId: string) {
        const s = index.scopes.get(scopeId);
        if (!s) return;

        if (s.hadFocus && s.activeNode) {
            const last = index.nodes.get(s.activeNode);
            if (last && !last.opts.disabled && isVisible(last.el) && !last.opts.focusInert?.() && isFocusable(last.el)) {
                focusNode(last);
                return;
            }
        }

        const el = s.opts.defaultFocus?.();
        if (el && isFocusable(el)) {
            const n = findNodeFromElement(el);
            if (n && n.scope === scopeId) { focusNode(n); return; }
        }

        const first = sweepCandidates(scopeId, 'auto')[0];
        if (first) focusNode(first);
    }

    function resolveScopeId(el: HTMLElement): string | null {
        let cur: HTMLElement | null = el;
        while (cur) {
            const sid = index.scopeByEl.get(cur);
            if (sid) return sid;
            cur = cur.parentElement;
        }
        return null;
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
        if (e.key === 'Escape') return false;
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
                press('press', OpenIntent.Keyboard);
                e.preventDefault();
                break;
            case 'Escape':
                if (press('back', OpenIntent.Keyboard)) e.preventDefault(); break;
            case 'o':
                if (!editable && !e.altKey && !e.metaKey) { if (press('options', OpenIntent.Keyboard)) e.preventDefault(); }
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

    function onFocusIn(e: FocusEvent) {
        const node = findNodeFromElement(e.target as HTMLElement | null);
        if (!node) return;
        const scope = index.scopes.get(node.scope);
        if (!scope) return;
        adoptActiveNode(scope, node.id);
    }

    createEffect(() => {
        window.addEventListener("keydown", onKeyDown, { capture: true });
        window.addEventListener("focusin", onFocusIn, { capture: true });
        raf = requestAnimationFrame(pollGamepads);
        onCleanup(() => {
            window.removeEventListener("keydown", onKeyDown, { capture: true } as any);
            window.removeEventListener("focusin", onFocusIn, { capture: true } as any);
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