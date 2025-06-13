import { createEffect, createMemo, type Component } from 'solid-js';
import LoaderContainer from '../basics/loaders/LoaderContainer';
import NavigationBar from '../topbars/NavigationBar';
import styles from './index.module.css';
import iconSettings from '../../assets/icons/icon_32_settings.svg';
import iconPlay from '../../assets/icons/icon24_play.svg';
import iconShuffle from '../../assets/icons/icon_shuffle.svg';
import CustomButton from '../buttons/CustomButton';
import ScrollContainer from '../containers/ScrollContainer';

import { IPlatformVideo } from '../../backend/models/content/IPlatformVideo';
import PlaylistItemView from '../PlaylistItemView';
import { Pager } from '../../backend/models/pagers/Pager';
import VirtualList from '../containers/VirtualList';
import { IPlatformContent } from '../../backend/models/content/IPlatformContent';

interface RemotePlaylistDetailViewProps {
  type: string;
  name?: string;
  itemCount?: number;
  pager?: Pager<IPlatformContent>;
  isLoading: boolean;
  onInteract?: () => void;
}

const RemotePlaylistDetailView: Component<RemotePlaylistDetailViewProps> = (props) => {
  let isLoading = false;
  async function onScrollEnd() {
      if (!isLoading && props.pager?.hasMore) {
          isLoading = true;
          console.log("Fetching next page");
          await props.pager?.nextPage();
          isLoading = false;
      }
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
        <div style="display: flex; flex-direction: row; align-items: center; margin-left: 32px; margin-right: 32px; margin-top: 46px;">
          <div style="display: flex; flex-direction: column;">
            <div class={styles.header}>{props.name}</div>
            <div class={styles.metadata}>{props.itemCount ?? 0} {props.itemCount === 1 ? "item" : "items"}</div>
          </div>
          <div style="flex-grow: 1"></div>
            <img src={iconSettings} style="width: 24px; height: 100%; margin-left: 16px; margin-right: 16px; padding-left: 16px; padding-right: 16px; cursor: pointer;" onClick={(ev) => {
              props?.onInteract?.();
            }} />
            <CustomButton
            text="Play all"
            icon={iconPlay}
            style={{
              background: "linear-gradient(267deg, #01D6E6 -100.57%, #0182E7 90.96%)",
              "flex-shrink": 0
            }}
            onClick={() => props?.onInteract?.()} />
          <CustomButton
            text="Shuffle"
            icon={iconShuffle}
            style={{
              border: "1px solid var(--grey-color-6)",
              "margin-left": "16px",
              "margin-right": "16px",
              "flex-shrink": 0
            }}
            onClick={() => props?.onInteract?.()} />
        </div>
        <ScrollContainer ref={scrollContainerRef}>
          <VirtualList outerContainerRef={scrollContainerRef}
            items={props.pager?.data}
            itemHeight={107}
            addedItems={props.pager?.addedFilteredItemsEvent}
            modifiedItems={props.pager?.modifiedFilteredItemsEvent}
            removedItems={props.pager?.removedFilteredItemsEvent}
            onEnd={onScrollEnd}
            builder={(index, item) => {
              return (
                <PlaylistItemView item={item() as IPlatformVideo} 
                  onRemove={() => props?.onInteract?.()} 
                  onSettings={(el) => props?.onInteract?.()} 
                  onPlay={() => props?.onInteract?.()} />
              );
            }} />
        </ScrollContainer>
      </LoaderContainer>
    </div>
  );
};

export default RemotePlaylistDetailView;
