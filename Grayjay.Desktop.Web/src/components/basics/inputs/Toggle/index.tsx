import { Component, createEffect, createSignal } from 'solid-js'

import styles from './index.module.css';

interface ToggleProps {
    value: boolean;
    onToggle: (value: boolean) => void;
}

const Toggle: Component<ToggleProps> = (props) => {
    const [toggle, setToggle] = createSignal(props.value);
    createEffect(() => {
        setToggle(props.value);
    });

    function handleToggle(ev: MouseEvent) {
        const newValue = !toggle();
        setToggle(newValue);
        props.onToggle(newValue);
        ev.preventDefault();
        ev.stopPropagation();
    }

    return (
        <div class={styles.toggle} classList={{ [styles.enabled]: toggle() }} onClick={(ev) => handleToggle(ev)}>
            <div class={styles.thumb}></div>
        </div>
    );
};

export default Toggle;