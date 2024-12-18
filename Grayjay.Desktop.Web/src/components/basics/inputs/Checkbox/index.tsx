import { Component, JSX, Show, createEffect, createSignal } from 'solid-js'

import styles from './index.module.css';
import check from '../../../../assets/icons/checkboxcheck.svg';

interface CheckboxProps {
    value: boolean;
    label?: string;
    onChecked: (value: boolean) => void;
    style?: JSX.CSSProperties;
    checkBoxStyle?: JSX.CSSProperties;
    checkStyle?: JSX.CSSProperties;
}

const Checkbox: Component<CheckboxProps> = (props) => {
    const [checked, setChecked] = createSignal(props.value);
    createEffect(() => {
        setChecked(props.value);
    });

    function handleChecked(ev: MouseEvent) {
        const newValue = !checked();
        setChecked(newValue);
        props.onChecked(newValue);
        ev.preventDefault();
        ev.stopPropagation();
    }

    return (
        <div style={props.style} class={styles.containerCheckbox} onClick={(ev) => handleChecked(ev)}>
            <div class={styles.checkbox} classList={{ [styles.checked]: checked() }} style={props.checkBoxStyle}>
                <img class={styles.check} src={check} style={props.checkStyle} />
            </div>
            <Show when={props.label}>
                <div class={styles.label} classList={{ [styles.checked]: checked() }}>{props.label}</div>
            </Show>
        </div>
    );
};

export default Checkbox;