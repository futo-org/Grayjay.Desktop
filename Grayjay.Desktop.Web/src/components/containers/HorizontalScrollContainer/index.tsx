import { Component, JSX } from "solid-js";
import styles from './index.module.css';

export interface HorizontalScrollContainerProps {
    ref?: HTMLDivElement;
    children: JSX.Element;
    style?: JSX.CSSProperties;
    subtle?: boolean;
};

const ScrollContainer: Component<HorizontalScrollContainerProps> = (props) => {
        //TODO: Should this use const c = children(() => props.children); ?
    return (
        <>
            <div ref={props.ref} class={props.subtle ? styles.containerScrollSubtle : styles.containerScroll} style={props.style}>
                {props.children}
            </div>
        </>
    );
};

export default ScrollContainer;
