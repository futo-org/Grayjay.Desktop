import { Component, Match, Show, Switch, createEffect, createMemo, createSignal } from 'solid-js'

import styles from './index.module.css';

import iconGrid from '../../../assets/icons/icon_grid_active.svg';
import iconList from '../../../assets/icons/icon_list_active.svg';
import iconGridInactive from '../../../assets/icons/icon_grid_inactive.svg';
import iconListInactive from '../../../assets/icons/icon_list_inactive.svg';
import { focusable } from '../../../focusable'; void focusable;
import { FocusableOptions } from '../../../nav';

interface ViewTypeTogglesProps {
    value: string;
    onToggle: (value: string) => void;
    focusableOpts?: FocusableOptions;
}

const ViewTypeToggles: Component<ViewTypeTogglesProps> = (props) => {
    const [toggle, setToggle] = createSignal(props.value);
    createEffect(() => {
        setToggle(props.value);
    });

    function handleToggle(val: string) {
        setToggle(val);
        props.onToggle(val);
    }

    const createModifiedFocusableOpts = (type: string) => {
        return createMemo(() => props.focusableOpts && !props.focusableOpts.onPress ? { ... props.focusableOpts, onPress: () => handleToggle(type) } : props.focusableOpts);
    };

    return (
        <div class={styles.toggleViewTypes}>
            <div class={styles.toggleViewType} classList={{ [styles.enabled]: toggle() == "grid" }} onClick={(ev) => {
                handleToggle("grid");
                ev.preventDefault();
                ev.stopPropagation();
            }} use:focusable={createModifiedFocusableOpts("grid")()}>
                <Show when={toggle() == "grid"}>
                    <img src={iconGrid} />
                </Show>
                <Show when={toggle() != "grid"}>
                    <img src={iconGridInactive} />
                </Show>
            </div>
            <div class={styles.toggleViewType} classList={{ [styles.enabled]: toggle() == "list" }} onClick={(ev) => {
                handleToggle("list");
                ev.preventDefault();
                ev.stopPropagation();
            }} use:focusable={createModifiedFocusableOpts("list")()}>
                <Show when={toggle() == "list"}>
                    <img src={iconList} />
                </Show>
                <Show when={toggle() != "list"}>
                    <img src={iconListInactive} />
                </Show>
            </div>
        </div>
    );
};

export default ViewTypeToggles;