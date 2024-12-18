import { Component, createSignal, onCleanup, onMount, For, JSX, createMemo, Show, Switch, Match } from "solid-js";
import styles from './index.module.css';

export interface SkeletonProps {
    children?: JSX.Element | undefined;
    style?: JSX.CSSProperties;
};

const SkeletonDiv: Component<SkeletonProps> = (props) => {    

    return (
        <div class={styles.skeletonBox} style={props.style}>
            {props.children}
        </div>
    );
};

export default SkeletonDiv;
