import { Component, JSX, Show, createMemo } from 'solid-js'

import styles from './index.module.css';
import IconButton from '../../buttons/IconButton';
import more from '../../../assets/icons/more_horiz_FILL0_wght400_GRAD0_opsz24.svg';
import { dateFromAny, proxyImage, toHumanNowDiffString, toHumanNumber, toHumanTime } from '../../../utility';
import { DateTime } from 'luxon';
import { useNavigate } from '@solidjs/router';
import StateGlobal from '../../../state/StateGlobal';
import { IPlatformVideo } from '../../../backend/models/content/IPlatformVideo';
import { IPlatformNestedMedia } from '../../../backend/models/content/IPlatformNestedMedia';
import { IPlatformLockedContent } from '../../../backend/models/content/IPlatformLockedContent';
import { FocusableOptions } from '../../../nav';
import { focusable } from '../../../focusable'; void focusable;

interface LockedContentProps {
  content?: IPlatformLockedContent;
  onClick: () => void;
  onSettings?: (element: HTMLDivElement, content: IPlatformLockedContent) => void;
  style?: JSX.CSSProperties;
  focusableOpts?: FocusableOptions;
}

const LockedContentThumbnailView: Component<LockedContentProps> = (props) => {
  var bestThumbnail$ = createMemo(()=>{
    return (props.content?.contentThumbnails?.sources?.length ?? 0 > 0) ? props.content?.contentThumbnails.sources[Math.max(0, props.video.contentThumbnails.sources.length - 1)] : null;
  })
  
  const navigate = useNavigate();
  function onClickAuthor() {
      const author = props.content?.author;
      if(author)
        navigate("/web/channel?url=" + encodeURIComponent(author.url), { state: { author } });
  }

  const pluginIconUrl = createMemo(() => {
    const plugin = StateGlobal.getSourceConfig(props.content?.id?.pluginID);
    return plugin?.absoluteIconUrl;
  });

  let refMoreButton: HTMLDivElement | undefined;

  function startDrag(ev: any){
    ev.dataTransfer?.setData("text/uri-list", props.content?.url ?? ""); 
    console.log(props.content?.url)
  }

  function onClicked(ev: any){
    if(props.onClick)
      props.onClick();
  }

  const showAuthorThumbnail$ = createMemo(() => props.content?.author.thumbnail && props.content?.author.thumbnail.length);
  return (
    <div class={styles.container} style={props.style} use:focusable={props.focusableOpts}>
        <div class={styles.videoThumbnail} 
          style={{"background-image": "url(" + bestThumbnail$()?.url?.replace("u0026", "&") + ")"}} 
          draggable={true}
          onDragStart={startDrag}
          onClick={onClicked}>

          <Show when={pluginIconUrl()}>
            <img src={pluginIconUrl()} class={styles.sourceIcon} />
          </Show>
          <div class={styles.lockedOverlay}>
            <div class={styles.lockedTextContainer}>
              <div class={styles.lockedTitle}>
                Content is locked
              </div>
              <div class={styles.lockedDescription}>
                {props.content?.lockDescription}
              </div>
              <div class={styles.lockedUrl}>
                {props.content?.unlockUrl}
              </div>
            </div>
          </div>
        </div>
        <div class={styles.title} onClick={props.onClick} onDragStart={startDrag} draggable={true}>{props.video?.name}</div>
        <div class={styles.bottomRow}>
            <Show when={showAuthorThumbnail$()}>
              <img src={props.content?.author.thumbnail} class={styles.authorThumbnail} alt="author thumbnail" onClick={onClickAuthor} referrerPolicy='no-referrer' />
            </Show>
            <div class={styles.authorColumn} style={{
              "margin-left": showAuthorThumbnail$() ? "8px" : undefined
            }}>
                <div class={styles.authorName} onClick={onClickAuthor}>{props.content?.author.name}</div>
                <Show when={props.content}>
                    <div class={styles.metadata} title={props.content?.dateTime}>{toHumanNowDiffString(props.content?.dateTime)}</div>
                </Show>
            </div>
            <Show when={props.onSettings}>
              <IconButton icon={more} ref={refMoreButton} onClick={() => props.onSettings?.(refMoreButton!, props.content!)} />
            </Show>
        </div>
    </div>
  );
};

export default LockedContentThumbnailView;