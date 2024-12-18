import { Component, Show } from 'solid-js'

import styles from './index.module.css';

interface PillButtonProps {
    icon: string;
    text: string;
    onClick?: (event: MouseEvent) => void;
}

const PillButton: Component<PillButtonProps> = (props) => {
    const handleClick = (event: MouseEvent) => {
        if (props.onClick) {
            props.onClick(event);
        }
    };

    return (
        <div class={styles.container} onClick={handleClick}>
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