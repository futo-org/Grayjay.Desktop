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
import { useLocation, useNavigate, useSearchParams } from '@solidjs/router';
import EmptyContentView from '../../EmptyContentView';
import { DetailsBackend } from '../../../backend/DetailsBackend';
import { IPlatformPostDetails, TextType } from '../../../backend/models/content/IPlatformPostDetails';
import { IPlatformPost } from '../../../backend/models/content/IPlatformPost';
import UIOverlay from '../../../state/UIOverlay';
import SubscribeButton from '../../buttons/SubscribeButton';
import { createResourceDefault, getBestThumbnail, toHumanNowDiffString, toHumanNumber } from '../../../utility';
import RatingView from '../../RatingView';

const PostDetailView: Component = () => {
  const [params, setParams] = useSearchParams();

  const navigate = useNavigate();
  const location = useLocation();
  const existingPost = createMemo(() => (location.state as { post?: IPlatformPost } | undefined)?.post);

  const [details$, detailResources] = createResourceDefault(()=>({ url: params.url, post: existingPost() }), async (source)=>{
    if(!source.post && !source.url)
        return undefined;
    if(source.post && "textType" in source.post)
        return { post: source.post as IPlatformPostDetails };
    return UIOverlay.catchDialogExceptions(()=>{
      return DetailsBackend.postLoad(source.url!);
    }, ()=>navigate(-1), ()=>detailResources.refetch());
  });

  const detailPost = createMemo<IPlatformPostDetails | undefined>(() => details$.loading ? undefined : details$()?.post);
  const displayPost = createMemo<IPlatformPost | undefined>(() => detailPost() ?? existingPost());

  
  function onClickAuthor() {
    const author = displayPost()?.author;
    if (author) {
        navigate("/web/channel?url=" + encodeURIComponent(author.url), { state: { author } });
    }
}

  const pluginIconUrl = createMemo(() => {
    const plugin = StateGlobal.getSourceConfig(displayPost()?.id?.pluginID);
    return plugin?.absoluteIconUrl;
  });

  let scrollContainerRef: HTMLDivElement | undefined;
  return (
    <div class={styles.container}>
        <NavigationBar isRoot={false} childrenAfter={
          <IconButton
            icon={iconRefresh}
            variant="none"
            shape="circle"
            width="30px"
            height="30px"
            iconInset="0px"
            style={{ "margin-left": "24px" }}
            onClick={() => {
              detailResources.refetch();
            }}
          />
        } />
        <Show when={displayPost()}>
          <ScrollContainer ref={scrollContainerRef}>
            <div>
              <div class={styles.authorContainer}>
                <Show when={displayPost()?.author?.thumbnail}>
                  <img src={displayPost()?.author?.thumbnail} class={styles.authorThumbnail} alt="author" onClick={onClickAuthor} referrerPolicy='no-referrer' />
                </Show>
                <div class={styles.authorDescription} style={{
                  "margin-left": !!displayPost()?.author?.thumbnail ? undefined : "40px"
                }}>
                    <div class={styles.authorName} onClick={onClickAuthor}>{displayPost()?.author?.name}</div>
                    <div style="flex-grow:1;"></div>
                    <Show when={(displayPost()?.author?.subscribers ?? 0) > 0}>
                      <div class={styles.authorMetadata} onClick={onClickAuthor}>{toHumanNumber(displayPost()?.author?.subscribers)} subscribers</div>
                      <div style="flex-grow:1;"></div>
                    </Show>
                </div>
                <SubscribeButton author={displayPost()?.author?.url} style={{"margin-top": "29px", "margin-left": "auto", "margin-right": "20px"}} />

              </div>
              <div class={styles.postTitle}>
                {displayPost()?.name}
              </div>
              <div class={styles.postMeta}>
                <div class={styles.date}>
                    {toHumanNowDiffString(displayPost()?.dateTime)}
                </div>
                <div class={styles.right} style={{"display": "inline-block"}}>
                  <RatingView rating={detailPost()?.rating} style={{"display": "inline-block"}} />
                  <div class={styles.sourceIcon} style={{"display": "inline-block"}}>
                    <img src={pluginIconUrl()} />
                  </div>
                </div>
              </div>
              <div class={styles.postBody}>
                <Switch>
                  <Match when={detailPost()?.textType == TextType.RAW}>
                    <div class={styles.postRaw}>
                      {detailPost()?.content}
                    </div>
                  </Match>
                  <Match when={detailPost()?.textType == TextType.HTML}>
                    <div class={styles.postHtml} innerHTML={detailPost()?.content}>
                        {
                        /*TODO: Safe html rendering*/
                        }
                    </div>
                  </Match>
                  <Match when={detailPost()?.textType == TextType.MARKUP}>
                    <div class={styles.postMarkup}>
                      {detailPost()?.content}
                    </div>
                  </Match>
                </Switch>
              </div>
              <div class={styles.postImages}>
                <Index each={displayPost()?.images}>{(img: Accessor<string>, index: number) =>
                  <div class={styles.postImage} onClick={()=>UIOverlay.overlayImage(img())}>
                    <img style={{"width": "300px", "height": (index == 1) ? "200px" : "300px"}}
                      src={getBestThumbnail(displayPost()?.thumbnails?.[index])?.url ?? img()} referrerPolicy='no-referrer' />

                  </div>
                }</Index>
              </div>
            </div>
          </ScrollContainer>
        </Show>
    </div>
  );
};

export default PostDetailView;
