import { createResource, type Component, Switch, Match, createSignal, batch, createMemo, Show, onMount } from 'solid-js';
import NavigationBar from '../../components/topbars/NavigationBar';
import styles from './index.module.css';
import { WatchLaterBackend } from '../../backend/WatchLaterBackend';
import ScrollContainer from '../../components/containers/ScrollContainer';
import VirtualGrid from '../../components/containers/VirtualGrid';
import VideoThumbnailView from '../../components/content/VideoThumbnailView';
import chevron_right from '../../assets/icons/icon24_chevron_right.svg';
import icon_add from '../../assets/icons/icon24_add.svg';
import { PlaylistsBackend } from '../../backend/PlaylistsBackend';
import UIOverlay from '../../state/UIOverlay';
import PlaylistView from '../../components/content/PlaylistView';
import Anchor, { AnchorStyle } from '../../utility/Anchor';
import SettingsMenu, { Menu, MenuItemButton, MenuSeperator } from '../../components/menus/Overlays/SettingsMenu';
import { IPlaylist } from '../../backend/models/IPlaylist';
import { Portal } from 'solid-js/web';

import iconPlaylist from '../../assets/icons/icon_nav_playlists.svg';
import iconQueue from '../../assets/icons/icon_add_to_queue.svg';
import iconAddToPlaylist from '../../assets/icons/icon_add_to_playlist.svg';
import iconDownload from '../../assets/icons/icon24_download.svg';
import iconTrash from '../../assets/icons/icon_trash.svg';
import { useNavigate } from '@solidjs/router';
import ButtonFlex from '../../components/buttons/ButtonFlex';
import { useVideo } from '../../contexts/VideoProvider';
import { ContentType } from '../../backend/models/ContentType';
import { IPlatformVideo } from '../../backend/models/content/IPlatformVideo';
import EmptyContentView from '../../components/EmptyContentView';
import { Menus } from '../../Menus';
import StateWebsocket from '../../state/StateWebsocket';
import { createResourceDefault } from '../../utility';
import LoaderGrid from '../../components/basics/loaders/LoaderGrid';

const PlaylistsPage: Component = () => {
  let scrollContainerRef: HTMLDivElement | undefined;

  const navigate = useNavigate();
  const video = useVideo();
  const [playlists$, playlistsResource] = createResourceDefault(async () => [], async () => {
    return await PlaylistsBackend.getAll();
  });

  StateWebsocket.registerHandlerNew("PlaylistsChanged", (packet)=>{
    playlistsResource.refetch();
  }, "playlistsPage");

  onMount(() => {
    video?.actions?.refetchWatchLater();
  });

  const addMediaMenuItems = (content: IPlatformVideo) => {
    return [
      new MenuItemButton("Add to queue", iconQueue, undefined, () => {

      }),
      new MenuItemButton("Add to playlist", iconAddToPlaylist, undefined, async () => {
        await UIOverlay.overlayAddToPlaylist(content, () => playlistsResource.refetch());
      }),
      new MenuItemButton("Download", iconDownload, undefined, () => {

      }),
      new MenuSeperator(),
      new MenuItemButton("Remove", iconTrash, undefined, async () => {
        await WatchLaterBackend.remove(content.url);
        video?.actions?.refetchWatchLater();
      }),
    ];
  };

  const [settingsContent$, setSettingsContent] = createSignal<IPlatformVideo | IPlaylist>();
  const settingsMenu$ = createMemo(() => {
    const content = settingsContent$();

    const itemsArray = [
      ((content as any)?.contentType === ContentType.MEDIA ? addMediaMenuItems(content as IPlatformVideo) : []),
      !(content as any)?.contentType && (content as any)?.id ? Menus.getPlaylistItems((content as IPlaylist).id, () => {
        playlistsResource.refetch();
      }) : []
    ];

    const items = [];
    for (const array of itemsArray) {
      if (array.length > 0 && items.length > 0)
        items.push(new MenuSeperator());
      items.push(...array);
    }

    return {
      title: "",
      items
    } as Menu;
  });

  const [show$, setShow] = createSignal<boolean>(false);
  const contentAnchor = new Anchor(null, show$, AnchorStyle.BottomRight);
  function onSettingsClicked(element: HTMLElement, content: IPlatformVideo | IPlaylist) {
    contentAnchor.setElement(element);

    batch(() => {
      setSettingsContent(content);
      setShow(true);
    });
  }

  function onSettingsHidden() {
    batch(() => {
      setSettingsContent(undefined);
      setShow(false);
    });
  }

  function createPlaylist() {
    UIOverlay.overlayNewPlaylist(() => {
      playlistsResource.refetch();
    });
  }

  return (
    <div class={styles.container}>
      <NavigationBar isRoot={true} defaultSearchType={ContentType.PLAYLIST} />
        <ScrollContainer ref={scrollContainerRef}>
          <Show when={video?.watchLater()?.length ?? 0 > 0}>
            <div class={styles.containerWatchLater}>
              <div class={styles.containerWatchLaterHeader}>
                <div class={styles.textHeader} style={{"margin-left": "24px"}}>Watch Later</div>
                <div style="flex-grow: 1;"></div>
                <div class={styles.containerSeeAll} onClick={() => navigate("/web/watchLater")}>
                  <div>See all</div>
                  <img style="width: 14px; height: 14px; margin-left: 6px;" src={chevron_right} />
                </div>
              </div>
              <VirtualGrid outerContainerRef={scrollContainerRef}
                items={video?.watchLater()}
                calculateHeight={(width) => {
                  const aspectRatio = 16 / 9;
                  const thumbnailHeight = width / aspectRatio;
                  const margin1 = 16;
                  const fontSize = 18;
                  const textHeight = 2.4 * fontSize;
                  const margin2 = 16;
                  const dataHeight = 32;
                  const totalHeight = thumbnailHeight + margin1 + textHeight + margin2 + dataHeight;
                  return totalHeight;                        
                }}
                itemWidth={320}
                autosizeWidth={true}
                maximumRowsVisible={1}
                style={{
                  "margin-left": "15px",
                  "margin-top": "15px",
                  "margin-bottom": "10px"
                }}
                builder={(index, item) =>
                  <VideoThumbnailView video={item() as IPlatformVideo}
                    onClick={() => {
                      const queue = video?.watchLater();
                      if (!queue) {
                        return;
                      }

                      video?.actions?.setQueue(index()!, queue, false, false);
                    }}
                    onSettings={(e, content) => onSettingsClicked(e, content)} />
                } />
            </div>
          </Show>
          <div class={styles.containerPlaylists}>
            <div class={styles.containerPlaylistsHeader}>
              <div class={styles.textHeader}>Playlists</div>
              <div style="flex-grow: 1;"></div>

              <ButtonFlex text='New playlist'
                icon={icon_add}
                color='#019BE7'
                small={true}
                style={{
                  "margin-right": "48px"
                }}
                onClick={() =>createPlaylist()} />
            </div>
            
            <Show when={(playlists$()?.length ?? 0) > 0}>
              <VirtualGrid outerContainerRef={scrollContainerRef}
                items={playlists$()}
                itemWidth={320}
                calculateHeight={(width) => {
                  const aspectRatio = 320 / 250;
                  const thumbnailHeight = width / aspectRatio;
                  return thumbnailHeight;
                }}
                autosizeWidth={true}
                style={{
                  "margin-left": "24px",
                  "margin-top": "24px",
                  "margin-bottom": "24px"
                }}
                builder={(index, item) => {
                  const bestThumbnail$ = createMemo(()=>{
                    const playlist = item();
                    const video = (playlist?.videos?.length ?? 0 > 0) ? playlist?.videos[0] : null;
                    return (video?.thumbnails?.sources?.length ?? 0 > 0) ? video?.thumbnails.sources[Math.max(0, video.thumbnails.sources.length - 1)] : null;
                  });

                  return (
                    <PlaylistView name={item()?.name}
                      itemCount={item()?.videos.length}
                      thumbnail={bestThumbnail$()?.url}
                      onClick={() => {
                        const playlist = item();
                        if (item()) {
                          navigate("/web/playlist?id=" + playlist.id);
                        }
                      }}
                      onSettings={(e) => {
                        const playlist = item();
                        if (playlist) {
                          onSettingsClicked(e, playlist);
                        }
                      }} />
                  );
                }} />
            </Show>
            <Show when={(playlists$()?.length ?? 0) == 0 && !playlists$.loading}>
              <EmptyContentView 
                  icon={iconPlaylist}
                  title='You have no playlists'
                  description='Create or import playlists'
                  actions={[
                    {
                      icon: iconPlaylist,
                      title: "Import Playlists",
                      action: ()=>{UIOverlay.dismiss(); UIOverlay.overlayImportSelect()}
                    },
                    {
                      icon: icon_add,
                      title: "Create Playlist",
                      color: "#019BE7",
                      action: ()=>{createPlaylist()}
                    }
                  ]} />
            </Show>
            <Show when={(playlists$()?.length ?? 0) == 0 && playlists$.loading}>
              <LoaderGrid />
            </Show>
          </div>
        </ScrollContainer>
      
      <Portal>
        <SettingsMenu menu={settingsMenu$()} show={show$()} onHide={() => onSettingsHidden()} anchor={contentAnchor} />
      </Portal>
    </div>
  );
};

export default PlaylistsPage;