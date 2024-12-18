import { Component, JSX, Show } from 'solid-js'

import spinner from '../../../assets/icons/tube-spinner.svg';

import styles from './index.module.css';
import Loader from '../../basics/loaders/Loader';
import CircleLoader from '../../basics/loaders/CircleLoader';

interface LoadingButtonProps {
    text: string;
    color?: string;
    onClick?: (event: MouseEvent) => void;
    small?: boolean;
    style?: JSX.CSSProperties;
}

const LoadingButton: Component<LoadingButtonProps> = (props) => {
    const handleClick = (event: MouseEvent) => {
        if (props.onClick) {
            props.onClick(event);
        }
    };

    return (
        <div class={styles.container} onClick={handleClick} style={{... props.style, "background-color": props.color ?? "#212122", width: props.style?.width ?? "fit-content"}} classList={{[styles.small]: props.small}}>
            <CircleLoader style={{height: "30px", width: "30px" }} />
        </div>
    );
};

export default LoadingButton;