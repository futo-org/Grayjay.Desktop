import { createResource, type Component, Show, Switch, Match, createMemo, For, Index, Accessor } from 'solid-js';

import styles from './index.module.css';
import { HomeBackend } from '../../../backend/HomeBackend';
import ContentGrid from '../../containers/ContentGrid';
import NavigationBar from '../../topbars/NavigationBar';
import ScrollContainer from '../../containers/ScrollContainer';
import StateGlobal from '../../../state/StateGlobal';
import { DateTime } from 'luxon';
import IconButton from '../../buttons/IconButton';

import iconRefresh from "../../../assets/icons/icon_reload_temp.svg"
import iconHome from "../../../assets/icons/icon_nav_home.svg"
import iconSources from "../../../assets/icons/ic_circles.svg"
import { useNavigate, useSearchParams } from '@solidjs/router';
import EmptyContentView from '../../EmptyContentView';
import { DetailsBackend } from '../../../backend/DetailsBackend';
import { TextType } from '../../../backend/models/content/IPlatformPostDetails';
import UIOverlay from '../../../state/UIOverlay';
import SubscribeButton from '../../buttons/SubscribeButton';
import { createResourceDefault, getBestThumbnail, toHumanNowDiffString, toHumanNumber } from '../../../utility';
import RatingView from '../../RatingView';

const PostDetailView: Component = () => {
  const [params, setParams] = useSearchParams();

  const navigate = useNavigate();

  const [details$, detailResources] = createResourceDefault(()=>params.url, async (url)=>{
    if(!url)
        return undefined;
      return UIOverlay.catchDialogExceptions(()=>{
        return DetailsBackend.postLoad(url);
      }, ()=>navigate(-1), ()=>detailResources.refetch());
  });

  
  function onClickAuthor() {
    const author = details$()?.post?.author;
    if (author) {
        navigate("/web/channel?url=" + encodeURIComponent(author.url), { state: { author } });
    }
}

  const pluginIconUrl = createMemo(() => {
    const plugin = StateGlobal.getSourceConfig(details$()?.post?.id?.pluginID);
    return plugin?.absoluteIconUrl;
  });

  let scrollContainerRef: HTMLDivElement | undefined;
  return (
    <div class={styles.container}>
        <NavigationBar isRoot={false} childrenAfter={
          <img src={iconRefresh} style={{"margin-left": "24px", "cursor": "pointer", "height": "30px", "width": "30px" }} 
            onClick={()=>{ detailResources.refetch() }} />
        } />
        <Show when={details$.state == 'ready'}>
          <Show when={details$()?.post}>
            <ScrollContainer ref={scrollContainerRef}>
              <div>
                <div class={styles.authorContainer}>
                  <Show when={details$()?.post?.author?.thumbnail}>
                    <img src={details$()?.post?.author?.thumbnail} class={styles.authorThumbnail} alt="author" onClick={onClickAuthor} referrerPolicy='no-referrer' />
                  </Show>
                  <div class={styles.authorDescription} style={{
                    "margin-left": !!details$()?.post?.author?.thumbnail ? undefined : "40px"
                  }}>
                      <div class={styles.authorName}>{details$()?.post?.author?.name}</div>
                      <div style="flex-grow:1;"></div>
                      <Show when={(details$()?.post?.author?.subscribers ?? 0) > 0}>
                        <div class={styles.authorMetadata} onClick={onClickAuthor}>{toHumanNumber(details$()?.post?.author?.subscribers)} subscribers</div>
                        <div style="flex-grow:1;"></div>
                      </Show>
                  </div>
                  <SubscribeButton author={details$()?.post?.author?.url} style={{"margin-top": "29px", "margin-left": "auto", "margin-right": "20px"}} />

                </div>
                <div class={styles.postTitle}>
                  {details$()?.post?.name}
                </div>
                <div class={styles.postMeta}>
                  <div class={styles.date} title={details$()?.post?.dateTime}>
                      {toHumanNowDiffString(details$()?.post?.dateTime)}
                  </div>
                  <div class={styles.right} style={{"display": "inline-block"}}>
                    <RatingView rating={details$()?.post?.rating} style={{"display": "inline-block"}} />
                    <div class={styles.sourceIcon} style={{"display": "inline-block"}}>
                      <img src={pluginIconUrl()} />
                    </div>
                  </div>
                </div>
                <div class={styles.postBody}>
                  <Switch>
                    <Match when={details$()?.post?.textType == TextType.RAW}>
                      <div class={styles.postRaw}>
                        {details$()?.post?.content}
                      </div>
                    </Match>
                    <Match when={details$()?.post?.textType == TextType.HTML}>
                      <div class={styles.postHtml} innerHTML={details$()?.post?.content}>
                          {
                          /*TODO: Safe html rendering*/
                          }
                      </div>
                    </Match>
                    <Match when={details$()?.post?.textType == TextType.MARKUP}>
                      <div class={styles.postMarkup}>
                        {details$()?.post?.content}
                      </div>
                    </Match>
                  </Switch>
                </div>
                <div class={styles.postImages}>
                  <Index each={details$()?.post?.images}>{(img: Accessor<string>, index: number) => 
                    <div class={styles.postImage} onClick={()=>UIOverlay.overlayImage(img())}>
                      <img style={{"width": "300px", "height": (index == 1) ? "200px" : "300px"}}
                        src={((details$()?.post?.thumbnails && (details$()?.post?.thumbnails.length) ? getBestThumbnail(details$()!.post.thumbnails[index])?.url : img()))} referrerPolicy='no-referrer' />

                    </div>
                  }</Index>
                </div>
              </div>
            </ScrollContainer>
          </Show>
        </Show>
    </div>
  );
};

export default PostDetailView;
