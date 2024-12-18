import { type Component, createMemo, createSignal, batch, Show } from 'solid-js';
import LoaderContainer from '../basics/loaders/LoaderContainer';
import NavigationBar from '../topbars/NavigationBar';
import styles from './index.module.css';
import iconSettings from '../../assets/icons/icon_32_settings.svg';
import iconPlay from '../../assets/icons/icon24_play.svg';
import iconShuffle from '../../assets/icons/icon_shuffle.svg';
import CustomButton from '../buttons/CustomButton';
import ScrollContainer from '../containers/ScrollContainer';
import { swap } from '../../utility';
import VirtualDragDropList from '../containers/VirtualDragDropList';
import { Portal } from 'solid-js/web';
import SettingsMenu, { Menu, MenuItemButton, MenuSeperator } from '../menus/Overlays/SettingsMenu';
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

interface PlaylistDetailViewProps {
  type: string;
  name?: string;
  videos?: IPlatformVideo[];
  isLoading: boolean;
  id?: string;
  onPlayAll: () => void;
  onShuffleAll: () => void;
  onDragEnd: () => void;
  onRemove: (video: IPlatformVideo) => void;
  onDownload: (video: IPlatformVideo) => void;
  onPlay: (video: IPlatformVideo) => void;
  onAddToQueue: (video: IPlatformVideo) => void;
  refetch?: () => void;
}

const PlaylistDetailView: Component<PlaylistDetailViewProps> = (props) => {
  const navigate = useNavigate();

  const [settingsContent$, setSettingsContent] = createSignal<IPlatformVideo>();
  const settingsMenu$ = createMemo(() => {
    const content = settingsContent$();
    if (!content) {
      const id = props.id;
      return id ? {
        title: "",
        items: Menus.getPlaylistItems(id, () => {
          navigate("/web/playlists");
        })
      } : {
        title: "",
        items: []
      };
    }

    return {
      title: "",
      items: [
        new MenuItemButton("Add to queue", iconQueue, undefined, () => props.onAddToQueue(content)),
        new MenuItemButton("Add to playlist", iconAddToPlaylist, undefined, async () => {
          await UIOverlay.overlayAddToPlaylist(content, () => props.refetch?.());
        }),
        new MenuItemButton("Download", iconDownload, undefined, () => props.onDownload(content)),
        new MenuSeperator(),
        new MenuItemButton("Remove", iconTrash, undefined, () => props.onRemove(content))
      ]
    } as Menu;
  });

  const [show$, setShow] = createSignal<boolean>(false);
  const contentAnchor = new Anchor(null, show$, AnchorStyle.BottomRight);
  function onSettingsClicked(element: HTMLElement, content?: IPlatformVideo) {
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
          <Show when={props.id}>
            <img src={iconSettings} style="width: 24px; height: 100%; margin-left: 16px; margin-right: 16px; padding-left: 16px; padding-right: 16px; cursor: pointer;" onClick={(ev) => {
              onSettingsClicked(ev.target as HTMLElement, undefined);
            }} />
          </Show>
          <CustomButton
            text="Play all"
            icon={iconPlay}
            style={{
              background: "linear-gradient(267deg, #01D6E6 -100.57%, #0182E7 90.96%)",
              "flex-shrink": 0
            }}
            onClick={() => props.onPlayAll()} />
          <CustomButton
            text="Shuffle"
            icon={iconShuffle}
            style={{
              border: "1px solid #2E2E2E",
              "margin-left": "16px",
              "margin-right": "16px",
              "flex-shrink": 0
            }}
            onClick={() => props.onShuffleAll()} />
        </div>
        <ScrollContainer ref={scrollContainerRef}>
          <VirtualDragDropList outerContainerRef={scrollContainerRef}
            items={props.videos}
            itemHeight={107}
            onSwap={(index1, index2) => {
              const videos = props.videos;
              if (!videos) {
                return;
              }

              swap(videos, index1, index2)
            }}
            onDragEnd={() => props.onDragEnd()}
            builder={(index, item, containerRef, startDrag) => {
              const video = createMemo(() => item() as IPlatformVideo | undefined);
              return (
                <PlaylistItemView item={video()} 
                  onRemove={() => {
                    const v = video();
                    if (!v) return;
                    props.onRemove(v);
                  }} 
                  onSettings={(el) => {
                    const v = video();
                    if (!v) return;
                    onSettingsClicked(el, v);
                  }} 
                  onDragStart={(e, el) => {
                    startDrag?.(e.pageY, containerRef!.getBoundingClientRect().top, el);
                    e.preventDefault();
                    e.stopPropagation();
                  }} 
                  onPlay={() => {
                    const v = video();
                    if (!v) return;
                    props.onPlay(v);
                  }} />
              );
            }} />
        </ScrollContainer>
      </LoaderContainer>
      <Portal>
        <SettingsMenu menu={settingsMenu$()} show={show$()} onHide={() => onSettingsHidden()} anchor={contentAnchor} />
      </Portal>
    </div>
  );
};

export default PlaylistDetailView;
