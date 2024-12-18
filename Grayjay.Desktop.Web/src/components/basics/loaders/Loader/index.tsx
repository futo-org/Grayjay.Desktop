import { Component, createSignal, onCleanup, onMount, For, JSX, createMemo, Show, Switch, Match } from "solid-js";
import styles from './index.module.css';

export interface LoaderContainerProps {
    background?: string,
    style?: JSX.CSSProperties
};

const Loader: Component<LoaderContainerProps> = (props) => {    
    return (
        <div class={styles.loader} style={props.style}>
            
        </div>
    );
};

export default Loader;
