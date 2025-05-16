import { createResource, type Component, createSignal, onMount, createMemo, createEffect, untrack, batch, Match, Switch, Show } from 'solid-js';
import { useVideo } from '../../contexts/VideoProvider';
import LoaderContainer from '../../components/basics/loaders/LoaderContainer';
import { HistoryBackend } from '../../backend/HistoryBackend';
import NavigationBar from '../../components/topbars/NavigationBar';
import styles from './index.module.css';
import InputText from '../../components/basics/inputs/InputText';
import CustomButton from '../../components/buttons/CustomButton';
import ic_trash from '../../assets/icons/icon_trash.svg';
import ic_more from '../../assets/icons/icon_button_more.svg';
import ic_search from '../../assets/icons/icon24_search.svg';
import iconDownload from '../../assets/icons/icon24_download.svg';
import iconQueue from '../../assets/icons/icon_add_to_queue.svg';
import no_videos_in_history from '../../assets/no_videos_in_history.svg';
import iconWatchLater from '../../assets/icons/icon24_watch_later.svg';
import iconAddToPlaylist from '../../assets/icons/icon24_add_to_playlist.svg';
import VirtualList from '../../components/containers/VirtualList';
import ScrollContainer from '../../components/containers/ScrollContainer';
import { IHistoryVideo } from '../../backend/models/content/IHistoryVideo';
import { getVideoProgressPercentage, proxyImage, toHumanNowDiffStringMinDay, toHumanNumber } from '../../utility';
import { DateTime, Duration } from 'luxon';
import IconButton from '../../components/buttons/IconButton';
import SettingsMenu, { Menu, MenuItemButton, MenuSeperator } from '../../components/menus/Overlays/SettingsMenu';
import Anchor, { AnchorStyle } from '../../utility/Anchor';
import { WatchLaterBackend } from '../../backend/WatchLaterBackend';
import UIOverlay from '../../state/UIOverlay';
import { Portal } from 'solid-js/web';
import { useNavigate } from '@solidjs/router';
import { Pager } from '../../backend/models/pagers/Pager';
import SkeletonDiv from '../../components/basics/loaders/SkeletonDiv';

const HistoryPage: Component = () => {
  const video = useVideo();

  const [query$, setQuery] = createSignal<string>();
  const [historyPager$, setHistoryPager] = createSignal<Pager<IHistoryVideo>>();

  let initialLoadComplete = false;
  let isLoading = false;
  const updateHistoryPager = async (query?: string) => {
    if (isLoading) {
      return;
    }

    isLoading = true;

    try {
      console.log("Fetching history", {query});
      if (query && query.length > 0) {
        const pager = await HistoryBackend.historySearchPager(query);
        setHistoryPager(pager);
      } else {
        const pager = await HistoryBackend.historyPager();
        setHistoryPager(pager);
      }
    } finally {
      isLoading = false;
      initialLoadComplete = true;
    }
  };

  createEffect(() => {
    updateHistoryPager(query$());
  });

  async function onScrollEnd() {
    if (isLoading) {
      return;
    }

    isLoading = true;

    try {
      const historyPager = historyPager$();
      if (historyPager?.hasMore)
        await historyPager?.nextPage();
    } finally {
      isLoading = false;
    }
  }

  const clearHistory = (promptText: string, timeMinutes: number) => {
    UIOverlay.overlayConfirm({
      yes: async () => {
        await HistoryBackend.removeHistoryRange(timeMinutes);
        updateHistoryPager();
      }
    }, promptText);
  };

  const [settingsContent$, setSettingsContent] = createSignal<IHistoryVideo | undefined>();
  const settingsMenu$ = createMemo(() => {
      const content = settingsContent$();
      if (!content) {
        return {
          title: "",
          items: [
            new MenuItemButton("Last hour", ic_trash, undefined, async () => {
              setShow(false);
              clearHistory("Are you sure you want to remove the last hour of history?", 60);
            }),
            new MenuItemButton("Last 24h", ic_trash, undefined, async () => {
              setShow(false);
              clearHistory("Are you sure you want to remove the last 24 hours of history?", 24 * 60);
            }),
            new MenuItemButton("Last 7 days", ic_trash, undefined, async () => {
              setShow(false);
              clearHistory("Are you sure you want to remove the last 7 days of history?", 7 * 24 * 60);
            }),
            new MenuItemButton("Last 30 days", ic_trash, undefined, async () => {
              setShow(false);
              clearHistory("Are you sure you want to remove the last 30 days of history?", 30 * 24 * 60);
            }),
            new MenuItemButton("Last year", ic_trash, undefined, async () => {
              setShow(false);
              clearHistory("Are you sure you want to remove the last year of history?", 365 * 24 * 60);
            }),
            new MenuItemButton("Everything", ic_trash, undefined, async () => {
              setShow(false);
              clearHistory("Are you sure you want to remove ALL history?", -1);
            })
          ]
        } as Menu;
      }
      return {
          title: "",
          items: [
            new MenuItemButton("Add to queue", iconQueue, undefined, () => {
              setShow(false);
              video?.actions.addToQueue(content.video);
            }),
            new MenuItemButton("Watch later", iconWatchLater, undefined, async () => {
              setShow(false);
              await WatchLaterBackend.add(content.video);
              await video?.actions?.refetchWatchLater();
            }),
            new MenuItemButton("Add to playlist", iconAddToPlaylist, undefined, async () => {
              setShow(false);
              await UIOverlay.overlayAddToPlaylist(content.video);
            }),
            new MenuItemButton("Download video", iconDownload, undefined, () => {
              setShow(false);
            }),
            new MenuSeperator(),
            new MenuItemButton("Delete history", ic_trash, undefined, async () => {
              const historyPager = historyPager$();
              if (!historyPager) {
                return;
              }

              await HistoryBackend.removeHistory(content.video.url);
              const index = historyPager.data.indexOf(content);
              if (index >= 0) {
                historyPager.data.splice(index, 1);
                historyPager.removed(index, 1);
              }

              const indexFiltered = historyPager.dataFiltered.indexOf(content);
              if (indexFiltered >= 0) {
                historyPager.dataFiltered.splice(index, 1);
                historyPager.removedFiltered(index, 1);
              }
            }),
          ]
      } as Menu;
  });
  const [show$, setShow] = createSignal<boolean>(false);
  const contentAnchor = new Anchor(null, show$, AnchorStyle.BottomRight);

  onMount(() => {
    updateHistoryPager();
  });

  let scrollContainerRef: HTMLDivElement | undefined;
  let refMoreButton: HTMLDivElement | undefined;
  return (
    <>
      <ScrollContainer ref={scrollContainerRef}>
        <div class={styles.container}>
          <NavigationBar isRoot={true} />
          <div style="display: flex; flex-direction: row; width: 100%; align-items: center; margin-top: 40px; margin-bottom: 40px;">
            <div class={styles.title}>Watch History</div>
            <div style="flex-grow: 1;"></div>
            <InputText 
              placeholder='Search through history'
              small={true}
              style={{
                "width": "300px",
              }} inputContainerStyle={{
                "background-color": "var(--grey-color-8)"
              }}
              onTextChanged={(newVal) => setQuery(newVal)}
              icon={ic_search}
              showClearButton={true} />

              <CustomButton 
                icon={ic_trash}
                text='Clear history'
                style={{
                  "margin-left": "16px",
                  "margin-right": "32px",
                  "border": "1px solid #f621215c",
                }}
                onClick={(e) => {
                  contentAnchor.setElement(e.target as HTMLElement);
                  batch(() => {
                    setSettingsContent(undefined);
                    setShow(true);
                  });
                }} />
          </div>
          <Switch>
            <Match when={!historyPager$()?.data?.length && initialLoadComplete && !isLoading}>
              <div style="display: flex; align-items: center; justify-content: center; height: 100%; width: 100%">
                <img src={no_videos_in_history} style="width: 307px; margin-top: 150px" />
              </div>
            </Match>
            <Match when={!historyPager$()?.data?.length && (!initialLoadComplete || isLoading)}>
              <VirtualList 
                items={new Array(30)}
                outerContainerRef={scrollContainerRef}
                itemHeight={110}
                builder={(index, item) => {
                  return (
                    <div class={styles.itemContainer}>
                      <SkeletonDiv />
                    </div>
                  );
                }
              } />
            </Match>
            <Match when={historyPager$()?.data?.length}>
              <VirtualList 
                items={historyPager$()?.data}
                addedItems={historyPager$()?.addedFilteredItemsEvent}
                modifiedItems={historyPager$()?.modifiedFilteredItemsEvent}
                removedItems={historyPager$()?.removedFilteredItemsEvent}
                outerContainerRef={scrollContainerRef}
                onEnd={onScrollEnd}
                itemHeight={110}
                builder={(index, item) => {
                  const historyVideo = createMemo(() => item() as IHistoryVideo | undefined);
                  const bestThumbnail = createMemo(() => (historyVideo()?.video?.thumbnails?.sources?.length ?? 0 > 0) ? historyVideo()!.video?.thumbnails.sources[Math.max(0, historyVideo()!.video.thumbnails.sources.length - 1)] : null);

                  const metadata = createMemo(() => {
                    const tokens = [];            
                    const viewCount = historyVideo()?.video?.viewCount;
                    if (viewCount && viewCount > 0) {
                        tokens.push(toHumanNumber(viewCount) + " views");
                    }
            
                    if (tokens.length < 1) {
                        return undefined;
                    }
            
                    const date = historyVideo()?.video.dateTime;
                    if (date) {
                        tokens.push(toHumanNowDiffStringMinDay(date));
                    }
            
                    return tokens.join(" • ");
                  });

                  const openVideo = () => {
                    const hv = historyVideo();
                    if (hv)
                      video?.actions.openVideo(hv.video, (hv.position > 10000) ? Duration.fromMillis(hv.position) : Duration.fromMillis(hv.position * 1000));
                  };

                  const openAuthor = () => {
                    
                  };

                  return (<div class={styles.itemContainer}>
                    <div style="height: 82px; width: 150px; position: relative; border-radius: 4.374px; overflow: hidden; cursor: pointer; flex-shrink: 0;" onClick={openVideo}>
                      <img src={bestThumbnail()?.url} style={{"height": "100%", "width": "100%", "object-fit": "cover"}} referrerPolicy='no-referrer' />
                      <div style={{
                        "position": "absolute",
                        "bottom": "0px",
                        "left": "0px",
                        "right": "0px",
                        "background-color": "#019BE7",
                        "height": "3px",
                        "width": historyVideo() ? `${(getVideoProgressPercentage(historyVideo()?.position, historyVideo()?.video?.duration))}%` : undefined
                      }} />
                    </div>
                    <div style="display: flex; flex-direction: column; height: 100%; margin-left: 20px; width: 100%;">
                      <div class={styles.videoTitle} onClick={openVideo}>{historyVideo()?.video.name}</div>
                      <div style="flex-grow: 1"></div>
                      <div style="display: flex; flex-direction: row; align-items: center;">
                        <img src={historyVideo()?.video.author.thumbnail} style={{"height": "26px", "width": "26px", "object-fit": "cover", "border-radius": "50%", "cursor": "pointer"}} onClick={openAuthor} referrerPolicy='no-referrer' />
                        <div style="display: flex; flex-direction: column; height: 100%; margin-left: 8px; gap: 1px;">
                          <div class={styles.authorTitle} onClick={openAuthor}>{historyVideo()?.video.author.name}</div>
                          <div class={styles.authorMetadata} onClick={openAuthor}>{metadata()}</div>
                        </div>
                      </div>
                    </div>
                    <div style="flex-grow: 1"></div>
                    <IconButton icon={ic_more}
                      style={{"flex-shrink": 0}}
                      ref={refMoreButton} 
                      onClick={(e) => {
                        contentAnchor.setElement(e.target as HTMLElement);
              
                        batch(() => {
                          setSettingsContent(historyVideo);
                          setShow(true);
                        });
                      }} 
                    />
                  </div>);
                }
              } />
            </Match>
          </Switch>
        </div>
      </ScrollContainer>
      <Portal>
          <SettingsMenu menu={settingsMenu$()} show={show$()} onHide={()=>setShow(false)} anchor={contentAnchor} />
      </Portal>
    </>
  );
};

export default HistoryPage;
