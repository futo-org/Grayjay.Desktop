import { Component, createSignal, onCleanup, Show, Index, JSX } from "solid-js";
import styles from './index.module.css';
import { Portal } from "solid-js/web";
import Anchor, { AnchorFlags, AnchorStyle } from "../../../../utility/Anchor";
import chevDown from "../../../../assets/icons/icon_chrevron_down.svg"
import check from "../../../../assets/icons/icon_checkmark.svg"
import StateGlobal from "../../../../state/StateGlobal";

export interface DropdownProps {
    options: any[];
    value: number;
    onSelectedChanged: (value: number) => void;
    anchorStyle?: AnchorStyle;
    label?: string;
    style?: JSX.CSSProperties;
};

const Dropdown: Component<DropdownProps> = (props) => {    
    const [selectedIndex$, setSelectedIndex] = createSignal(props.value);
    const [showOptions$, setShowOptions] = createSignal(false);

    function selectionChanged(index: number) {
        setShowOptions(false);
        setSelectedIndex(index);
        props.onSelectedChanged(index);
    }

    let toggleShow = () => {
        setShowOptions(!showOptions$());

        if(showOptions$()) {
            StateGlobal.onGlobalClick.registerOne(this, (ev)=>{
              if(ev.target && !optionsElement?.contains(ev.target as Node) && !selectElement.contains(ev.target as Node)) {
                StateGlobal.onGlobalClick.unregister(this);
                setShowOptions(false);
              }
            });
        }
    }

    //let anchor = new Anchor(null, showOptions$, props.anchorStyle ? props.anchorStyle : AnchorStyle.BottomLeft, [AnchorFlags.AnchorMinWidth]);

    let optionsElement: HTMLDivElement;
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
        <div class={styles.selectContainer} onClick={(ev)=>{toggleShow()}} style={props.style}>
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
            <Show when={showOptions$()}>
                    <div class={styles.optionsContainer} ref={(el)=>optionsElement = el}>
                        <Index each={props.options}>{(item: any, i: number) =>
                            <div class={styles.option} classList={{[styles.selected]: selectedIndex$() == i}} onClick={()=>selectionChanged(i)}>
                                <Show when={selectedIndex$() == i}>
                                    <img class={styles.selectIcon} src={check} />
                                </Show>

                                {item()}
                            </div>
                        }</Index>
                    </div>
            </Show>
        </div>
    );
};

export default Dropdown;
