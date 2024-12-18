import { Component, For, JSX, Show, createEffect, createSignal } from 'solid-js'

import styles from './index.module.css';

export interface ToggleButtonGroupItem {
    text: string;
    value: any;
    icon?: string;
};

interface ToggleItemButtonGroupProps {
    defaultSelectedValue?: any;
    items?: ToggleButtonGroupItem[];
    onValueChanged?: (item: any) => void;
    style?: JSX.CSSProperties;
};

const ToggleItemButtonGroup: Component<ToggleItemButtonGroupProps> = (props) => {
    const [selectedItem, setSelectedItem] = createSignal<any | undefined>(props.defaultSelectedValue);
    createEffect(() => {
        setSelectedItem(props.defaultSelectedValue);
    });

    const toggleItem = (item: ToggleButtonGroupItem) => {
        if (selectedItem() != item.value) {
            setSelectedItem(item.value);
        } else {
            setSelectedItem(undefined);
        }

        if (props.onValueChanged)
            props.onValueChanged(selectedItem());
    };

    return (
        <div class={styles.containerGroup} style={{ ... props.style }}>
            <For each={props.items}>{(item, i) =>
                <>
                    <Show when={i() > 0}>
                        <div style="height: 100%; width: 1px; background-color: #454545;"></div>
                    </Show>
                    <div class={styles.containerButton} classList={{ [styles.active]: item.value == selectedItem() }} onClick={() => toggleItem(item)}>
                        <Show when={item.icon}>
                            <img src={item.icon} style="width: 16px; height: 16px; flex-shrink: 0;" />
                        </Show>
                        <div>{item.text}</div>
                    </div>
                </>
            }</For>
        </div>
    );
};

export default ToggleItemButtonGroup;