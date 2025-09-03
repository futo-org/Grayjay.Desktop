import { Component, createSignal, onCleanup, Show, Index, JSX, createMemo, batch } from "solid-js";
import styles from './index.module.css';
import { AnchorStyle } from "../../../../utility/Anchor";
import chevDown from "../../../../assets/icons/icon_chrevron_down.svg"
import check from "../../../../assets/icons/icon_checkmark.svg"
import StateGlobal from "../../../../state/StateGlobal";
import { focusScope } from '../../../../focusScope'; void focusScope;
import { focusable } from '../../../../focusable'; void focusable;
import { FocusableOptions, OpenIntent } from "../../../../nav";

export interface DropdownProps {
    options: any[];
    value: number;
    onSelectedChanged: (value: number) => void;
    anchorStyle?: AnchorStyle;
    label?: string;
    style?: JSX.CSSProperties;
    focusable?: boolean;
};

const Dropdown: Component<DropdownProps> = (props) => {    
    const [selectedIndex$, setSelectedIndex] = createSignal(props.value);
    const [showOptions$, setShowOptions] = createSignal<{ show: boolean, openIntent?: OpenIntent }>({ show: false, openIntent: undefined });

    function selectionChanged(index: number) {
        setShowOptions({ show: false, openIntent: showOptions$().openIntent });
        setSelectedIndex(index);
        props.onSelectedChanged(index);
    }

    let toggleShow = (openIntent: OpenIntent) => {
        setShowOptions({ show: !showOptions$().show, openIntent });        

        if(showOptions$()) {
            StateGlobal.onGlobalClick.registerOne(this, (ev)=>{
              if(ev.target && !optionsElement?.contains(ev.target as Node) && !selectElement.contains(ev.target as Node)) {
                StateGlobal.onGlobalClick.unregister(this);
                setShowOptions({ show: false, openIntent: showOptions$().openIntent });
              }
            });
        }
    }

    //let anchor = new Anchor(null, showOptions$, props.anchorStyle ? props.anchorStyle : AnchorStyle.BottomLeft, [AnchorFlags.AnchorMinWidth]);

    let optionsElement!: HTMLDivElement;
    let selectElement: HTMLDivElement;
    function refSelectElement(element: HTMLDivElement) {
        selectElement = element;
        //anchor.setElement(selectElement);
    }
    onCleanup(()=>{
        //anchor?.dispose();
        StateGlobal.onGlobalClick.unregister(this);
    });
    
    return (
        <div class={styles.selectContainer} onClick={() => toggleShow(OpenIntent.Pointer)} style={props.style} use:focusable={{ onPress: () => toggleShow(OpenIntent.Gamepad) }}>
            <div ref={refSelectElement} class={styles.select}>
                <div class={styles.selectText}>
                    <div style={{"display": "flex", "flex-direction": "column"}}>
                        <Show when={props.label}>
                            <div class={styles.labelText}>{props.label}</div>
                        </Show>
                        {props.options[selectedIndex$()]}
                    </div>
                </div>
                <div style={{"flex-grow": 1}}></div>
                <div class={styles.selectArrow}>
                    <img src={chevDown} style={{ transform: (showOptions$()) ? "rotate(-180deg)" : undefined }} />
                </div>
            </div>
            <Show when={showOptions$().show}>
                <div class={styles.optionsContainer} ref={optionsElement} use:focusScope={{
                    trap: true,
                    wrap: true,
                    orientation: "vertical"
                }}>
                    <Index each={props.options}>{(item: any, i: number) =>
                        <div class={styles.option} classList={{[styles.selected]: selectedIndex$() == i}} onClick={()=>selectionChanged(i)} use:focusable={{
                                focusInert: createMemo(() => showOptions$().openIntent === OpenIntent.Pointer),
                                onPress: () => selectionChanged(i),
                                onBack: (el, openIntent) => {
                                    if (showOptions$().show) {
                                        setShowOptions({ show: false, openIntent });
                                        return true;
                                    }
                                    return false;
                                },
                            }}>
                            <Show when={selectedIndex$() == i}>
                                <img class={styles.selectIcon} src={check} />
                            </Show>

                            {item()}
                        </div>
                    }
                    </Index>
                </div>
            </Show>
        </div>
    );
};

export default Dropdown;
