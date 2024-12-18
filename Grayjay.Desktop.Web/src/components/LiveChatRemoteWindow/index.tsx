import { Component, JSX, Match, Show, Switch, createEffect, onMount } from 'solid-js'

import styles from './index.module.css';
import { IRating, IRatingLikes, IRatingLikesDislikes, IRatingScaler, RatingTypes } from '../../backend/models/IRating';

import ic_like_active from '../../assets/icons/like_active.svg';
import ic_dislike_active from '../../assets/icons/dislike_active.svg';
import ic_like_inactive from '../../assets/icons/like_inactive.svg';
import ic_dislike_inactive from '../../assets/icons/dislike_inactive.svg';
import { toHumanNumber } from '../../utility';
import { ILiveChatWindowDescriptor } from '../../backend/models/comments/ILiveChatWindowDescriptor';

interface LiveChatRemoteWindowProps {
    style?: JSX.CSSProperties;
    descriptor?: ILiveChatWindowDescriptor;
}

const LiveChatRemoteWindow: Component<LiveChatRemoteWindowProps> = (props) => {
    let iframe: HTMLIFrameElement | undefined;

    return (
        <div class={styles.container} style={props.style}>
            <Show when={!!props.descriptor}>
                <div class={styles.window}>
                    <iframe ref={iframe} src={props.descriptor!.url} referrerPolicy='origin' allow="cross-origin"></iframe>
                </div>
            </Show>
        </div>
    );
};

export default LiveChatRemoteWindow;