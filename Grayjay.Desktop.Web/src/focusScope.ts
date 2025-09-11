import { Accessor, onCleanup, onMount, createEffect, untrack } from "solid-js";
import { useFocus } from "./FocusProvider";
import type { ScopeOptions } from "./nav";

export function focusScope(el: HTMLElement, accessor: Accessor<ScopeOptions | undefined>) {
    const focus = useFocus();
    if (!focus) {
        console.warn("FocusScope not inside FocusProvider", el);
        return;
    }

    let scopeId: string | null = null;

    const opts = () => accessor();

    const resolveParentScopeId = (): string | null => {
        const host = el.parentElement?.closest?.("[data-focus-scope]") as HTMLElement | null;
        return (host && focus.resolveScopeId(host)) || focus.getActiveScope?.() || null;
    };

    const register = (): boolean => {
        const scopeOptions = opts();
        if (!scopeOptions) return false;

        const parentId = resolveParentScopeId() ?? undefined;

        if (!scopeId) {
            scopeId = focus.registerScope(el, scopeOptions, parentId);
            return !!scopeId;
        }

        (focus as any).setScopeOptions?.(scopeId, scopeOptions);
        return true;
    };

    const tryRegisterWithRetry = () => {
        if (register()) return;

        queueMicrotask(() => {
            if (register()) return;
            requestAnimationFrame(() => {
                if (register()) return;
                console.warn("focusScope: no parent scope found (or no options) for", el);
            });
        });
    };

    onMount(() => {
        el.setAttribute("data-focus-scope", "");
        tryRegisterWithRetry();

        onCleanup(() => {
            if (scopeId) {
                focus.unregisterScope(scopeId);
                scopeId = null;
            }
            el.removeAttribute("data-focus-scope");
        });
    });

    createEffect(() => {
        const scopeOptions = opts();
        if (!scopeOptions) return;

        if (!scopeId) {
            untrack(tryRegisterWithRetry);
            tryRegisterWithRetry();
            return;
        }
        (focus as any).setScopeOptions?.(scopeId, scopeOptions);
    });
}
