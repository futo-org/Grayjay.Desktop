import { Component, createSignal, onCleanup, onMount, JSX, untrack, createEffect, Accessor, createMemo } from "solid-js";
import styles from './index.module.css';
import { ShrinkProgressContext } from "../../../contexts/ShrinkProgress";

export interface StickyShrinkOnScrollContainerProps {
    outerContainerRef: HTMLDivElement | undefined;
    maximumHeight: number;
    minimumHeight: number;
    heightChanged?: (newHeight: number) => void;
    children: JSX.Element;
    sticky?: boolean;
};

const StickyShrinkOnScrollContainer: Component<StickyShrinkOnScrollContainerProps> = (props) => {
    let containerRef: HTMLDivElement | undefined;
    let originalTop: number = 0;
    const [height, setHeight] = createSignal<number>(props.maximumHeight);
    const progress = createMemo<number>(() => {
        return 1.0 - (height() - props.minimumHeight) / (props.maximumHeight - props.minimumHeight);
    });

    const handleScroll = () => {
        if (!props.outerContainerRef || !containerRef) 
            return;

        const scrollDistance = props.outerContainerRef.scrollTop - originalTop;
        const newHeight = Math.min(Math.max(props.maximumHeight - scrollDistance, props.minimumHeight), props.maximumHeight);
        if (height() != newHeight)
            props.heightChanged?.(newHeight);
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

        //TODO: Needs to be fixed when StickyShrinkOnScrollContainer is not the first element
        //originalTop = containerRef?.offsetTop ?? 0;
    });

    onCleanup(() => {
        resizeObserver.unobserve(props.outerContainerRef!);
        //window.removeEventListener('resize', handleScroll);
        props.outerContainerRef?.removeEventListener('scroll', handleScroll);
        resizeObserver.disconnect();
    });

    return (
        <ShrinkProgressContext.Provider value={progress}>
            <div ref={containerRef} class={styles.container} style={{
                height: `${height()}px`, 
                "padding-bottom": props.sticky ? `${props.maximumHeight - height()}px` : undefined,
                "padding-top": !props.sticky ? `${props.maximumHeight - height()}px` : undefined,
                "position": props.sticky ? "sticky" : undefined
            }}>
                {props.children}
            </div>
        </ShrinkProgressContext.Provider>
    );
};

export default StickyShrinkOnScrollContainer;