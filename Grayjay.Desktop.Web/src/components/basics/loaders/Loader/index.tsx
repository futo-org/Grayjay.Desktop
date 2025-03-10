import { Component } from "solid-js";
import styles from './index.module.css';

export interface LoaderProps {
  size?: string;
}

const Loader: Component<LoaderProps> = (props) => {
    return (
        <span class={styles.loader} style={{ '--size': props.size || '6rem' }} />
    );
};

export default Loader;