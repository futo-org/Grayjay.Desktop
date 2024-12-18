import { Component, JSX, Match, Show, Switch, createEffect } from 'solid-js'

import styles from './index.module.css';
import { IRating, IRatingLikes, IRatingLikesDislikes, IRatingScaler, RatingTypes } from '../../backend/models/IRating';

import ic_like_active from '../../assets/icons/like_active.svg';
import ic_dislike_active from '../../assets/icons/dislike_active.svg';
import ic_like_inactive from '../../assets/icons/like_inactive.svg';
import ic_dislike_inactive from '../../assets/icons/dislike_inactive.svg';
import { toHumanNumber } from '../../utility';

interface RatingViewProps {
    rating?: IRating;
    style?: JSX.CSSProperties;
    hasLiked?: boolean;
    hasDisliked?: boolean;
    editable?: boolean;
}

const RatingView: Component<RatingViewProps> = (props) => {
    const renderLikes = (rating: IRatingLikes) => {
        return (
            <div class={styles.containerLikeDislike}>
                <img src={props.hasLiked ? ic_like_active : ic_like_inactive} style="width: 32px; height: 32px;" />
                <div class={styles.text} classList={{ [styles.active]: props.hasLiked }}>{toHumanNumber(rating.likes ?? 0)}</div>
            </div>
        );
    };

    const renderLikesDislikes = (rating: IRatingLikesDislikes) => {
        return (
            <>
                <div style="display: flex; flex-direction: row; align-items: center; height: 100%;">
                    <div class={styles.containerLikeDislike} classList={{ [styles.editable]: props.editable }}>
                        <img src={props.hasLiked ? ic_like_active : ic_like_inactive} style="width: 32px; height: 32px;" />
                        <div class={styles.text} classList={{ [styles.active]: props.hasLiked }}>{toHumanNumber(rating.likes ?? 0)}</div>
                    </div>
                    <div class={styles.containerLikeDislike} classList={{ [styles.editable]: props.editable }} style={{ "margin-left": "12px" }}>
                        <img src={props.hasDisliked ? ic_dislike_active : ic_dislike_inactive} style="width: 32px; height: 32px;" />
                        <div class={styles.text} classList={{ [styles.active]: props.hasDisliked }}>{toHumanNumber(rating.dislikes ?? 0)}</div>
                    </div>
                </div>
            </>
        );
    };

    const renderScaler = (rating: IRatingScaler) => {
        return (
            <div>Scaler ({rating.value})</div>
        );
    };

    return (
        <Show when={props.rating}>
            <div class={styles.containerRating} style={props.style}>
                <Switch>
                    <Match when={props.rating?.type === RatingTypes.Likes}>
                        {renderLikes(props.rating as IRatingLikes)}
                    </Match>
                    <Match when={props.rating?.type === RatingTypes.LikesDislikes}>
                        {renderLikesDislikes(props.rating as IRatingLikesDislikes)}
                    </Match>
                    <Match when={props.rating?.type === RatingTypes.Scaler}>
                        {renderScaler(props.rating as IRatingScaler)}
                    </Match>
                </Switch>
            </div>
        </Show>
    );
};

export default RatingView;