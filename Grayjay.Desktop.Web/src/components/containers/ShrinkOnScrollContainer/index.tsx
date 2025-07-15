import { Component, createSignal, onCleanup, onMount, JSX, untrack, createEffect } from "solid-js";
import styles from './index.module.css';
import { getNestedOffsetTop } from "../../../utility";

export interface ShrinkOnScrollContainerProps {
    outerContainerRef: HTMLDivElement | undefined;
    maximumHeight: number;
    minimumHeight: number;
    children: JSX.Element;
};

const ShrinkOnScrollContainer: Component<ShrinkOnScrollContainerProps> = (props) => {
    let containerRef: HTMLDivElement | undefined;
    const [height, setHeight] = createSignal<number>(props.maximumHeight);
    const handleScroll = () => {
        if (!props.outerContainerRef || !containerRef) 
            return;

        const scrollDistance = props.outerContainerRef.scrollTop - getNestedOffsetTop(containerRef, props.outerContainerRef);
        const h = height();
        const newHeight = Math.min(Math.max(props.maximumHeight - scrollDistance, props.minimumHeight), props.maximumHeight);
        setHeight(newHeight);
    };

    const resizeObserver = new ResizeObserver(entries => {
        handleScroll();
    });

    createEffect(() => {
        setHeight(props.maximumHeight);
    });

    onMount(() => {
        resizeObserver.observe(props.outerContainerRef!);
        //window.addEventListener('resize', handleScroll);
        props.outerContainerRef?.addEventListener('scroll', handleScroll);
    });

    onCleanup(() => {
        resizeObserver.unobserve(props.outerContainerRef!);
        //window.removeEventListener('resize', handleScroll);
        props.outerContainerRef?.removeEventListener('scroll', handleScroll);
        resizeObserver.disconnect();
    });

    return (
        <>
            <div ref={containerRef} class={styles.container} style={{height: `${height()}px`, "padding-top": `${props.maximumHeight - height()}px`}}>
                {props.children}
            </div>
        </>
    );
};

export default ShrinkOnScrollContainer;