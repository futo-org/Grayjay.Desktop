import { Accessor, onCleanup, onMount, createEffect } from "solid-js";
import { useFocus } from "./FocusProvider";
import type { FocusableOptions } from "./nav";

export function focusable(el: HTMLElement, accessor: Accessor<FocusableOptions>) {
    const focus = useFocus();
    if (!focus) {
        console.warn("Focusable not inside FocusProvider", el);
        return;
    }

    let nodeId: string | null = null;
    let sid: string | null = null;
    const opts = () => accessor() ?? {};

    const resolveSid = (): string | null => {
        const host = el.closest("[data-focus-scope]") as HTMLElement | null;
        return (host && focus.resolveScopeId(host)) || focus.getActiveScope?.() || null;
    };

    const registerIntoCurrentScope = (): boolean => {
        const next = resolveSid();
        if (!next) return false;

        if (nodeId && next !== sid) {
            focus.unregisterNode(nodeId);
            nodeId = null;
        }

        if (!nodeId) {
            nodeId = focus.registerNode(el, next, opts());
            sid = next;
        } else {
            focus.setNodeOptions(nodeId, opts());
        }
        return true;
    };

    onMount(() => {
        onCleanup(() => {
            if (nodeId) {
                focus.unregisterNode(nodeId);
                nodeId = null;
                sid = null;
            }
        });

        if (registerIntoCurrentScope()) return;
        requestAnimationFrame(() => {
            if (registerIntoCurrentScope()) return;
            console.warn("focusable: no focus scope found for element", el);
        });
    });

    createEffect(() => {
        if (nodeId) focus.setNodeOptions(nodeId, opts());
    });
}