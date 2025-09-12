import { Component, JSX, Show } from 'solid-js'

import styles from './index.module.css';
import { focusable } from "../../../focusable"; void focusable;
import { FocusableOptions } from '../../../nav';

interface IconButtonProps {
    icon: string;
    width?: string;
    height?: string;
    iconPadding?: string;
    alt?: string;
    onClick?: (event: MouseEvent) => void;
    ref?: HTMLDivElement | undefined;
    style?: JSX.CSSProperties;
    focusableOpts?: FocusableOptions;
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
            padding: props.iconPadding || "0px"
        }} onClick={handleClick} use:focusable={props.focusableOpts}>
            <img
                class={styles.icon}
                src={props.icon}

                alt={props.alt || 'icon'}
            />
        </div>
    );
};

export default IconButton;