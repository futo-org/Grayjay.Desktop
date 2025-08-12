import { Accessor, onCleanup, onMount, createEffect } from "solid-js";
import { useFocus } from "./FocusProvider";
import type { ScopeOptions } from "./nav";

export function focusScope(el: HTMLElement, accessor: Accessor<ScopeOptions | undefined>) {
    const focus = useFocus();
    let scopeId: string | null = null;

    const opts = () => accessor() ?? {};

    onMount(() => {
        el.setAttribute("data-focus-scope", "");
        scopeId = focus.registerScope(el, opts());
        if (opts().trap) queueMicrotask(() => focus.focusFirstInScope(scopeId!));
    });

    createEffect(() => {
        if (!scopeId) return;
        const o = opts();
        // TODO: If you add focus.setScopeOptions(scopeId, o) in the provider, call it here.
    });

    onCleanup(() => {
        if (scopeId) focus.unregisterScope(scopeId);
    });
}
