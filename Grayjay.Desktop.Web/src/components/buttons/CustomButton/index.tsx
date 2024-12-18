import { Component, JSX, Show, createEffect } from 'solid-js'

import styles from './index.module.css';

interface CustomButtonProps {
    icon?: string;
    text: string;
    style?: JSX.CSSProperties;
    iconStyle?: JSX.CSSProperties;
    textStyle?: JSX.CSSProperties;
    background?: string;
    border?: string;
    onClick?: (event: MouseEvent) => void;
    onMouseDown?: (event: MouseEvent) => void;
}

const CustomButton: Component<CustomButtonProps> = (props) => {
    return (
        <div class={styles.container} onClick={props.onClick} onMouseDown={props.onMouseDown} style={{ background: props.background, border: props.border, ... props.style }}>
            <Show when={props.icon}>
                <img src={props.icon} class={styles.icon} alt={props.text} style={props.iconStyle} />
            </Show>
            <div style={props.textStyle}>{props.text}</div>
        </div>
    );
};

export default CustomButton;