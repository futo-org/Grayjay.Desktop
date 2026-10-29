import { Component, createSignal, createEffect, onCleanup, Show, Index, JSX, createMemo } from "solid-js";
import styles from './index.module.css';
import { AnchorStyle } from "../../../../utility/Anchor";
import chevDown from "../../../../assets/icons/icon_chrevron_down.svg"
import check from "../../../../assets/icons/icon_checkmark.svg"
import StateGlobal from "../../../../state/StateGlobal";
import { focusScope } from '../../../../focusScope'; void focusScope;
import { focusable } from '../../../../focusable'; void focusable;
import { Direction, InputSource } from "../../../../nav";

export type DropdownApi = {
    open: (inputSource: InputSource) => void;
    close: (inputSource: InputSource) => void;
    toggle: (inputSource: InputSource) => void;
    isOpen: () => boolean;
};

export interface DropdownProps {
    options: any[];
    value: number;
    onSelectedChanged: (value: number) => void;
    anchorStyle?: AnchorStyle;
    label?: string;
    style?: JSX.CSSProperties;
    selectStyle?: JSX.CSSProperties;
    focusable?: boolean;
    onBack?: () => boolean;
    direction?: "up" | "down";
    focusableGroupOpts?: {
        groupId?: string;
        groupType?: "grid" | "horizontal" | "vertical";
        groupIndices?: (number | undefined)[];
        groupEscapeTo?: Partial<Record<Direction, string[]>>;
    };
    apiRef?: (api: DropdownApi) => void;
};

const Dropdown: Component<DropdownProps> = (props) => {
    const [selectedIndex$, setSelectedIndex] = createSignal(props.value);
    createEffect(() => {
        setSelectedIndex(props.value);
    });
    const [showOptions$, setShowOptions] = createSignal<{ show: boolean; inputSource?: InputSource }>({
        show: false,
        inputSource: undefined
    });

    const clickKey = {};

    let optionsElement!: HTMLDivElement;
    let selectElement!: HTMLDivElement;

    function registerGlobalClose() {
        StateGlobal.onGlobalClick.registerOne(clickKey as any, (ev) => {
            const t = ev.target as Node | null;
            if (!t) return;

            if (!optionsElement?.contains(t) && !selectElement?.contains(t)) {
                StateGlobal.onGlobalClick.unregister(clickKey as any);
                setShowOptions((prev) => ({ show: false, inputSource: prev.inputSource }));
            }
        });
    }

    function open(inputSource: InputSource) {
        setShowOptions({ show: true, inputSource });
        registerGlobalClose();
    }

    function close(inputSource: InputSource) {
        StateGlobal.onGlobalClick.unregister(clickKey as any);
        setShowOptions({ show: false, inputSource });
    }

    function toggle(inputSource: InputSource) {
        setShowOptions((prev) => {
            const nextShow = !prev.show;
            if (nextShow) {
                registerGlobalClose();
            } else {
                StateGlobal.onGlobalClick.unregister(clickKey as any);
            }
            return { show: nextShow, inputSource };
        });
    }

    function selectionChanged(index: number) {
        close(showOptions$().inputSource ?? "gamepad");
        setSelectedIndex(index);
        props.onSelectedChanged(index);
    }

    props.apiRef?.({
        open,
        close,
        toggle,
        isOpen: () => showOptions$().show
    });

    onCleanup(() => {
        StateGlobal.onGlobalClick.unregister(clickKey as any);
    });

    return (
        <div
            class={styles.selectContainer}
            onClick={() => toggle("pointer")}
            style={props.style}
            use:focusable={{
                ...(props.focusableGroupOpts ?? {}),
                focusInert: createMemo(() => props.focusable === false),
                onPress: () => toggle("gamepad"),
                onBack: props.onBack
            }}
        >
            <div ref={selectElement} class={styles.select} style={props.selectStyle}>
                <div class={styles.selectText}>
                    <div style={{ display: "flex", "flex-direction": "column" }}>
                        <Show when={props.label}>
                            <div class={styles.labelText}>{props.label}</div>
                        </Show>
                        {props.options[selectedIndex$()]}
                    </div>
                </div>

                <div style={{ "flex-grow": 1 }} />

                <div class={styles.selectArrow}>
                    <img src={chevDown} style={{ transform: showOptions$().show ? "rotate(-180deg)" : undefined }} />
                </div>
            </div>

            <Show when={showOptions$().show}>
                <div
                    classList={{
                        [styles.optionsContainer]: true,
                        [styles.upwards]: props.direction === "up"
                    }}
                    ref={optionsElement}
                    use:focusScope={{
                        id: "dropdown",
                        initialMode: "trap"
                    }}
                >
                    <Index each={props.options}>
                        {(item: any, i: number) => (
                            <div
                                class={styles.option}
                                classList={{ [styles.selected]: selectedIndex$() === i }}
                                onClick={(e) => {
                                    selectionChanged(i);
                                    e.stopPropagation();
                                }}
                                use:focusable={{
                                    focusInert: createMemo(() => showOptions$().inputSource === "pointer"),
                                    onPress: () => selectionChanged(i),
                                    onBack: (el, inputSource) => {
                                        if (showOptions$().show) {
                                            close(inputSource);
                                            return true;
                                        }
                                        return false;
                                    }
                                }}
                            >
                                <Show when={selectedIndex$() === i}>
                                    <img class={styles.selectIcon} src={check} />
                                </Show>
                                {item()}
                            </div>
                        )}
                    </Index>
                </div>
            </Show>
        </div>
    );
};

export default Dropdown;
