import { Accessor } from "solid-js";

export type Direction = "up" | "down" | "left" | "right" | "next" | "prev";
export type Press = "press" | "options" | "back" | "start";

export type Orientation = "vertical" | "horizontal" | "grid" | "spatial";
export type ScopeId = string;

export enum OpenIntent {
  Pointer = 'pointer',
  Gamepad = 'gamepad',
  Keyboard = 'keyboard'
}

export interface FocusableOptions {
    disabled?: boolean;
    priority?: number;
    order?: number;
    onPress?: (el: HTMLElement, openIntent: OpenIntent) => void;
    onOptions?: (el: HTMLElement, openIntent: OpenIntent) => void;
    onBack?: (el: HTMLElement, openIntent: OpenIntent) => boolean | undefined;
    getRect?: (el: HTMLElement) => DOMRect;
    roving?: boolean; //If true, uses roving tab index pattern where only one item in the scope is tabbable
    focusInert?: Accessor<boolean>; //If true, don't claim focus by yourself
}

export interface ScopeOptions {
    id?: ScopeId;
    orientation?: Orientation;
    wrap?: boolean;
    loop?: boolean;
    trap?: boolean;
    defaultFocus?: () => HTMLElement | null;
}

export function isVisible(el: Element): boolean {
    if (!(el instanceof HTMLElement)) return false;
    if (el.hidden) return false;
    const style = getComputedStyle(el);
    if (style.visibility === "hidden" || style.display === "none") return false;
    const rect = el.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
}

export function isFocusable(el: Element): el is HTMLElement {
    if (!(el instanceof HTMLElement)) return false;
    const tabIndex = el.tabIndex;
    const disabled = (el as HTMLButtonElement).disabled || el.getAttribute("aria-disabled") === "true";
    return !disabled && isVisible(el) && tabIndex >= -1;
}

export function uid(prefix = "scope"): ScopeId {
    return `${prefix}-${Math.random().toString(36).slice(2, 9)}`;
}

export const clamp = (v: number, min: number, max: number) => Math.max(min, Math.min(max, v));