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
        <Switch>
            <Match when={!isSubscribing$() && (isSubscribed$() === true || (isSubscribed$() === undefined && props.isSubscribedInitialState === true))}>
                <ButtonFlex style={{ width: "170px", ... props.style }} small={props.small} text="Unubscribe" color="#2E2E2E" onClick={ () => {
                    const url = props.author;
                    if (url)
                        unsubscribe(url);
                }} />
            </Match>
            <Match when={!isSubscribing$() && (isSubscribed$() === false || (isSubscribed$() === undefined && props.isSubscribedInitialState === false))}>
                <ButtonFlex style={{ width: "170px", ... props.style }} small={props.small} text="Subscribe" color="#019BE7" onClick={ () => {
                    const url = props.author;
                    if (url)
                        subscribe(url);
                }} />
            </Match>
            <Match when={isSubscribing$()}>
                <LoadingButton style={{ width: "170px", ... props.style }} small={props.small} text="" color="#019BE7" onClick={ () => {
                    
                }} />
            </Match>
        </Switch>
    );
};

export default SubscribeButton;