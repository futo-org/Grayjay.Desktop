import { Accessor, onCleanup, onMount, createEffect, on, untrack } from "solid-js";
import { useFocus } from "./FocusProvider";
import type { ScopeOptions } from "./nav";

export function focusScope(el: HTMLElement, accessor: Accessor<ScopeOptions | undefined>) {
    const focus = useFocus();
    if (!focus) {
        console.warn("FocusScope not inside FocusProvider", el);
        return;
    }
    let scopeId: string | null = null;
    let parentIdAtMount: string | null = null;

    const opts = () => accessor() ?? {};

    onMount(() => {
        el.setAttribute("data-focus-scope", "");
        parentIdAtMount = focus.resolveScopeId(el.parentElement as HTMLElement) ?? focus.getActiveScope() ?? null;
        scopeId = focus.registerScope(el, opts(), parentIdAtMount ?? undefined);
        console.info("registerScope", {scopeId, el, opts: opts(), parentIdAtMount});
    });

    onCleanup(() => {
        if (scopeId) focus.unregisterScope(scopeId);
        el.removeAttribute("data-focus-scope");
    });
}
