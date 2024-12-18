import { Component, createSignal, onCleanup, onMount, For, JSX, createMemo, Show, mergeProps, createEffect } from "solid-js";
import styles from './index.module.css';
import { Portal } from "solid-js/web";
import Button from "../../buttons/Button";
import IconButton from "../../buttons/IconButton";
import ic_arrowUp from '../../../assets/icons/arrow_upward.svg';

export interface ScrollContainerProps {
    ref?: HTMLDivElement;
    children: JSX.Element;
    style?: JSX.CSSProperties;
    scrollToTopButton?: boolean;
};

const ScrollContainer: Component<ScrollContainerProps> = (p) => {
    const props = mergeProps({ scrollToTopButton: true }, p);

    let scrollingElement: HTMLDivElement | undefined;
    const [scrollToTopVisible$, setScrollToTopVisible] = createSignal(false);
    const handleScroll = (e: Event) => {
        scrollingElement =  e.target as HTMLDivElement;
        const scrollTop = scrollingElement?.scrollTop ?? 0;
        if (props.scrollToTopButton) {
            setScrollToTopVisible(scrollTop > window.outerHeight);
        }
    };

    //TODO: Should this use const c = children(() => props.children); ?
    return (
        <>
            <div ref={props.ref} class={styles.containerScroll} style={props.style} onScroll={handleScroll}>
                {props.children}
            </div>
            <Show when={scrollToTopVisible$()}>
                <Portal>
                    <div style="position: absolute; bottom: 10px; right: 30px;" onClick={(e) => {
                        scrollingElement?.scrollTo({
                            behavior: "smooth",
                            top: 0
                        });
                    }}>
                        <IconButton icon={ic_arrowUp} style={{
                            "border": "1px solid #454545"
                        }}></IconButton>
                    </div>
                </Portal>
            </Show>
        </>
    );
};

export default ScrollContainer;
