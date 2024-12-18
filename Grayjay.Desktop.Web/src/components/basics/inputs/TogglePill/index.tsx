import { Component, createEffect, createSignal } from 'solid-js'

import styles from './index.module.css';

interface TogglePillProps {
    name: string;
    value: boolean;
    onToggle: (value: boolean) => void;
}

const TogglePill: Component<TogglePillProps> = (props) => {
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
        <div class={styles.togglePill} classList={{ [styles.enabled]: toggle() }} onClick={(ev) => handleToggle(ev)}>
            {props.name}
        </div>
    );
};

export default TogglePill;