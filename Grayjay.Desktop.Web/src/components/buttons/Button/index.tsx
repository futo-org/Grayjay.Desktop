import { Component, JSX, Show } from 'solid-js'

import styles from './index.module.css';

interface ButtonProps {
    icon?: string;
    text: string;
    color?: string;
    onClick?: (event: MouseEvent) => void;
    small?: boolean;
    style?: JSX.CSSProperties;
}

const Button: Component<ButtonProps> = (props) => {
    const handleClick = (event: MouseEvent) => {
        if (props.onClick) {
            props.onClick(event);
        }
    };

    return (
        <div class={styles.container} classList={{[styles.small]: props.small}} onClick={handleClick} style={{... props.style, "background": props.color ?? "var(--grey-color-8)", width: (props.style?.width ?? "fit-content")}}>
            <Show when={props.icon}>
                <img src={props.icon} class={styles.icon} alt={props.text} />
            </Show>
            <div class={styles.text}>{props.text}</div>
        </div>
    );
};

export default Button;