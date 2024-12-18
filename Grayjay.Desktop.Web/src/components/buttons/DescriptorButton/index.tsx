import { Component, JSX, Show } from 'solid-js'

import styles from './index.module.css';

interface DescriptorButtonProps {
    icon?: string;
    text: string;
    description: string;
    color?: string;
    onClick?: (event: MouseEvent) => void;
    small?: boolean;
    disabled?: boolean;
    style?: JSX.CSSProperties;
}

const DescriptorButton: Component<DescriptorButtonProps> = (props) => {
    const handleClick = (event: MouseEvent) => {
        if(props.disabled)
            return;
        if (props.onClick) {
            props.onClick(event);
        }
    };

    return (
        <div class={styles.container} classList={{[styles.small]: props.small}} onClick={handleClick} style={{... props.style, width: (props.style?.width ?? "fit-content"), opacity: (props.disabled ? "0.3" : "1.0")}}>
            <Show when={props.icon}>
                <img src={props.icon} class={styles.icon} alt={props.text} />
            </Show>
            <div class={styles.textContainer}>
                <div class={styles.text}>
                        <div class={styles.title}>
                            {props.text}
                        </div>
                        <div class={styles.description}>
                            {props.description}
                        </div>
                </div>
            </div>
            <div ></div>
        </div>
    );
};

export default DescriptorButton;