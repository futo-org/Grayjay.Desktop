import { createContext, useContext, createSignal, onCleanup, createEffect, Accessor, JSX } from "solid-js";
import { Direction, Press, FocusableOptions, ScopeOptions, uid, isVisible, isFocusable, InputSource } from "./nav";
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
    opts: ScopeOptions;
    nodes: Set<NodeId>;
    activeNode?: NodeId;
    hadFocus?: boolean;
    mode: 'off' | 'on' | 'trap';
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
    lastInputSource: Accessor<InputSource>;
    registerScope: (el: HTMLElement, opts?: ScopeOptions, parentScopeId?: string) => string;
    unregisterScope: (id: string) => void;
    registerNode: (el: HTMLElement, scopeId: string, opts: FocusableOptions) => string;
    unregisterNode: (id: string) => void;
    setNodeOptions: (id: string, opts: Partial<FocusableOptions>) => void;
    navigate: (dir: Direction, inputSource: InputSource) => void;
    press: (kind: Press, inputSource: InputSource) => void;
    focusFirstInScope: (scopeId: string, isDefaultFocus: boolean) => void;
    setActiveScope: (id: string | null) => void;
    getActiveScope: Accessor<string | null>;
    resolveScopeId: (el: HTMLElement) => string | null;
    setScopeMode: (id: string, mode: 'off' | 'on' | 'trap') => void;
    getScopeMode: (id: string) => 'off' | 'on' | 'trap' | undefined;
    getFocusedNode: Accessor<NodeEntry | undefined>;
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
    const [lastInputSource, setLastInputSource] = createSignal<InputSource>("pointer");
    createEffect(() => console.info("lastInputSource changed", lastInputSource()));
    const [focusedNode, setFocusedNode] = createSignal<NodeEntry | undefined>(undefined);
    const trapStack: string[] = [];

    function isActiveMode(s?: ScopeEntry) { return !!s && s.mode !== 'off'; }

    function isWithinScope(childId: string, ancestorId: string): boolean {
        let cur: string | undefined | null = childId;
        while (cur) {
            if (cur === ancestorId) return true;
            cur = index.scopes.get(cur)?.parent ?? null;
        }
        return false;
    }

    function syncTabIndexForNode(n: NodeEntry) {
        const s = index.scopes.get(n.scope);
        const inert = !!n.opts.focusInert?.();
        const active = isActiveMode(s);
        if (inert || !active) n.el.tabIndex = -1;
        else if (n.el.tabIndex < 0) n.el.tabIndex = 0;
    }

    function firstActiveAncestor(startId?: string | null): string | null {
        let cur = startId ?? null;
        while (cur) {
            const s = index.scopes.get(cur);
            if (isActiveMode(s)) return cur;
            cur = s?.parent ?? null;
        }
        return null;
    }

    function anyActiveScope(excludeId?: string | null): string | null {
        for (const s of index.scopes.values()) {
            if (s.id !== (excludeId ?? '') && isActiveMode(s)) return s.id;
        }
        return null;
    }

    function topTrap(): string | null {
        return trapStack.length ? trapStack[trapStack.length - 1] : null;
    }

    function recomputeActiveScope() {
        const trap = topTrap();
        if (trap) { setActiveScope(trap); return; }
        const cur = activeScope();
        const curScope = cur ? index.scopes.get(cur) : undefined;
        if (cur && isActiveMode(curScope)) return;
        setActiveScope(anyActiveScope());
    }
    
    function registerScope(el: HTMLElement, opts?: ScopeOptions, parentScopeId?: string) {
        const id = opts?.id ?? uid("scope");
        const initialMode = opts?.initialMode ?? 'on';
        const rec: ScopeEntry = {
            id,
            parent: parentScopeId ?? activeScope() ?? undefined,
            opts: { ...opts },
            nodes: new Set(),
            mode: initialMode,
        };
        index.scopes.set(id, rec);
        index.scopeByEl.set(el, id);

        if (rec.mode === 'trap' && !trapStack.includes(id)) {
            trapStack.push(id);
            setActiveScope(id);
            queueMicrotask(() => focusFirstInScope(id, true));
        } else {
            recomputeActiveScope();
        }

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

        const tIdx = trapStack.indexOf(id);
        if (tIdx >= 0) trapStack.splice(tIdx, 1);

        const wasActive = activeScope() === id;
        recomputeActiveScope();
        const next = activeScope();
        if (wasActive && next) queueMicrotask(() => focusFirstInScope(next, true));
    }

    function setScopeMode(id: string, mode: 'off' | 'on' | 'trap') {
        console.info("setScopeMode", {id, mode});
        const s = index.scopes.get(id);
        if (!s || s.mode === mode) return;

        const prev = s.mode;
        s.mode = mode;

        for (const nid of s.nodes) {
            const n = index.nodes.get(nid);
            if (n) syncTabIndexForNode(n);
        }

        if (mode === 'trap') {
            if (!trapStack.includes(id)) trapStack.push(id);
            setActiveScope(id);
            queueMicrotask(() => focusFirstInScope(id, true));
            return;
        }
        if (prev === 'trap') {
            const i = trapStack.indexOf(id);
            if (i >= 0) trapStack.splice(i, 1);
        }

        if (mode === 'off') {
            const cur = currentFocused();
            if (cur && isWithinScope(cur.scope, id)) {
                const target = topTrap() ?? firstActiveAncestor(s.parent) ?? anyActiveScope(id);
                if (target) queueMicrotask(() => focusFirstInScope(target, true));
                else cur.el.blur?.();
            }
        }

        recomputeActiveScope();
    }

    function registerNode(el: HTMLElement, scopeId: string, opts: FocusableOptions) {
        const id = uid("node");
        const entry: NodeEntry = { id, el, scope: scopeId, opts: { priority: 0, ...opts } };
        index.nodes.set(id, entry);
        index.nodeByEl.set(el, id);

        const scope = index.scopes.get(scopeId);
        if (scope) scope.nodes.add(id);

        syncTabIndexForNode(entry);
        return id;
    }

    function setNodeOptions(id: string, opts: Partial<FocusableOptions>) {
        const rec = index.nodes.get(id);
        if (!rec) return;
        rec.opts = { ...rec.opts, ...opts };
        syncTabIndexForNode(rec);
    }

    function unregisterNode(id: string) {
        const rec = index.nodes.get(id);
        if (!rec) return;
        index.nodeByEl.delete(rec.el);
        index.nodes.delete(id);
        const scope = index.scopes.get(rec.scope);
        scope?.nodes.delete(id);
        if (scope?.activeNode === id) scope.activeNode = undefined;

        if (focusedNode()?.id === id) {
            setFocusedNode(undefined);
        }
    }

    function candidatesInScope(scopeId: string): NodeEntry[] {
        const s = index.scopes.get(scopeId);
        if (!s || !isActiveMode(s)) return [];
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
        const el = document.activeElement as HTMLElement | null;
        const node = findNodeFromElement(el);
        return node;
    }

    function rectOf(n: NodeEntry): DOMRect {
        return (n.opts.getRect?.(n.el) ?? n.el.getBoundingClientRect());
    }

    function sweepCandidates(scopeId: string, fromEl?: HTMLElement): NodeEntry[] {
        const s = index.scopes.get(scopeId);
        if (!s || !isActiveMode(s)) return [];
        const isAxisY = false;

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
            if (isAxisY) {
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

    function sameOrDescendantContainer(parent: HTMLElement, el: HTMLElement) {
        const candCont = nearestScrollContainer(el);
        return candCont === parent || parent.contains(candCont);
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
        const isVertical = (dir === 'up' || dir === 'down');

        const EDGE_PX = 8;
        const atTop = navContainer.scrollTop <= 1 || fromRect.top    <= navContRect.top    + EDGE_PX;
        const atBottom = (navContainer.scrollHeight - navContainer.clientHeight - navContainer.scrollTop) <= 1 || fromRect.bottom >= navContRect.bottom - EDGE_PX;
        const atLeft = navContainer.scrollLeft <= 1 || fromRect.left  <= navContRect.left   + EDGE_PX;
        const atRight = (navContainer.scrollWidth - navContainer.clientWidth - navContainer.scrollLeft) <= 1 || fromRect.right  >= navContRect.right - EDGE_PX;

        const wantEscape = (dir === 'up' && atTop) || (dir === 'down' && atBottom) || (dir === 'left' && atLeft) || (dir === 'right' && atRight);
        const BEAM_MIN = 44;
        const BEAM_PAD = 12;
        const BEAM_SCALE = 1.0;

        const beam = isVertical
            ? {
                left: cx0 - Math.max(BEAM_MIN / 2, fromRect.width  * BEAM_SCALE / 2) - BEAM_PAD,
                right: cx0 + Math.max(BEAM_MIN / 2, fromRect.width  * BEAM_SCALE / 2) + BEAM_PAD,
            }
            : {
                top: cy0 - Math.max(BEAM_MIN / 2, fromRect.height * BEAM_SCALE / 2) - BEAM_PAD,
                bottom: cy0 + Math.max(BEAM_MIN / 2, fromRect.height * BEAM_SCALE / 2) + BEAM_PAD,
            };

        const intervalOverlap = (a1: number, a2: number, b1: number, b2: number) => Math.max(0, Math.min(a2, b2) - Math.max(a1, b1));
        const pointIntervalDist = (p: number, a: number, b: number) => (p < a ? a - p : (p > b ? p - b : 0));
        const hitsBeam = (r: DOMRect) => {
            return isVertical
                ? intervalOverlap(r.left, r.right, (beam as any).left, (beam as any).right) > 0
                : intervalOverlap(r.top, r.bottom, (beam as any).top, (beam as any).bottom) > 0;
        };

        const forward: { n: NodeEntry; r: DOMRect; cx: number; cy: number; }[] = [];
        for (const n of all) {
            const r = rectOf(n);
            const cx = (r.left + r.width / 2) - cx0;
            const cy = (r.top  + r.height / 2) - cy0;
            const dot = cx * vx + cy * vy;
            if (dot > 0) forward.push({ n, r, cx, cy });
        }
        if (!forward.length) return;

        const sameOrDesc = forward.filter(c => sameOrDescendantContainer(navContainer, c.n.el));
        const unrelated  = forward.filter(c => !sameOrDescendantContainer(navContainer, c.n.el));
        const STRICT_OVERLAP = 0.45;
        const RELAX_OVERLAP = 0.20;
        const MAX_ANGLE_TAN = Math.tan(65 * Math.PI / 180);

        function verticalPool(cands: typeof forward) {
            const strict = cands.filter(c => (intervalOverlap(fromRect.left, fromRect.right, c.r.left, c.r.right) / Math.min(fromRect.width, c.r.width || 1)) >= STRICT_OVERLAP);
            if (strict.length) return strict;
            const relaxed = cands.filter(c => (intervalOverlap(fromRect.left, fromRect.right, c.r.left, c.r.right) / Math.min(fromRect.width, c.r.width || 1)) >= RELAX_OVERLAP);
            if (relaxed.length) return relaxed;
            const beamHits = cands.filter(c => hitsBeam(c.r));
            if (beamHits.length) return beamHits;
            return cands.filter(c => Math.abs(c.cx) <= Math.abs(c.cy) * MAX_ANGLE_TAN);
        }

        function horizontalPool(cands: typeof forward) {
            const strict = cands.filter(c => (intervalOverlap(fromRect.top, fromRect.bottom, c.r.top, c.r.bottom) / Math.min(fromRect.height, c.r.height || 1)) >= STRICT_OVERLAP);
            if (strict.length) return strict;
            const relaxed = cands.filter(c => (intervalOverlap(fromRect.top, fromRect.bottom, c.r.top, c.r.bottom) / Math.min(fromRect.height, c.r.height || 1)) >= RELAX_OVERLAP);
            if (relaxed.length) return relaxed;
            const beamHits = cands.filter(c => hitsBeam(c.r));
            if (beamHits.length) return beamHits;
            return cands.filter(c => Math.abs(c.cy) <= Math.abs(c.cx) * MAX_ANGLE_TAN);
        }

        function chooseAxisPool(cands: typeof forward) {
            return isVertical ? verticalPool(cands) : horizontalPool(cands);
        }

        let axisPool = chooseAxisPool(sameOrDesc);
        if (!axisPool.length) {
            axisPool = wantEscape ? chooseAxisPool(unrelated)
                                : chooseAxisPool(unrelated).filter(c => isPartiallyVisibleInContainer(c.n.el, nearestScrollContainer(c.n.el)));
            if (!axisPool.length) return;
        }

        const PERP_COEF = 0.35;
        const DIST_COEF = 0.015;
        const ALIGN_BONUS = -120;
        const SAME_CONT_BIAS = -200;
        const CROSS_VISIBLE_BIAS = -150;
        const SMALL_TARGET_PX = 28;
        const SMALL_BONUS = -100;

        type Scored = {
            n: NodeEntry;
            prim: number;
            perp: number;
            centerHyp: number;
            beamOverlapRatio: number;
            sameContainer: boolean;
            score: number;
        };

        const scored: Scored[] = [];
        for (const c of axisPool) {
            const r = c.r;

            let prim: number;
            if (dir === 'down') prim = Math.max(0, r.top    - fromRect.bottom);
            else if (dir === 'up') prim = Math.max(0, fromRect.top - r.bottom);
            else if (dir === 'right') prim = Math.max(0, r.left  - fromRect.right);
            else /* left */ prim = Math.max(0, fromRect.left - r.right);

            const perp = isVertical
                ? pointIntervalDist(cx0, r.left, r.right)
                : pointIntervalDist(cy0, r.top, r.bottom);

            const centerHyp = Math.hypot(c.cx, c.cy);
            const candCont = nearestScrollContainer(c.n.el);
            const sameContainer = (candCont === navContainer);

            const beamOverlap = isVertical
                ? intervalOverlap(r.left, r.right, (beam as any).left, (beam as any).right)
                : intervalOverlap(r.top, r.bottom, (beam as any).top, (beam as any).bottom);
            const span = isVertical ? r.width : r.height;
            const beamOverlapRatio = span > 0 ? (beamOverlap / span) : 0;

            let s = 0;
            s += prim * 1.0;
            s += perp * PERP_COEF;
            s += centerHyp * DIST_COEF;

            if (perp === 0) s += ALIGN_BONUS;
            if (sameContainer) s += SAME_CONT_BIAS;
            else if (isPartiallyVisibleInContainer(c.n.el, candCont)) s += CROSS_VISIBLE_BIAS;

            s += -80 * beamOverlapRatio;
            const smallDim = isVertical ? r.width : r.height;
            if (smallDim <= SMALL_TARGET_PX) s += SMALL_BONUS;

            s -= (c.n.opts.priority ?? 0) * 1000;
            scored.push({ n: c.n, prim, perp, centerHyp, beamOverlapRatio, sameContainer, score: s });
        }

        if (!scored.length) return;

        scored.sort((a, b) =>
            a.score - b.score
            || a.prim - b.prim
            || a.perp - b.perp
            || a.centerHyp - b.centerHyp
        );

        return scored[0]?.n;
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
        setFocusedNode(node);
    }

    function findScopeForNavigation(): ScopeEntry | undefined {
        const trap = topTrap();
        if (trap) return index.scopes.get(trap);

        const current = currentFocused();
        if (current) {
            const s = index.scopes.get(current.scope);
            if (isActiveMode(s)) return s;
        }
        const as = activeScope();
        if (as) {
            const s = index.scopes.get(as);
            if (isActiveMode(s)) return s;
        }
        return [...index.scopes.values()].find(isActiveMode);
    }

    function navigateDirection(dir: Direction, inputSource: InputSource) {
        const focused = currentFocused();
        const scope = findScopeForNavigation();
        if (!scope) return;

        const trappingId = topTrap();
        const trapActive = !!trappingId;
        
        setLastInputSource(inputSource);
        if (!focused) {
            focusFirstInScope(scope.id, false);
            return;
        }

        if ((focused.opts.onDirection?.(focused.el, dir, inputSource) ?? false) === true) {
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
            if (trapActive) return;
        }

        if (dir === 'next' || dir === 'prev') {
            const list = sweepCandidates(scope.id, focused.el);
            if (!list.length) return;

            const idx = list.findIndex(n => n.id === focused.id);
            let target: NodeEntry | undefined;

            if (idx >= 0) {
                const step = dir === 'next' ? 1 : -1;
                const nextIdx = idx + step;
                if (nextIdx >= 0 && nextIdx < list.length) {
                    target = list[nextIdx];
                }
            } else {
                target = (dir === 'next') ? list[0] : list[list.length - 1];
            }

            if (target) { focusNode(target); return; }
            if (trapActive) return;
        }

        let parentId = scope.parent;
        while (parentId) {
            if (trapActive && trappingId && !isWithinScope(parentId, trappingId)) {
                return;
            }

            const parent = index.scopes.get(parentId);
            if (!parent) break;

            if (dir === 'left' || dir === 'right' || dir === 'up' || dir === 'down') {
                const cand = spatialNext(focused.el, dir, parent.id);
                if (cand) { focusNode(cand); return; }
            } else {
                const list = sweepCandidates(parent.id, focused.el);
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
        if (state === VideoState.Fullscreen) {
            video!.actions.setState(VideoState.Maximized);
            return true;
        } else if (state === VideoState.Maximized) {
            video!.actions.setState(VideoState.Minimized);
            return true;
        } else if (state === VideoState.Minimized) {
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

    function press(kind: Press, inputSource: InputSource): boolean {
        setLastInputSource(inputSource);
        const node = currentFocused();
        if (!node) {
            if (kind === "back") {
                return back();
            }
            return false;
        }

        if (kind === "press") {
            return node.opts.onPress?.(node.el, inputSource) ?? false;
        } 
        
        if (kind === "back") {
            const backResult = node.opts.onBack?.(node.el, inputSource);
            if (backResult !== true) {
                return back();
            }
            return false;
        }

        if (kind === "options") {
            return node.opts.onOptions?.(node.el, inputSource) ?? false;
        }

        if (kind === "action") {
            return node.opts.onAction?.(node.el, inputSource) ?? false;
        }

        return false;
    }

    function focusFirstInScope(scopeId: string, isAutoFocus: boolean) {
        const s = index.scopes.get(scopeId);
        console.info("focusFirstInScope", {scopeId, isAutoFocus, s});

        if (!s || !isActiveMode(s)) return;
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

        const first = sweepCandidates(scopeId)[0];
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
        ensureFocusInActiveScope();
        const trap = topTrap();
        const target = e.target as HTMLElement | null;
        const editable = isEditable(e.target);

        const isNavKey =
            e.key === 'ArrowUp' || e.key === 'ArrowDown' ||
            e.key === 'ArrowLeft' || e.key === 'ArrowRight' ||
            e.key === 'Tab' || e.key === 'Escape';

        if (editable && isNavKey && !editableWantsKey(e, !!trap)) {
            e.preventDefault();
            e.stopPropagation();

            const owner = findNodeFromElement(target!);
            if (owner) {
                focusNode(owner);
            } else {
                target?.blur?.();
            }

            if (e.key === 'Escape') { press('back', "keyboard"); return; }
            if (e.key === 'Tab') { navigateDirection(e.shiftKey ? 'prev' : 'next', "keyboard"); return; }

            const dir = e.key === 'ArrowUp' 
                ? 'up' 
                : e.key === 'ArrowDown' 
                    ? 'down' 
                    : e.key === 'ArrowLeft' 
                        ? 'left' 
                        : 'right';

            navigateDirection(dir as Direction, "keyboard");
            return;
        }

        if (e.key === 'Tab' && trap) {
            navigateDirection(e.shiftKey ? 'prev' : 'next', "keyboard");
            e.preventDefault();
            e.stopPropagation();
            return;
        }

        if (editable && editableWantsKey(e, !!trap)) return;

        switch (e.key) {
            case 'ArrowUp': navigateDirection('up', "keyboard"); e.preventDefault(); e.stopPropagation(); break;
            case 'ArrowDown': navigateDirection('down', "keyboard"); e.preventDefault(); e.stopPropagation(); break;
            case 'ArrowLeft': navigateDirection('left', "keyboard"); e.preventDefault(); e.stopPropagation(); break;
            case 'ArrowRight': navigateDirection('right', "keyboard"); e.preventDefault(); e.stopPropagation(); break;
            case 'Enter':
            case ' ':
                press('press', "keyboard");
                e.preventDefault();
                e.stopPropagation();
                break;
            case 'Escape':
                if (press('back', "keyboard")) e.preventDefault(); e.stopPropagation(); break;
            case 'o':
                if (!editable && !e.altKey && !e.metaKey) { if (press('options', "keyboard")) e.preventDefault(); e.stopPropagation(); }
                break;
            case 'p':
                if (!editable && !e.altKey && !e.metaKey) { if (press('action', "keyboard")) e.preventDefault(); e.stopPropagation(); }
                break;
            default:
                if (!editable) {
                    if (e.key === 'w') { navigateDirection('up', "keyboard"); e.preventDefault(); e.stopPropagation(); }
                    else if (e.key === 's') { navigateDirection('down', "keyboard"); e.preventDefault(); e.stopPropagation(); }
                    else if (e.key === 'a') { navigateDirection('left', "keyboard"); e.preventDefault(); e.stopPropagation(); }
                    else if (e.key === 'd') { navigateDirection('right', "keyboard"); e.preventDefault(); e.stopPropagation(); }
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
            const vert = Math.abs(ly) > axisThreshold ? (ly > 0 ? "down" : "up") as Direction : undefined;
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
                    navigateDirection(dir, "gamepad");
                } else {
                    const delay = padState.lastFire ? (now - padState.lastFire) : Infinity;
                    if (delay >= (initialDelay)) {
                        navigateDirection(dir, "gamepad");
                        padState.lastFire = now - (initialDelay - repeatDelay);
                    }
                }

                setLastInputSource("gamepad");
            } else {
                padState.dirHeld = undefined;
                padState.lastFire = undefined;
            }

            const pressBtn = gp.buttons[btn.A]?.pressed;
            const backBtn = gp.buttons[btn.B]?.pressed;
            const optionsBtn = gp.buttons[btn.X]?.pressed;
            const actionBtn = gp.buttons[btn.Y]?.pressed;
            const startBtn = gp.buttons[btn.START]?.pressed;

            if (pressBtn && !padState.pressed.has(btn.A)) { press("press", "gamepad"); padState.pressed.add(btn.A); }
            if (!pressBtn) padState.pressed.delete(btn.A);

            if (backBtn && !padState.pressed.has(btn.B)) { press("back", "gamepad"); padState.pressed.add(btn.B); }
            if (!backBtn) padState.pressed.delete(btn.B);

            if (optionsBtn && !padState.pressed.has(btn.X)) { press("options", "gamepad"); padState.pressed.add(btn.X); }
            if (!optionsBtn) padState.pressed.delete(btn.X);

            if (actionBtn && !padState.pressed.has(btn.Y)) { press("action", "gamepad"); padState.pressed.add(btn.Y); }
            if (!actionBtn) padState.pressed.delete(btn.Y);

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

        const trap = topTrap();
        if (trap && !isWithinScope(node.scope, trap)) {
            queueMicrotask(() => focusFirstInScope(trap, true));
            return;
        }

        if (!isActiveMode(scope)) {
            const target = trap ?? activeScope() ?? firstActiveAncestor(scope.parent) ?? anyActiveScope(scope.id);
            if (target) queueMicrotask(() => focusFirstInScope(target, true));
            else (e.target as HTMLElement).blur?.();
            return;
        }

        adoptActiveNode(scope, node.id);
        setFocusedNode(node);
    }

    function ensureFocusInActiveScope() {
        const cur = currentFocused();
        if (!cur) return;
        const s = index.scopes.get(cur.scope);
        if (isActiveMode(s)) return;

        const target = topTrap() ?? firstActiveAncestor(s?.parent) ?? anyActiveScope(cur.scope);
        if (target) queueMicrotask(() => focusFirstInScope(target, true));
        else (cur.el as HTMLElement).blur?.();
    }

    const onPointerMove = (e: PointerEvent) => {
        setLastInputSource("pointer");
    };

    createEffect(() => {
        window.addEventListener("keydown", onKeyDown, { capture: true });
        window.addEventListener("focusin", onFocusIn, { capture: true });
        window.addEventListener("pointermove", onPointerMove, { passive: true });
        raf = requestAnimationFrame(pollGamepads);
        onCleanup(() => {
            window.removeEventListener("keydown", onKeyDown, { capture: true } as any);
            window.removeEventListener("focusin", onFocusIn, { capture: true } as any);
            cancelAnimationFrame(raf);
        });
    });

    function setActiveScopeSafe(id: string | null) {
        if (id === null) { setActiveScope(null); return; }
        const s = index.scopes.get(id);
        if (s && isActiveMode(s)) { setActiveScope(id); return; }
        const fallback = topTrap() ?? firstActiveAncestor(s?.parent) ?? anyActiveScope() ?? null;
        setActiveScope(fallback);
    }

    createEffect(() => {
        const src = lastInputSource();
        const root = document.body;
        root.setAttribute("data-input-source", src);
    });

    const api: FocusAPI = {
        lastInputSource,
        registerScope,
        unregisterScope,
        registerNode,
        unregisterNode,
        setNodeOptions,
        navigate: navigateDirection,
        press,
        focusFirstInScope,
        setActiveScope: setActiveScopeSafe,
        getActiveScope: activeScope,
        resolveScopeId,
        setScopeMode,
        getScopeMode: (id) => index.scopes.get(id)?.mode,
        getFocusedNode: focusedNode
    };

    return <FocusCtx.Provider value={api}>{props.children}</FocusCtx.Provider>;
}