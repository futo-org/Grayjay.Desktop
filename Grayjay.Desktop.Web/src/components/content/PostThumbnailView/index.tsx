import { Accessor, Component, Index, JSX, Show, createMemo } from 'solid-js'

import styles from './index.module.css';
import IconButton from '../../buttons/IconButton';
import more from '../../../assets/icons/more_horiz_FILL0_wght400_GRAD0_opsz24.svg';
import { dateFromAny, getBestThumbnail, proxyImage, toHumanNowDiffString, toHumanNumber, toHumanTime } from '../../../utility';
import { DateTime } from 'luxon';
import { useNavigate } from '@solidjs/router';
import StateGlobal from '../../../state/StateGlobal';
import { IPlatformVideo } from '../../../backend/models/content/IPlatformVideo';
import { IPlatformPost } from '../../../backend/models/content/IPlatformPost';
import { FocusableOptions } from '../../../nav';
import { focusable } from '../../../focusable'; void focusable;

interface PostProps {
  post?: IPlatformPost;
  onClick: () => void;
  onSettings?: (element: HTMLDivElement, content: IPlatformPost) => void;
  style?: JSX.CSSProperties;
  focusableOpts?: FocusableOptions;
}

const PostThumbnailView: Component<PostProps> = (props) => {
  
  const navigate = useNavigate();
  function onClickAuthor() {
      const author = props.post?.author;
      if(author)
        navigate("/web/channel?url=" + encodeURIComponent(author.url), { state: { author } });
  }

  const hasThumbnails$ = createMemo(()=>props?.post?.thumbnails && props?.post?.thumbnails?.length && props?.post?.thumbnails?.length > 0)

  const pluginIconUrl = createMemo(() => {
    const plugin = StateGlobal.getSourceConfig(props.post?.id?.pluginID);
    return plugin?.absoluteIconUrl;
  });

  let refMoreButton: HTMLDivElement | undefined;

  function startDrag(ev: any){
    ev.dataTransfer?.setData("text/uri-list", props.post?.url ?? ""); 
    console.log(props.post?.url)
  }

  function onClicked(ev: any){
    if(props.onClick)
      props.onClick();
  }

  const showAuthorThumbnail$ = createMemo(() => props.post?.author.thumbnail && props.post?.author.thumbnail.length);
  return (
    <div class={styles.container} style={props.style} use:focusable={props.focusableOpts}>
        <div class={styles.title} onClick={props.onClick} onDragStart={startDrag} draggable={true}>{props.post?.name}</div>
        <div class={styles.description} onClick={props.onClick} style={{height: !hasThumbnails$() ? "calc(100% - 140px)" : "calc(100% - 240px)"}}>
          {props.post?.description}
        </div>
        <div class={styles.descriptionOverlay} style={{bottom: !hasThumbnails$() ? "65px" : "165px"}}></div>
        <Show when={hasThumbnails$}>
          <div class={styles.thumbnails} onClick={props.onClick}>
            <Index each={props.post?.thumbnails}>{(thumb: Accessor<IThumbnails>, index: number)=>
              <Show when={index < 3}>
                <img class={styles.thumbnail} src={getBestThumbnail(thumb())?.url} referrerPolicy='no-referrer' />
              </Show>
            }</Index>
          </div>
        </Show>
        <div class={styles.bottomRow}>
            <Show when={showAuthorThumbnail$()}>
              <img src={props.post?.author.thumbnail} class={styles.authorThumbnail} alt="author thumbnail" onClick={onClickAuthor} referrerPolicy='no-referrer' />
            </Show>
            <Show when={pluginIconUrl()}>
              <img src={pluginIconUrl()} class={styles.sourceIcon} />
            </Show>
            <div class={styles.authorColumn} style={{
              "margin-left": showAuthorThumbnail$() ? "8px" : undefined
            }}>
                <div class={styles.authorName} onClick={onClickAuthor}>{props.post?.author.name}</div>
                <Show when={props.post}>
                    <div class={styles.metadata} title={props.post?.dateTime}>{toHumanNowDiffString(props.post?.dateTime)}</div>
                </Show>
            </div>
        </div>
    </div>
  );
};

export default PostThumbnailView;