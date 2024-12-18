import { Component, createEffect, createSignal } from 'solid-js'

import styles from './index.module.css';
import check from '../../../../assets/icons/checkboxcheck.svg';

interface CheckboxProps {
    value: boolean;
    onChecked: (value: boolean) => void;
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
        <div class={styles.checkbox} classList={{ [styles.checked]: checked() }} onClick={(ev) => handleChecked(ev)}>
            <img class={styles.check} src={check} />
        </div>
    );
};

export default Checkbox;