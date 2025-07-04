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

interface NestedMediaProps {
  video?: IPlatformNestedMedia;
  onClick: () => void;
  onSettings?: (element: HTMLDivElement, content: IPlatformNestedMedia) => void;
  style?: JSX.CSSProperties;
}

const NestedMediaThumbnailView: Component<NestedMediaProps> = (props) => {
  var bestThumbnail$ = createMemo(()=>{
    return (props.video?.contentThumbnails?.sources?.length ?? 0 > 0) ? props.video?.contentThumbnails.sources[Math.max(0, props.video.contentThumbnails.sources.length - 1)] : null;
  })
  
  const navigate = useNavigate();
  function onClickAuthor() {
      const author = props.video?.author;
      if(author)
        navigate("/web/channel?url=" + encodeURIComponent(author.url), { state: { author } });
  }

  const pluginIconUrl = createMemo(() => {
    const plugin = StateGlobal.getSourceConfig(props.video?.id?.pluginID);
    return plugin?.absoluteIconUrl;
  });

  let refMoreButton: HTMLDivElement | undefined;

  function startDrag(ev: any){
    ev.dataTransfer?.setData("text/uri-list", props.video?.url ?? ""); 
    console.log(props.video?.url)
  }

  function onClicked(ev: any){
    if(props.onClick)
      props.onClick();
  }

  const showAuthorThumbnail$ = createMemo(() => props.video?.author.thumbnail && props.video?.author.thumbnail.length);
  return (
    <div class={styles.container} style={props.style}>
        <div class={styles.videoThumbnail} 
          style={{"background-image": "url(" + bestThumbnail$()?.url?.replace("u0026", "&") + ")"}} 
          draggable={true}
          onDragStart={startDrag}
          onClick={onClicked}>

          <Show when={pluginIconUrl()}>
            <img src={pluginIconUrl()} class={styles.sourceIcon} />
          </Show>
          <Show when={props.video?.pluginThumbnail}>
            <img src={props.video?.pluginThumbnail} class={styles.nestedSourceIcon} />
          </Show>
        </div>
        <div class={styles.title} onClick={props.onClick} onDragStart={startDrag} draggable={true}>{props.video?.name}</div>
        <div class={styles.bottomRow}>
            <Show when={showAuthorThumbnail$()}>
              <img src={props.video?.author.thumbnail} class={styles.authorThumbnail} alt="author thumbnail" onClick={onClickAuthor} referrerPolicy='no-referrer' />
            </Show>
            <div class={styles.authorColumn} style={{
              "margin-left": showAuthorThumbnail$() ? "8px" : undefined
            }}>
                <div class={styles.authorName} onClick={onClickAuthor}>{props.video?.author.name}</div>
                <Show when={props.video}>
                    <div class={styles.metadata} title={props.video?.dateTime}>{toHumanNowDiffString(props.video?.dateTime)}</div>
                </Show>
            </div>
            <Show when={props.onSettings}>
              <IconButton icon={more} ref={refMoreButton} onClick={() => props.onSettings?.(refMoreButton!, props.video!)} />
            </Show>
        </div>
    </div>
  );
};

export default NestedMediaThumbnailView;