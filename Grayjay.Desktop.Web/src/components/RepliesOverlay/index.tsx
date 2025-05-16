import { Component, Show, createMemo } from 'solid-js'

import styles from './index.module.css';
import thumbsUp from '../../assets/icons/icon24_thumbsup.svg';
import comment from '../../assets/icons/icon24_comment.svg';
import { proxyImage, toHumanNowDiffString, toHumanNumber } from '../../utility';
import { DateTime } from 'luxon';
import { decode } from 'html-entities';
import PillButtonSmall from '../buttons/PillButtonSmall';
import { toSvg } from "jdenticon";
import { ISerializedComment } from '../../backend/models/comments/ISerializedComment';
import RatingView from '../RatingView';
import CustomButton from '../buttons/CustomButton';
import { DetailsBackend } from '../../backend/DetailsBackend';

interface RepliesOverlayProps {
  parents?: ISerializedComment[];
  onClick: () => void;
  onRepliesClicked: () => void;
  editable?: boolean;
}

const RepliesOverlay: Component<RepliesOverlayProps> = (props) => {
  return (
    <div class={styles.container} onClick={props.onClick}>
      <div class={styles.authorContainer}>
        <div class={styles.containerAuthorThumbnail}>
          <Show when={thumbnail()} fallback={jidenticon()}>
            <img src={thumbnail()} alt="author thumbnail" referrerPolicy='no-referrer' />
          </Show>
        </div>

        <div class={styles.thumbnailContainer}>
          <div style="display: flex; flex-direction: row; align-items: center;">
            <div class={styles.authorName}>{props.comment?.author.name}</div>
            <Show when={props.comment?.date}>
              <div class={styles.metadata} title={props.comment?.date}>{toHumanNowDiffString(props.comment?.date)}</div>
            </Show>
          </div>
          <pre class={styles.text}>
            {decode(props.comment?.message)}
          </pre>
        </div>
      </div>
      <Show when={props.comment?.rating || props.comment?.replyCount}>
        <div class={styles.buttonList}>
          <RatingView rating={props.comment?.rating} />
          <Show when={props.editable || (props.comment?.replyCount ?? 0) > 0}>
            <div class={styles.repiesButton} onClick={props.onRepliesClicked}>
              <img src={comment} style="width: 12px; height: 12px; margin-right: 4px;" />
              {`${toHumanNumber(props.comment?.replyCount ?? 0) ?? "0"} replies`}
            </div>
          </Show>
        </div>
      </Show>
    </div>
  );
};

export default RepliesOverlay;