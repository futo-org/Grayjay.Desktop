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

        if (opts().trap) {
            queueMicrotask(() => {
                requestAnimationFrame(() => scopeId && focus.focusFirstInScope(scopeId));
            });
        }
    });

    createEffect(on(() => !!opts().trap, (trap, prevTrap) => {
        if (!scopeId) return trap;
        if (trap && !prevTrap) {
            const isActive = untrack(() => focus.getActiveScope() === scopeId);
            if (!isActive) {
                focus.setActiveScope(scopeId);
                queueMicrotask(() => focus.focusFirstInScope(scopeId!));
            }
            return trap;
        }

        if (!trap && prevTrap) {
            const isActive = untrack(() => focus.getActiveScope() === scopeId);
            if (isActive) {
                focus.setActiveScope(parentIdAtMount);
                if (parentIdAtMount) {
                    queueMicrotask(() => focus.focusFirstInScope(parentIdAtMount!));
                }
            }
        }

        return trap;
    },
    { defer: true }));

    onCleanup(() => {
        if (scopeId) focus.unregisterScope(scopeId);
        el.removeAttribute("data-focus-scope");
    });
}
