import { Component, createSignal, onCleanup, onMount, For, JSX, createMemo, Show, mergeProps, createEffect } from "solid-js";
import styles from './index.module.css';
import { Portal } from "solid-js/web";
import Button from "../../buttons/Button";
import IconButton from "../../buttons/IconButton";
import ic_arrowUp from '../../../assets/icons/arrow_upward.svg';
import ic_arrowDown from "../../../assets/icons/arrow_downward.svg";

export interface ScrollContainerProps {
    ref?: HTMLDivElement;
    children: JSX.Element;
    wrapperStyle?: JSX.CSSProperties;
    scrollStyle?: JSX.CSSProperties;
    scrollToTopButton?: boolean;
    scrollToBottomButton?: boolean;
    scrollSmooth?: boolean;
    onScroll?: (e: Event) => void
};

const ScrollContainer: Component<ScrollContainerProps> = (p) => {
    const props = mergeProps({ scrollToTopButton: !p.scrollToBottomButton ? true : false, scrollToBottomButton: false, scrollSmooth: true }, p);
    if (props.scrollToTopButton && props.scrollToBottomButton) {
        throw new Error("Only one of scrollToTopButton or scrollToBottomButton should be true.");
    }

    const [buttonVisible, setButtonVisible] = createSignal(false);
    
    let scrollingElement: HTMLDivElement | undefined;
    const handleScroll = (e: Event) => {
        scrollingElement =  e.target as HTMLDivElement;

        const scrollTop = scrollingElement.scrollTop;
        const scrollHeight = scrollingElement.scrollHeight;
        const clientHeight = scrollingElement.clientHeight;

        if (props.scrollToTopButton) {
            setButtonVisible(scrollTop > 0);
        }

        if (props.scrollToBottomButton) {
            const distanceFromBottom = scrollHeight - scrollTop - clientHeight;
            setButtonVisible(distanceFromBottom > 0);
        }

        props.onScroll?.(e);
    };

    const handleButtonClick = () => {
        if (!scrollingElement) return;

        scrollingElement.scrollTo({
            top: props.scrollToTopButton ? 0 : scrollingElement.scrollHeight,
            behavior: props.scrollSmooth ? "smooth" : "instant",
        });
    };

    //TODO: Should this use const c = children(() => props.children); ?
    return (
        <>
            <div class={styles.scrollWrapper} style={props.wrapperStyle}>
                <div ref={props.ref} class={styles.containerScroll} onScroll={handleScroll} style={props.scrollStyle}>
                    {props.children}
                    <Show when={buttonVisible()}>
                        <div class={styles.scrollButton} onClick={handleButtonClick}>
                            <IconButton
                                icon={props.scrollToTopButton ? ic_arrowUp : ic_arrowDown}
                                style={{ border: "1px solid #454545" }}
                            />
                        </div>
                    </Show>
                </div>
            </div>
        </>
    );
};

export default ScrollContainer;
