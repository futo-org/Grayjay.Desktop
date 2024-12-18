import { Component, JSX, Show } from 'solid-js'

import styles from './index.module.css';

interface IconButtonProps {
    icon: string;
    width?: string;
    height?: string;
    alt?: string;
    onClick?: (event: MouseEvent) => void;
    ref?: HTMLDivElement | undefined;
    style?: JSX.CSSProperties;
}

const IconButton: Component<IconButtonProps> = (props) => {
    const handleClick = (event: MouseEvent) => {
        if (props.onClick) {
            props.onClick(event);
        }
    };

    return (
        <div class={styles.container} ref={props.ref} style={{
            ... props.style,
            width: props.width || '32px',
            height: props.height || '32px',
        }} onClick={handleClick}>
            <img
                class={styles.icon}
                src={props.icon}

                alt={props.alt || 'icon'}
            />
        </div>
    );
};

export default IconButton;