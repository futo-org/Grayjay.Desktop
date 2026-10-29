import { type Component, Show, createMemo } from 'solid-js';
import { useNavigate } from '@solidjs/router';
import styles from './index.module.css';
import iconDrag from '../../assets/icons/icon_drag.svg';
import iconClose from '../../assets/icons/icon24_close.svg';
import iconMore from '../../assets/icons/icon_button_more.svg';
import { proxyImage, toHumanNowDiffString, toHumanNumber } from '../../utility';
import { DateTime } from 'luxon';
import IconButton from '../buttons/IconButton';

import { IPlatformVideo } from '../../backend/models/content/IPlatformVideo';
import { FocusableOptions } from '../../nav';
import { focusable } from '../../focusable';import { useFocus } from '../../FocusProvider';
 void focusable;

interface PlaylistItemViewProps {
  item?: IPlatformVideo;
  onPlay?: () => void;
  onRemove?: () => void;
  onSettings?: (e: HTMLElement) => void;
  onDragStart?: (e: MouseEvent, el: HTMLElement) => void;
  isEditable?: boolean;
  isRemovable?: boolean;
  focusableOpts?: FocusableOptions;
}

const PlaylistItemView: Component<PlaylistItemViewProps> = (props) => {
  const focus = useFocus();
  const navigate = useNavigate();
  let moreElement: HTMLDivElement | undefined;

  const bestThumbnail$ = createMemo(() => {
    return (props.item && props.item.thumbnails.sources.length > 0) ? props.item.thumbnails.sources[Math.max(0, props.item.thumbnails.sources.length - 1)] : null;
  });

  const editable$ = createMemo(() => props.isEditable ?? true);
  const removable$ = createMemo(() => props.isRemovable ?? editable$());
  const progress$ = createMemo(() => {
    const pos = props.item?.metadata?.position;
    const dur = props.item?.duration;
    if (pos && dur && dur > 0) {
      return Math.min(1, pos / dur);
    }
    return props.item?.metadata?.watched ? 1 : 0;
  });

  return (
    <div class={styles.container} onClick={() => {{
      props.onPlay?.();
    }}} use:focusable={props.focusableOpts}>
      <Show when={props.onDragStart && editable$() && focus?.isControllerMode() !== true} fallback={<div style="width: 12px"></div>}>
        <img src={iconDrag} style="width: 24px; height: 24px; padding: 20px; cursor: pointer;" onMouseDown={(e) => props.onDragStart?.(e, e.target as HTMLElement)} />
      </Show>
      <div style="position: relative; width: auto; height: 100%; border-radius: 6px; aspect-ratio: 16/9; overflow: hidden; flex-shrink: 0; cursor: pointer;">
        <img src={bestThumbnail$()?.url} style="width: 100%; height: 100%; object-fit: cover; display: block;" referrerPolicy='no-referrer' />
        <Show when={progress$() > 0}>
          <div style="position: absolute; bottom: 0px; left: 0px; right: 0px; height: 3px; background-color: rgba(255, 255, 255, 0.2);">
            <div style={{ height: "100%", "background-color": "#019BE7", width: `${progress$() * 100}%` }} />
          </div>
        </Show>
      </div>
      <div style="display: flex; flex-direction: column; flex-grow: 1; margin-left: 20px; margin-right: 20px; height: 100%; cursor: pointer;">
        <div class={styles.itemTitle}>{props.item?.name}</div>
        <div class={styles.authorBottomRow}>
          <Show when={props.item?.author && (props.item?.author?.thumbnail?.length ?? 0) > 0}>
            <img src={props.item?.author?.thumbnail} class={styles.authorThumbnail} alt="author thumbnail" onClick={(e) => {{
              navigate("/web/channel?url=" + encodeURIComponent(props.item?.author.url), { state: { author: props.item?.author } });
              e.preventDefault();
              e.stopPropagation();
            }}} referrerPolicy='no-referrer' />
          </Show>
          <div class={styles.authorColumn}>
            <div class={styles.authorName} onClick={(e) => {
              navigate("/web/channel?url=" + encodeURIComponent(props.item?.author.url), { state: { author: props.item?.author } });
              e.preventDefault();
              e.stopPropagation();
            }}>{props.item?.author.name}</div>
            <Show when={props.item}>
              <div class={styles.metadata}><Show when={(props.item?.viewCount ?? 0) > 0}>{toHumanNumber(props.item?.viewCount)} • </Show>{toHumanNowDiffString(props.item?.dateTime)}</div>
            </Show>
          </div>
        </div>
      </div>
      <Show when={removable$() && focus?.isControllerMode() !== true}>
        <IconButton icon={iconClose} style={{ "margin-left": "16px" }} onClick={(e) => {
          props.onRemove?.();
          e.preventDefault();
          e.stopPropagation();
        }} />
      </Show>
      <Show when={focus?.isControllerMode() !== true} fallback={<div class="menu-anchor"></div>}>
        <IconButton ref={moreElement} icon={iconMore} style={{ "margin-left": "16px", "margin-right": "16px" }} onClick={(e) => {
          props.onSettings?.(e.target as HTMLElement);
          e.preventDefault();
          e.stopPropagation();
        }} />
      </Show>
    </div>
  );
};

export default PlaylistItemView;
