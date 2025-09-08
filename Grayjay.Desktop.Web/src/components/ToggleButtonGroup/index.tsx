import { Component, For, JSX, Show, createSignal } from 'solid-js'

import { focusable } from "../../focusable";  void focusable;
import styles from './index.module.css';

interface ButtonGroupProps {
    defaultSelectedItem?: string;
    items: string[];
    onItemChanged?: (item: string) => void;
    style?: JSX.CSSProperties;
}

const ButtonGroup: Component<ButtonGroupProps> = (props) => {
    const [selectedItem, setSelectedItem] = createSignal<string | undefined>(props.defaultSelectedItem);

    const selectItem = (item: string) => {
        if (selectedItem() !== item) {
            setSelectedItem(item);
            if (props.onItemChanged)
                props.onItemChanged(item);
        }
    };

    return (
        <div class={styles.containerGroup} style={{ ... props.style, width: `${props.items.length * 120}px` }}>
            <For each={props.items}>{(item, i) =>
                <div class={styles.containerButton} classList={{ [styles.active]: item == selectedItem() }} onClick={() => selectItem(item)} use:focusable={{
                    onPress: () => selectItem(item)
                 }}>
                    {item}
                </div>
            }</For>
        </div>
    );
};

export default ButtonGroup;