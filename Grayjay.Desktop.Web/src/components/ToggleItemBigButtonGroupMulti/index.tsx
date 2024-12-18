import { Component, For, JSX, Show, createEffect, createSignal } from 'solid-js'

import styles from './index.module.css';
import StateGlobal from '../../state/StateGlobal';

export interface ToggleBigButtonGroupItemMulti {
    text: string;
    value: any;
    icon: string;
};

interface ToggleItemBigButtonGroupPropsMulti {
    defaultSelectedValues?: any[];
    items: ToggleBigButtonGroupItemMulti[];
    onValueChanged?: (items: any[]) => void;
    style?: JSX.CSSProperties;
};

const ToggleItemBigButtonGroupMulti: Component<ToggleItemBigButtonGroupPropsMulti> = (props) => {
    const [selectedItems, setSelectedItems] = createSignal<any[] | undefined>(props.defaultSelectedValues);
    createEffect(() => {
        setSelectedItems(props.defaultSelectedValues);
    });

    const selectItem = (item: ToggleBigButtonGroupItemMulti) => {
        const isSelected = selectedItems()?.some(v => v === item.value) ?? false;
        if (isSelected) {
            setSelectedItems(selectedItems()!.filter(v => v !== item.value));
        } else {
            setSelectedItems([ ... selectedItems() ?? [], item.value ]);
        }

        if (props.onValueChanged)
            props.onValueChanged(selectedItems() ?? []);
    };

    return (
        <div class={styles.containerGroup}style={{ ... props.style }}>
            <For each={props.items}>{(item, i) =>
                <>
                    <div class={styles.containerButton} classList={{ [styles.active]: selectedItems()?.some(v => v === item.value) ?? false }} onClick={() => selectItem(item)}>
                        <Show when={item.icon}>
                            <img src={item.icon} style={{
                                "width": "48px",
                                "height": "48px",
                                "object-fit": "contain",
                                "flex-shrink": 0
                            }} />
                        </Show>
                        <div style="margin-top: 6px;">{item.text}</div>
                    </div>
                </>
            }</For>
        </div>
    );
};

export default ToggleItemBigButtonGroupMulti;