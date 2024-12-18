import { Component, createSignal, onCleanup, onMount, For, JSX, createMemo, Show, Switch, Match } from "solid-js";
import styles from './index.module.css';

export interface CircleLoaderProps {
    style?: JSX.CSSProperties;
};

const CircleLoader: Component<CircleLoaderProps> = (props) => {    
    return (
        <span class={styles.loader} style={props.style}></span>
    );
};

export default CircleLoader;
