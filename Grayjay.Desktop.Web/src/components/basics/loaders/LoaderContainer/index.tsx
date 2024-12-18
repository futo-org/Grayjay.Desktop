import { Component, createSignal, onCleanup, onMount, For, JSX, createMemo, Show, Switch, Match } from "solid-js";
import styles from './index.module.css';
import Loader from "../Loader";

export interface LoaderContainerProps {
    loader?: JSX.Element | undefined;
    children: JSX.Element | undefined;
    isLoading: boolean,
    loadingText?: string,
    loadingSubText?: string,
    background?: string,
    style?: JSX.CSSProperties
};

const LoaderContainer: Component<LoaderContainerProps> = (props) => {    
    return (
        <div class={styles.containerLoader} style={{...props.style, "background-color": ((props.background) ? props.background : "transparant")}}>
            <Switch>
                <Match when={props.isLoading && !props.loader}>
                    <div class={styles.loaderWrapper}>
                        <Loader />
                        <Show when={props.loadingText}>
                            <div class={styles.loadingText}>
                                {props.loadingText}
                            </div>
                        </Show>
                        <Show when={props.loadingSubText}>
                            <div class={styles.loadingSubText}>
                                {props.loadingSubText}
                            </div>
                        </Show>
                    </div>
                </Match>
                <Match when={props.isLoading && props.loader}>
                    {props.loader}
                </Match>
                <Match when={!props.isLoading}>
                    {props.children}
                </Match>
            </Switch>
        </div>
    );
};

export default LoaderContainer;
