import { Accessor, Component, createEffect, createSignal, For, JSX, onCleanup, onMount, Show, batch, untrack } from "solid-js";
import { Portal } from "solid-js/web";
import styles from './index.module.css';
import UIOverlay from "../../state/UIOverlay";

export interface GlobalContextMenuProps {}

const GlobalContextMenu: Component<GlobalContextMenuProps> = (props) => {
    const [menuVisible, setMenuVisible] = createSignal(false);
    const [menuPosition, setMenuPosition] = createSignal({ x: 0, y: 0 });
    const [selectedText, setSelectedText] = createSignal("");

    const handleContextMenu = (event: MouseEvent) => {
        const selection = window.getSelection()?.toString().trim();
        if (selection) {
            event.preventDefault();
            event.stopPropagation();
            setSelectedText(selection);
            setMenuPosition({ x: event.clientX, y: event.clientY });
            setMenuVisible(true);
        } else {
            setMenuVisible(false);
        }
    };

    const handleClick = () => {
        if (menuVisible()) {
            setMenuVisible(false);
        }
    };

    const handleCopy = async () => {
        try {
            await navigator.clipboard.writeText(selectedText());
            UIOverlay.toast("Text has been copied");
            setMenuVisible(false);
        } catch (e) {
            console.error("Copy failed", e);
        }
    };

    onMount(() => {
        document.addEventListener("contextmenu", handleContextMenu);
        document.addEventListener("click", handleClick);
    });

    onCleanup(() => {
        document.removeEventListener("contextmenu", handleContextMenu);
        document.removeEventListener("click", handleClick);
    });

    createEffect(() => {
        if (menuVisible()) {
            document.body.style.overflow = "hidden";
        } else {
            document.body.style.overflow = "";
        }
    });

    return (
        <Show when={menuVisible()}>
            <Portal>
                <div
                    style={{
                        position: "fixed",
                        top: "0",
                        left: "0",
                        width: "100vw",
                        height: "100vh",
                        background: "transparent",
                        "z-index": 9998
                    }}
                    onClick={handleClick}
                    onContextMenu={(e) => e.preventDefault()}
                    onWheel={(e) => e.preventDefault()}
                >
                </div>

                <div
                    class={styles.menu}
                    style={{
                        position: "fixed",
                        top: `${menuPosition().y}px`,
                        left: `${menuPosition().x}px`,
                        "z-index": 9999
                    }}
                >
                    <div class={styles.menuItem} onClick={handleCopy}>Copy</div>
                </div>
            </Portal>
        </Show>
    );
};

export default GlobalContextMenu;
