import { Component, JSX, Match, Show, Switch, createResource, createSignal } from 'solid-js'

import ButtonFlex from '../ButtonFlex';
import { SubscriptionsBackend } from '../../../backend/SubscriptionsBackend';
import LoadingButton from '../LoadingButton';
import { createResourceDefault } from '../../../utility';

interface SubscribeButtonProps {
    author: string | undefined;
    small?: boolean;
    style?: JSX.CSSProperties;
    onIsSubscribedChanged?: (isSubscribed: boolean) => void;
    isSubscribedInitialState?: boolean;
    focusable?: boolean;
}

const SubscribeButton: Component<SubscribeButtonProps> = (props) => {    
    const [isSubscribed$, isSubscribedResource] = createResourceDefault(() => props.author, async (author) => (!author) ? undefined : await SubscriptionsBackend.isSubscribed(author));
    const [isSubscribing$, setIsSubscribing] = createSignal(false);

    async function subscribe(url: string) {
        setIsSubscribing(true);
        try {
            const isSubscribed = await SubscriptionsBackend.subscribe(url);
            isSubscribedResource.mutate(isSubscribed);
            props.onIsSubscribedChanged?.(isSubscribed);
        }
        finally {
            setIsSubscribing(false);
        }
    }
    async function unsubscribe(url: string) {
        setIsSubscribing(true);
        try {
            const isSubscribed = await SubscriptionsBackend.unsubscribe(url);
            isSubscribedResource.mutate(isSubscribed);
            props.onIsSubscribedChanged?.(isSubscribed);
        }
        finally {
            setIsSubscribing(false);
        }
    }

    return (
        <>
            <Show when={!isSubscribing$() && (isSubscribed$() === true || (isSubscribed$() === undefined && props.isSubscribedInitialState === true))}>
                <ButtonFlex style={{ width: "170px", ... props.style }} small={props.small} text="Unsubscribe" color="#2E2E2E" onClick={ () => {
                    const url = props.author;
                    if (url)
                        unsubscribe(url);
                }} focusableOpts={props.focusable === true ? {
                    onPress: () => {
                        const url = props.author;
                        if (url)
                            unsubscribe(url);
                    }
                } : undefined} />
            </Show>
            <Show when={!isSubscribing$() && (isSubscribed$() === false || (isSubscribed$() === undefined && props.isSubscribedInitialState === false))}>
                <ButtonFlex style={{ width: "170px", ... props.style }} small={props.small} text="Subscribe" color="#019BE7" onClick={ () => {
                    const url = props.author;
                    if (url)
                        subscribe(url);
                }} focusableOpts={props.focusable === true ? {
                    onPress: () => {
                        const url = props.author;
                        if (url)
                            subscribe(url);
                    }
                } : undefined} />
            </Show>
            <Show when={isSubscribing$()}>
                <LoadingButton style={{ width: "170px", ... props.style }} small={props.small} text="" color="#019BE7" onClick={ () => {
                    
                }} />
            </Show>
        </>
    );
};

export default SubscribeButton;