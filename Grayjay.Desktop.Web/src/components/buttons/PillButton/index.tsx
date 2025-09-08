import { Component, Show } from 'solid-js'

import styles from './index.module.css';
import { focusable } from "../../../focusable";  void focusable;
import { FocusableOptions } from '../../../nav';

interface PillButtonProps {
    icon: string;
    text: string;
    onClick?: () => void;
    focusableOpts?: FocusableOptions;
}

const PillButton: Component<PillButtonProps> = (props) => {
    const handleClick = (event: MouseEvent) => {
        if (props.onClick) {
            props.onClick();
        }
    };

    return (
        <div class={styles.container} onClick={handleClick} use:focusable={props.focusableOpts}>
            <img
                class={styles.icon}
                src={props.icon}
                alt={props.text}
            />

            <div class={styles.text}>{props.text}</div>
        </div>
    );
};

export default PillButton;