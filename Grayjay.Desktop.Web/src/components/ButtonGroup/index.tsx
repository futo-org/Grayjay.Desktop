import { Component, For, JSX, Show, createEffect, createMemo, createSignal } from 'solid-js'
import { focusable } from "../../focusable"; void focusable;
import { FocusableOptions } from '../../nav';
import styles from './index.module.css';

interface ButtonGroupProps {
    defaultSelectedItem?: string;
    items: string[];
    onItemChanged?: (item: string) => void;
    style?: JSX.CSSProperties;
    focusableOpts?: FocusableOptions;
}

const ButtonGroup: Component<ButtonGroupProps> = (props) => {
    const [selectedItem, setSelectedItem] = createSignal<string | undefined>(props.defaultSelectedItem);
    createEffect(() => {
        setSelectedItem(props.defaultSelectedItem);
    })

    const selectItem = (item: string) => {
        console.info("selectItem", item);
        if (selectedItem() !== item) {
            setSelectedItem(item);
            if (props.onItemChanged)
                props.onItemChanged(item);
        }
    };

    const getFocusableOpts = (item: string) => {
        if (props.focusableOpts && !props.focusableOpts.onPress)
            return { ... props.focusableOpts, onPress: () => selectItem(item) };
        return props.focusableOpts;
    };

    return (
        <div class={styles.containerGroup} style={props.style}>
            <For each={props.items}>{(item, i) =>
                <div class={styles.containerButton} classList={{ [styles.active]: item == selectedItem() }} onClick={() => selectItem(item)} use:focusable={getFocusableOpts(item)}>
                    {item}
                </div>
            }</For>
        </div>
    );
};

export default ButtonGroup;