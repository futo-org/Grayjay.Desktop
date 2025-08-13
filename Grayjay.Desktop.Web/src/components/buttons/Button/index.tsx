import { Component, JSX, Show } from 'solid-js'

import styles from './index.module.css';
import { FocusableOptions } from '../../../nav';
import { focusable } from '../../../focusable'; void focusable;

interface ButtonProps {
    icon?: string;
    text: string;
    color?: string;
    focusColor?: string;
    onClick?: (event: MouseEvent) => void;
    small?: boolean;
    style?: JSX.CSSProperties;
    focusableOpts?: FocusableOptions;
}

const Button: Component<ButtonProps> = (props) => {
    const handleClick = (event: MouseEvent) => {
        if (props.onClick) {
            props.onClick(event);
        }
    };

    const style = {
        ...props.style,
        '--btn-bg': props.color ?? '#212122',
        '--btn-bg-focus': props.focusColor ?? props.color ?? '#212122',
        width: props.style?.width ?? 'fit-content',
    } as JSX.CSSProperties & Record<string, string>;

    return (
        <div class={styles.container} classList={{[styles.small]: props.small}} style={style} onClick={handleClick} use:focusable={props.focusableOpts}>
            <Show when={props.icon}>
                <img src={props.icon} class={styles.icon} alt={props.text} />
            </Show>
            <div class={styles.text}>{props.text}</div>
        </div>
    );
};

export default Button;