import { type Component, createMemo, createSignal, createEffect, batch, Show } from 'solid-js';
import LoaderContainer from '../basics/loaders/LoaderContainer';
import NavigationBar from '../topbars/NavigationBar';
import styles from './index.module.css';
import iconSettings from '../../assets/icons/icon_32_settings.svg';
import iconPlay from '../../assets/icons/icon24_play.svg';
import iconShuffle from '../../assets/icons/icon_shuffle.svg';
import CustomButton from '../buttons/CustomButton';
import ScrollContainer from '../containers/ScrollContainer';
import { swap } from '../../utility';
import VirtualDragDropList, { DragSession } from '../containers/VirtualDragDropList';
import { Portal } from 'solid-js/web';
import SettingsMenu, { Menu, MenuItem, MenuItemButton, MenuItemToggle, MenuSeperator } from '../menus/Overlays/SettingsMenu';
import Anchor, { AnchorStyle } from '../../utility/Anchor';

import iconQueue from '../../assets/icons/icon_add_to_queue.svg';
import iconAddToPlaylist from '../../assets/icons/icon_add_to_playlist.svg';
import iconDownload from '../../assets/icons/icon24_download.svg';
import iconTrash from '../../assets/icons/icon_trash.svg';
import UIOverlay from '../../state/UIOverlay';
import { IPlatformVideo } from '../../backend/models/content/IPlatformVideo';
import PlaylistItemView from '../PlaylistItemView';
import { Menus } from '../../Menus';
import { useNavigate } from '@solidjs/router';
import InputText from '../basics/inputs/InputText';
import Dropdown from '../basics/inputs/Dropdown';
import ic_search from '../../assets/icons/search.svg';
import { focusable } from '../../focusable'; void focusable;
import { InputSource } from '../../nav';
import { VideoState } from '../../contexts/VideoProvider';
import IconButton from '../buttons/IconButton';
import { SettingsBackend } from '../../backend/SettingsBackend';
import { WatchLaterBackend } from '../../backend/WatchLaterBackend';
import StateWebsocket from '../../state/StateWebsocket';

export enum PlaylistSortOrder {
  DateAddedNewest = 0,
  DateAddedOldest = 1,
  AlphabeticalAZ = 2,
  AlphabeticalZA = 3,
  Custom = 4,
  CustomReversed = 5
}

export enum PlaylistWatchFilter {
  All = 0,
  Unwatched = 1,
  Watched = 2
}

const SORT_OPTIONS = [
  "Date Added (Newest)",
  "Date Added (Oldest)",
  "Name (A-Z)",
  "Name (Z-A)",
  "Custom",
  "Custom (Reversed)"
];

const WATCH_FILTER_OPTIONS = [
  "All",
  "Not watched",
  "Watched"
];

export const isVideoWatched = (video: IPlatformVideo): boolean => {
  if (video.metadata?.watched !== undefined) {
    return Boolean(video.metadata.watched);
  }
  const pos = video.metadata?.position ?? 0;
  if (video.duration > 0) {
    return pos > video.duration * 0.7;
  }
  return pos >= 30;
};

interface PlaylistDetailViewProps {
  type: string;
  name?: string;
  videos?: IPlatformVideo[];
  videoAdds?: Record<string, number>;
  isLoading: boolean;
  id?: string;
  showWatchFilter?: boolean;
  onPlayAll: (videoState?: VideoState, sortedVideos?: IPlatformVideo[]) => void;
  onShuffleAll: (videoState?: VideoState, sortedVideos?: IPlatformVideo[]) => void;
  onDragEnd: () => void;
  onRemove: (video: IPlatformVideo) => void;
  onPlay: (video: IPlatformVideo, videoState?: VideoState, sortedVideos?: IPlatformVideo[]) => void;
  onAddToQueue: (video: IPlatformVideo) => void;
  onDownload?: () => void;
  refetch?: () => void;
}

const PlaylistDetailView: Component<PlaylistDetailViewProps> = (props) => {
  const navigate = useNavigate();

  const isWatchLater$ = createMemo(() => !props.id && props.type === "Watch Later");
  const [autoPurgeWatched$, setAutoPurgeWatched] = createSignal(false);

  createEffect(() => {
    if (isWatchLater$()) {
      WatchLaterBackend.getAutoPurgeWatched()
        .then((val) => setAutoPurgeWatched(Boolean(val)))
        .catch(() => {});
    }
  });

  StateWebsocket.registerHandlerNew("SettingsChanged", (packet) => {
    if (isWatchLater$()) {
      WatchLaterBackend.getAutoPurgeWatched()
        .then((val) => setAutoPurgeWatched(Boolean(val)))
        .catch(() => {});
    }
  }, "playlistDetailView");

  const purgeWatchedVideos = () => {
    UIOverlay.overlayConfirm({
      yes: async () => {
        const removedCount = await WatchLaterBackend.purgeWatched();
        UIOverlay.toast(`Removed ${removedCount} watched ${removedCount === 1 ? "video" : "videos"}`);
        props.refetch?.();
      }
    }, "Are you sure you want to remove all watched videos from Watch Later?");
  };

  const [settingsContent$, setSettingsContent] = createSignal<IPlatformVideo>();
  const [settingsInputSource$, setSettingsInputSource] = createSignal<InputSource>();
  const settingsMenu$ = createMemo(() => {
    const content = settingsContent$();
    if (!content) {
      const id = props.id;
      if (id) {
        return {
          title: "",
          items: Menus.getPlaylistItems(id, () => {
            navigate("/web/playlists");
          })
        } as Menu;
      }

      if (isWatchLater$()) {
        const items: MenuItem[] = [
          new MenuItemToggle({
            name: "Auto-purge watched videos",
            description: "Automatically remove videos from Watch Later once watched",
            isSelected: autoPurgeWatched$(),
            onToggle: async (val) => {
              setAutoPurgeWatched(val);
              await WatchLaterBackend.setAutoPurgeWatched(val);
              props.refetch?.();
            }
          }),
          new MenuSeperator(),
          new MenuItemButton("Purge watched videos now", iconTrash, undefined, () => {
            purgeWatchedVideos();
          })
        ];
        return {
          title: "",
          items
        } as Menu;
      }

      return {
        title: "",
        items: []
      } as Menu;
    }

    return {
      title: "",
      items: [
        new MenuItemButton("Add to queue", iconQueue, undefined, () => props.onAddToQueue(content)),
        new MenuItemButton("Add to playlist", iconAddToPlaylist, undefined, async () => {
          await UIOverlay.overlayAddToPlaylist(content, () => props.refetch?.());
        }),
        new MenuItemButton("Download", iconDownload, undefined, () => UIOverlay.overlayDownload(content.url)),
        ... (canRemove$() ? [ 
          new MenuSeperator(),
          new MenuItemButton("Remove", iconTrash, undefined, () => props.onRemove(content)) 
        ] : [])
      ]
    } as Menu;
  });

  const [show$, setShow] = createSignal<boolean>(false);
  const contentAnchor = new Anchor(null, show$, AnchorStyle.BottomRight);
  function onSettingsClicked(element: HTMLElement, inputSource: InputSource, content?: IPlatformVideo) {
    contentAnchor.setElement(element);

    batch(() => {
      setSettingsContent(content);
      setSettingsInputSource(inputSource);
      setShow(true);
    });
  }

  function onSettingsHidden() {
    batch(() => {
      setSettingsContent(undefined);
      setShow(false);
    });
  }

  const [filterText$, setFilterText] = createSignal("");
  const [sortBy$, setSortByInternal] = createSignal<PlaylistSortOrder>(PlaylistSortOrder.DateAddedNewest);
  const [watchFilter$, setWatchFilterInternal] = createSignal<PlaylistWatchFilter>(PlaylistWatchFilter.All);

  const persistKey$ = createMemo(() => props.id ? `playlist_sort_${props.id}` : `playlist_sort_watch_later`);
  const watchFilterPersistKey$ = createMemo(() => props.id ? `playlist_watch_filter_${props.id}` : `playlist_watch_filter_watch_later`);

  createEffect(() => {
    const key = persistKey$();
    SettingsBackend.persistGet(key, PlaylistSortOrder.DateAddedNewest)
      .then((val: number) => {
        if (typeof val === "number" && val >= 0 && val < SORT_OPTIONS.length) {
          setSortByInternal(val as PlaylistSortOrder);
        } else {
          setSortByInternal(PlaylistSortOrder.DateAddedNewest);
        }
      })
      .catch(() => {
        setSortByInternal(PlaylistSortOrder.DateAddedNewest);
      });

    if (props.showWatchFilter || isWatchLater$()) {
      const wfKey = watchFilterPersistKey$();
      SettingsBackend.persistGet(wfKey, PlaylistWatchFilter.All)
        .then((val: number) => {
          if (typeof val === "number" && val >= 0 && val < WATCH_FILTER_OPTIONS.length) {
            setWatchFilterInternal(val as PlaylistWatchFilter);
          } else {
            setWatchFilterInternal(PlaylistWatchFilter.All);
          }
        })
        .catch(() => {
          setWatchFilterInternal(PlaylistWatchFilter.All);
        });
    }
  });

  const setSortBy = (value: PlaylistSortOrder) => {
    setSortByInternal(value);
    SettingsBackend.persistSet(persistKey$(), value);
  };

  const setWatchFilter = (value: PlaylistWatchFilter) => {
    setWatchFilterInternal(value);
    SettingsBackend.persistSet(watchFilterPersistKey$(), value);
  };

  const canRemove$ = createMemo(() => true);
  const isEditable$ = createMemo(() =>
    (filterText$()?.length ?? 0) === 0 &&
    (!(props.showWatchFilter || isWatchLater$()) || watchFilter$() === PlaylistWatchFilter.All) &&
    (sortBy$() === PlaylistSortOrder.Custom || sortBy$() === PlaylistSortOrder.CustomReversed)
  );

  const filteredVideos$ = createMemo(() => {
    const source = props.videos;
    if (!source) return undefined;

    const indexed = source.map((video, index) => ({
      video,
      index,
      addedTime: props.videoAdds?.[video.url] ?? 0
    }));

    const filter = filterText$()?.toLowerCase() ?? "";
    let filtered = filter.length > 0
      ? indexed.filter(item => item.video.name.toLowerCase().indexOf(filter) !== -1)
      : indexed;

    if (props.showWatchFilter || isWatchLater$()) {
      const wf = watchFilter$();
      if (wf === PlaylistWatchFilter.Unwatched) {
        filtered = filtered.filter(item => !isVideoWatched(item.video));
      } else if (wf === PlaylistWatchFilter.Watched) {
        filtered = filtered.filter(item => isVideoWatched(item.video));
      }
    }

    switch (sortBy$()) {
      case PlaylistSortOrder.DateAddedNewest:
        filtered.sort((a, b) => {
          if (a.addedTime !== b.addedTime) {
            return b.addedTime - a.addedTime;
          }
          return b.index - a.index;
        });
        break;
      case PlaylistSortOrder.DateAddedOldest:
        filtered.sort((a, b) => {
          if (a.addedTime !== b.addedTime) {
            return a.addedTime - b.addedTime;
          }
          return a.index - b.index;
        });
        break;
      case PlaylistSortOrder.AlphabeticalAZ:
        filtered.sort((a, b) => a.video.name.localeCompare(b.video.name) || (a.index - b.index));
        break;
      case PlaylistSortOrder.AlphabeticalZA:
        filtered.sort((a, b) => b.video.name.localeCompare(a.video.name) || (a.index - b.index));
        break;
      case PlaylistSortOrder.Custom:
        // Keep underlying custom order (index ascending)
        break;
      case PlaylistSortOrder.CustomReversed:
        filtered.reverse();
        break;
    }

    return filtered.map(item => item.video);
  });

  let dragSession: DragSession | undefined;
  let scrollContainerRef: HTMLDivElement | undefined;
  return (
    <div class={styles.container}>
      <NavigationBar />
      <LoaderContainer isLoading={props.isLoading} loadingText={`Loading ${props.type}`} style={{
        "flex-grow": 1,
        "display": "flex",
        "flex-direction": "column",
        "overflow": "hidden",
        "height": 'unset'
      }}>
        <div style="display: flex; flex-direction: row; align-items: center; margin-left: 32px; margin-top: 46px;">
          <div style="display: flex; flex-direction: column;">
            <div class={styles.header}>{props.name}</div>
            <div class={styles.metadata}>{props.videos?.length} {props.videos?.length === 1 ? "item" : "items"}</div>
          </div>
          <div style="flex-grow: 1"></div>
        </div>
        <ScrollContainer ref={scrollContainerRef}>
          <div class={styles.containerFilters}>
            <InputText icon={ic_search} placeholder={"Search playlists"}
              value={filterText$()}
              showClearButton={true}
              inputContainerStyle={{
                "height": "48px", 
                "background": "#141414"
              }}
              onTextChanged={(v) => {
                setFilterText(v);
              }}
              focusable={true} />
            <Show when={props.showWatchFilter || isWatchLater$()}>
              <CustomButton
                text="Purge watched"
                icon={iconTrash}
                style={{
                  border: "1px solid #f621215c",
                  "flex-shrink": 0
                }}
                onClick={() => purgeWatchedVideos()}
                focusableOpts={{
                  onPress: () => purgeWatchedVideos()
                }} />
              <Dropdown
                label="Filter"
                onSelectedChanged={(v) => setWatchFilter(v as PlaylistWatchFilter)}
                value={watchFilter$()}
                options={WATCH_FILTER_OPTIONS}
                anchorStyle={AnchorStyle.BottomLeft}
                style={{ "width": "180px", "flex-shrink": 0 }}
              />
            </Show>
            <Dropdown
              label="Sort by"
              onSelectedChanged={(v) => setSortBy(v as PlaylistSortOrder)}
              value={sortBy$()}
              options={SORT_OPTIONS}
              anchorStyle={AnchorStyle.BottomLeft}
              style={{ "width": "240px", "flex-shrink": 0 }}
            />
          <CustomButton
            text="Play all"
            icon={iconPlay}
            style={{
              background: "linear-gradient(267deg, #01D6E6 -100.57%, #0182E7 90.96%)",
              "flex-shrink": 0
            }}
            onClick={() => props.onPlayAll(undefined, filteredVideos$())}
            focusableOpts={{
              onPress: () => props.onPlayAll(VideoState.Fullscreen, filteredVideos$())
            }} />
          <CustomButton
            text="Shuffle"
            icon={iconShuffle}
            style={{
              border: "1px solid #2E2E2E",
              "flex-shrink": 0
            }}
            onClick={() => props.onShuffleAll(undefined, filteredVideos$())}
            focusableOpts={{
              onPress: () => props.onShuffleAll(VideoState.Fullscreen, filteredVideos$())
            }} />
            <Show when={props.id || isWatchLater$()}>
              <IconButton
                icon={iconSettings}
                variant="none"
                width="24px"
                height="100%"
                iconInset="0px"
                onClick={(ev) => {
                  onSettingsClicked(ev.currentTarget as HTMLElement, "pointer", undefined);
                }}
                focusableOpts={{
                  onPress: (el, inputSource) => onSettingsClicked(el, inputSource, undefined),
                }}
              />
            </Show>
          </div>

          <VirtualDragDropList outerContainerRef={scrollContainerRef}
            items={filteredVideos$()}
            itemHeight={107}
            onSwap={(index1, index2) => {
              const videos = props.videos;
              if (!videos || !isEditable$()) {
                return;
              }

              if (sortBy$() === PlaylistSortOrder.CustomReversed) {
                const realIdx1 = videos.length - 1 - index1;
                const realIdx2 = videos.length - 1 - index2;
                swap(videos, realIdx1, realIdx2);
              } else {
                swap(videos, index1, index2);
              }
            }}
            onDragEnd={() => props.onDragEnd()}
            builder={(index, item, containerRef, dragControls) => {
              const video = createMemo(() => item() as IPlatformVideo | undefined);
              return (
                <PlaylistItemView item={video()} 
                  isEditable={isEditable$()}
                  isRemovable={canRemove$()}
                  onRemove={() => {
                    const v = video();
                    if (!v) return;
                    props.onRemove(v);
                  }} 
                  onSettings={(el) => {
                    const v = video();
                    if (!v) return;
                    onSettingsClicked(el, "pointer", v);
                  }} 
                  onDragStart={(e, el) => {
                    if (!isEditable$()) return;
                    dragControls.startPointerDrag?.(e.pageY, containerRef!.getBoundingClientRect().top, el);
                    e.preventDefault();
                    e.stopPropagation();
                  }} 
                  onPlay={() => {
                    const v = video();
                    if (!v) return;
                    props.onPlay(v, undefined, filteredVideos$());
                  }}
                  focusableOpts={{
                    groupId: 'playlist',
                    groupType: 'vertical',
                    groupIndices: [index()],
                    groupEscapeDirs: ['up', 'left'],
                    onPress: () => {
                      const v = video();
                      if (!v) return;
                      props.onPlay(v, VideoState.Fullscreen, filteredVideos$());
                    },
                    onOptions: (el, inputSource) => {
                      const v = video();
                      if (!v) return;
                      onSettingsClicked(el, inputSource, v);
                    },
                    onAction: () => {
                      if (!isEditable$()) return;
                      if (!dragSession) {
                        dragSession = dragControls.startProgrammaticDrag();
                      } else {
                        dragSession.end();
                        dragSession = undefined;
                      }
                    },
                    onActionLabel: isEditable$() ? "Reorder" : undefined,
                    onDirection: (el, dir) => {
                      if (!dragSession || !dragSession.isActive()) return false;
                      if (dir === "up") {
                        dragSession.moveBy(-1);
                        return true;
                      }
                      else if (dir === "down") {
                        dragSession.moveBy(1);
                        return true;
                      }
                    }
                  }} />
              );
            }} />
        </ScrollContainer>
      </LoaderContainer>
      <Portal>
        <SettingsMenu menu={settingsMenu$()} show={show$()} onHide={() => onSettingsHidden()} anchor={contentAnchor} inputSource={settingsInputSource$()} />
      </Portal>
    </div>
  );
};

export default PlaylistDetailView;
