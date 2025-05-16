import { Component, JSX, Show, createMemo } from 'solid-js'

import styles from './index.module.css';
import comment from '../../assets/icons/icon24_comment.svg';
import { preventDragDrop, proxyImage, sanitzeHtml, toHumanNowDiffString, toHumanNumber } from '../../utility';
import { DateTime } from 'luxon';
import { decode } from 'html-entities';
import { toSvg } from "jdenticon";
import { ISerializedComment } from '../../backend/models/comments/ISerializedComment';
import RatingView from '../RatingView';

interface CommentViewProps {
  comment?: ISerializedComment;
  onClick?: (ev: MouseEvent) => void;
  onRepliesClicked?: () => void;
  editable?: boolean;
  style?: JSX.CSSProperties;
}

const CommentView: Component<CommentViewProps> = (props) => {
  const thumbnail = createMemo(() => {
    const c = props.comment;
    if (!c) {
      return undefined;
    }

    const t = c.author.thumbnail;
    if (!t || t.length === 0) {
      return undefined;
    }

    return proxyImage(t);
  });

  const jidenticon = createMemo(() => {
    const c = props.comment;
    if (!c) {
      return undefined;
    }

    const parser = new DOMParser();
    const svgDoc = parser.parseFromString(toSvg(c.author.url, 40), "image/svg+xml");
    const svgElement = svgDoc.documentElement;
    return svgElement;
  });

  const message = createMemo(() => {
    return sanitzeHtml(props.comment?.message ?? "");
  });

  return (
    <div class={styles.container} onClick={props.onClick} style={props.style}>
      <div class={styles.authorContainer}>
        <div class={styles.containerAuthorThumbnail}>
          <Show when={thumbnail()} fallback={jidenticon()}>
            <img src={thumbnail()} alt="author thumbnail" />
          </Show>
        </div>

        <div class={styles.thumbnailContainer}>
          <div style="display: flex; flex-direction: row; align-items: center;">
            <div class={styles.authorName}>{props.comment?.author.name}</div>
            <Show when={props.comment?.date}>
              <div class={styles.metadata} title={props.comment?.date}>{toHumanNowDiffString(props.comment?.date)}</div>
            </Show>
          </div>
          <pre class={styles.text} innerHTML={message()} ondragstart={(ev)=>preventDragDrop(ev)} />
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

export default CommentView;