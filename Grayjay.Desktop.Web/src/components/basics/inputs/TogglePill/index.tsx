import { Component, createEffect, createSignal } from 'solid-js'

import styles from './index.module.css';
import { focusable } from '../../../../focusable'; void focusable;
import { FocusableOptions } from '../../../../nav';

interface TogglePillProps {
    name: string;
    value: boolean;
    onToggle: (value: boolean) => void;
    focusableOpts?: FocusableOptions;
}

const TogglePill: Component<TogglePillProps> = (props) => {
    const [toggle, setToggle] = createSignal(props.value);
    createEffect(() => {
        setToggle(props.value);
    });

    function handleToggle() {
        const newValue = !toggle();
        setToggle(newValue);
        props.onToggle(newValue);
    }

    return (
        <div class={styles.togglePill} classList={{ [styles.enabled]: toggle() }} onClick={(ev) => {
            handleToggle();
            ev.preventDefault();
            ev.stopPropagation();
        }} use:focusable={props.focusableOpts && !props.focusableOpts.onPress ? { ... props.focusableOpts, onPress: handleToggle } : props.focusableOpts}>
            {props.name}
        </div>
    );
};

export default TogglePill;