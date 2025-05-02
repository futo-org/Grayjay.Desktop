import { Component, JSX, Show, createMemo } from 'solid-js'

import styles from './index.module.css';
import IconButton from '../../buttons/IconButton';
import more from '../../../assets/icons/more_horiz_FILL0_wght400_GRAD0_opsz24.svg';
import addToQueueIcon from '../../../assets/icons/icon_add_to_queue.svg';
import { dateFromAny, toHumanNowDiffString, toHumanNumber, toHumanTime } from '../../../utility';
import { DateTime } from 'luxon';
import { useNavigate } from '@solidjs/router';
import StateGlobal from '../../../state/StateGlobal';
import { IPlatformVideo } from '../../../backend/models/content/IPlatformVideo';
import AnimatedImage from '../../basics/AnimatedImage';

interface VideoProps {
  video?: IPlatformVideo;
  onClick: () => void;
  onSettings?: (element: HTMLDivElement, content: IPlatformVideo) => void;
  onAddtoQueue?: (element: HTMLDivElement, content: IPlatformVideo) => void;
  style?: JSX.CSSProperties;
  imageStyle?: JSX.CSSProperties;
  useCache?: boolean;
}

const VideoThumbnailView: Component<VideoProps> = (props) => {
  var bestThumbnail$ = createMemo(()=>{
    return (props.video?.thumbnails?.sources?.length ?? 0 > 0) ? props.video?.thumbnails.sources[Math.max(0, props.video.thumbnails.sources.length - 1)] : null;
  })
  var progress$ = createMemo(()=>{
    let videoAny = props.video as any;
    console.log(videoAny?.metadata, props.video?.duration);
    return (videoAny?.metadata?.position && props.video?.duration && props.video.duration > 0) ? (videoAny?.metadata?.position / props.video!.duration) : 0;
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
  let refAddToQueueButton: HTMLDivElement | undefined;

  function startDrag(ev: any){
    ev.dataTransfer?.setData("text/uri-list", props.video?.url ?? ""); 
    console.log(props.video?.url)
  }

  function onClicked(ev: any){
    if(props.onClick)
      props.onClick();
  }

  const showAuthorThumbnail$ = createMemo(() => props.video?.author?.thumbnail && props.video?.author.thumbnail.length);
  return (
    <div class={styles.container} style={props.style}>
        <div class={styles.videoThumbnail} 
          style={{... props.imageStyle}} 
          draggable={true}
          onDragStart={startDrag}
          onClick={onClicked}>
            <AnimatedImage class={styles.image} src={(!props.useCache) ? bestThumbnail$()?.url?.replace("u0026", "&") : "/Images/CachePassthrough?url=" + encodeURIComponent(bestThumbnail$()?.url?.replace("u0026", "&") ?? "")} referrerPolicy='no-referrer' />

          <Show when={pluginIconUrl()}>
            <img src={pluginIconUrl()} class={styles.sourceIcon} />
          </Show>
          <Show when={props.video?.isLive && dateFromAny(props.video?.dateTime, DateTime.max())! <= DateTime.now()}>
            <div class={styles.isLive}>LIVE</div>
          </Show>
          <Show when={props.video?.isLive && dateFromAny(props.video?.dateTime, DateTime.min())! > DateTime.now()}>
            <div class={styles.isPlanned}>PLANNED</div>
          </Show>
          <Show when={!props.video?.isLive}>
            <div class={styles.duration}>{toHumanTime(props.video?.duration ?? 0)}</div>
          </Show>
            <div class={styles.progressBar}>
              <div class={styles.progressBarProgress} style={{width: (progress$() * 100) + "%"}}>

              </div>
            </div>
        </div>
        <div class={styles.title} onClick={props.onClick} onDragStart={startDrag} draggable={true}>{props.video?.name}</div>
        <div class={styles.bottomRow}>
            <Show when={showAuthorThumbnail$()}>
              <AnimatedImage src={props.video?.author.thumbnail} class={styles.authorThumbnail} alt="author thumbnail" onClick={onClickAuthor} referrerPolicy='no-referrer' />
            </Show>
            <div class={styles.authorColumn} style={{
              "margin-left": showAuthorThumbnail$() ? "8px" : undefined
            }}>
                <div class={styles.authorName} onClick={onClickAuthor}>{props.video?.author?.name ?? "Unknown"}</div>
                <Show when={props.video}>
                    <div class={styles.metadata}><Show when={(props.video?.viewCount ?? 0) > 0}>{toHumanNumber(props.video?.viewCount)} views • </Show>{toHumanNowDiffString(props.video?.dateTime)}</div>
                </Show>
            </div>
            

            <Show when={props.onAddtoQueue}>
              <IconButton icon={addToQueueIcon} 
                style={{"margin-right": "7px", "margin-top": "4px"}}
                iconPadding='3px'
                height={"22px"} width={"22px"} ref={refAddToQueueButton} onClick={() => props.onAddtoQueue?.(refAddToQueueButton!, props.video!)} />
            </Show>
            
            <Show when={props.onSettings}>
              <IconButton icon={more} ref={refMoreButton} onClick={() => props.onSettings?.(refMoreButton!, props.video!)} />
            </Show>
        </div>
    </div>
  );
};

export default VideoThumbnailView;