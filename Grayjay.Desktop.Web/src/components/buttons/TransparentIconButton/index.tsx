import { Component, JSX } from 'solid-js'

import styles from './index.module.css';

interface TransparentIconButtonProps {
    icon: string;
    alt?: string;
    onClick?: (event: MouseEvent) => void;
    ref?: HTMLDivElement | undefined
    style?: JSX.CSSProperties
}

const TransparentIconButton: Component<TransparentIconButtonProps> = (props) => {
    const handleClick = (event: MouseEvent) => {
        if (props.onClick) {
            props.onClick(event);
        }
    };

    return (
        <div class={styles.containerTransparentButton} ref={props.ref} style={{
            ... props.style,
            width: props.style?.width ?? '48px',
            height: props.style?.height ?? '48px'
        }} onClick={handleClick}>
            <img
                class={styles.icon}
                src={props.icon}
                alt={props.alt || 'icon'}
            />
        </div>
    );
};

export default TransparentIconButton;