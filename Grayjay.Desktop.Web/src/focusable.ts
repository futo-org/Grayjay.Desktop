import { Accessor, onCleanup, onMount, createEffect } from "solid-js";
import { useFocus } from "./FocusProvider";
import type { FocusableOptions } from "./nav";

export function focusable(el: HTMLElement, accessor: Accessor<FocusableOptions>) {
    const focus = useFocus();
    let nodeId: string | null = null;

    const opts = () => accessor() ?? {};

    const findScopeHost = () => el.closest("[data-focus-scope]") as HTMLElement | null;

    const tryRegister = (): boolean => {
        const host = findScopeHost();
        const sid = (host && focus.resolveScopeId(host)) || focus.getActiveScope?.() || null;
        if (!sid) return false;

        nodeId = focus.registerNode(el, sid, opts());
        onCleanup(() => nodeId && focus.unregisterNode(nodeId));
        return true;
    };

    onMount(() => {
        if (tryRegister()) return;

        queueMicrotask(() => {
            if (nodeId || tryRegister()) return;

            let attempts = 0;
            const tick = () => {
                if (nodeId || tryRegister()) return;
                if (++attempts < 10) requestAnimationFrame(tick);
            };
            requestAnimationFrame(tick);
        });

        // for portals/late mounts
        const mo = new MutationObserver(() => {
            if (!nodeId && tryRegister()) mo.disconnect();
        });
        mo.observe(document.documentElement, { childList: true, subtree: true, attributes: true });
        onCleanup(() => mo.disconnect());
    });

    createEffect(() => {
        if (nodeId) focus.setNodeOptions(nodeId, opts());
    });
}
