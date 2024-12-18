import { Component, For, JSX, Show, createEffect, createSignal } from 'solid-js'

import styles from './index.module.css';

interface ButtonGroupProps {
    defaultSelectedItem?: string;
    items: string[];
    onItemChanged?: (item: string) => void;
    style?: JSX.CSSProperties;
}

const ButtonGroup: Component<ButtonGroupProps> = (props) => {
    const [selectedItem, setSelectedItem] = createSignal<string | undefined>(props.defaultSelectedItem);
    createEffect(() => {
        setSelectedItem(props.defaultSelectedItem);
    })

    const selectItem = (item: string) => {
        if (selectedItem() !== item) {
            setSelectedItem(item);
            if (props.onItemChanged)
                props.onItemChanged(item);
        }
    };

    return (
        <div class={styles.containerGroup} style={props.style}>
            <For each={props.items}>{(item, i) =>
                <div class={styles.containerButton} classList={{ [styles.active]: item == selectedItem() }} onClick={() => selectItem(item)}>
                    {item}
                </div>
            }</For>
        </div>
    );
};

export default ButtonGroup;