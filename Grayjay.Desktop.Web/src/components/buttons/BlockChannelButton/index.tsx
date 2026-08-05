import { Component, JSX, Show, createMemo, createSignal } from 'solid-js'

import ButtonFlex from '../ButtonFlex';
import LoadingButton from '../LoadingButton';
import StateBlockedChannels from '../../../state/StateBlockedChannels';
import UIOverlay from '../../../state/UIOverlay';

interface BlockChannelButtonProps {
    author: string | undefined;
    name?: string;
    thumbnail?: string;
    pluginId?: string;
    small?: boolean;
    style?: JSX.CSSProperties;
    focusable?: boolean;
    onBlocked?: () => void;
    onUnblocked?: () => void;
}

const BlockChannelButton: Component<BlockChannelButtonProps> = (props) => {
    const [isWorking$, setIsWorking] = createSignal(false);
    const isBlocked = createMemo(() => StateBlockedChannels.isBlocked(props.author));

    async function block(url: string) {
        setIsWorking(true);
        try {
            await StateBlockedChannels.block(url, props.name, props.thumbnail, props.pluginId);
            props.onBlocked?.();
        }
        finally {
            setIsWorking(false);
        }
    }
    async function unblock(url: string) {
        setIsWorking(true);
        try {
            await StateBlockedChannels.unblock(url);
            props.onUnblocked?.();
        }
        finally {
            setIsWorking(false);
        }
    }

    function onBlockClick() {
        const url = props.author;
        if (!url)
            return;
        UIOverlay.overlayConfirm({
            no: () => { },
            yes: () => { block(url); }
        }, "Block this channel? Content from this channel will no longer appear in your home feed, and videos from it will not play.");
    }

    return (
        <>
            <Show when={!isWorking$() && isBlocked()}>
                <ButtonFlex small={props.small} style={{ width: "170px", ...props.style }} text="Unblock" color="#019BE7" onClick={() => {
                    const url = props.author;
                    if (url)
                        unblock(url);
                }} focusableOpts={props.focusable === true ? {
                    onPress: () => {
                        const url = props.author;
                        if (url)
                            unblock(url);
                    }
                } : undefined} />
            </Show>
            <Show when={!isWorking$() && !isBlocked()}>
                <ButtonFlex small={props.small} style={{ width: "170px", ...props.style }} text="Block" color="#C0392B" onClick={onBlockClick} focusableOpts={props.focusable === true ? {
                    onPress: onBlockClick
                } : undefined} />
            </Show>
            <Show when={isWorking$()}>
                <LoadingButton small={props.small} style={{ width: "170px", ...props.style }} text="" color="#019BE7" onClick={() => {

                }} />
            </Show>
        </>
    );
};

export default BlockChannelButton;
