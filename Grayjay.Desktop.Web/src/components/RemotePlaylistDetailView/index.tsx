import { Show, createEffect, createMemo, type Component } from 'solid-js';
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
import { focusable } from '../../focusable'; void focusable;
import { IPlatformContent } from '../../backend/models/content/IPlatformContent';
import IconButton from '../buttons/IconButton';

export type RemotePlaylistAction = "playAll" | "shuffle" | { url: string, index?: number };

interface RemotePlaylistDetailViewProps {
  type: string;
  name?: string;
  itemCount?: number;
  pager?: Pager<IPlatformContent>;
  isLoading: boolean;
  onInteract?: (action?: RemotePlaylistAction) => void;
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
      <NavigationBar groupEscapeTo={{
        down: ['actions']
      }} />
      <LoaderContainer isLoading={props.isLoading} loadingText={`Loading ${props.type}`} style={{
        "flex-grow": 1,
        "display": "flex",
        "flex-direction": "column",
        "overflow": "hidden",
        "height": 'unset'
      }}>
        <div style="display: flex; flex-direction: row; align-items: center; margin-left: 32px; margin-right: 32px; margin-top: 46px; margin-bottom: 16px; gap: 16px;">
          <div style="display: flex; flex-direction: column;">
            <div class={styles.header}>{props.name}</div>
            <Show when={(props.itemCount ?? 0) >= 0}>
              <div class={styles.metadata}>{props.itemCount ?? 0} {props.itemCount === 1 ? "item" : "items"}</div>
            </Show>
          </div>
          <div style="flex-grow: 1"></div>
            <CustomButton
            text="Play all"
            icon={iconPlay}
            style={{
              background: "linear-gradient(267deg, #01D6E6 -100.57%, #0182E7 90.96%)",
              "flex-shrink": 0
            }}
            onClick={() => props?.onInteract?.("playAll")}
            focusableOpts={{
              groupId: 'actions',
              groupType: 'horizontal',
              groupIndices: [1],
              onPress: () => props?.onInteract?.("playAll")
            }} />
          <CustomButton
            text="Shuffle"
            icon={iconShuffle}
            style={{
              border: "1px solid #2E2E2E",
              "flex-shrink": 0
            }}
            onClick={() => props?.onInteract?.("shuffle")}
            focusableOpts={{
              groupId: 'actions',
              groupType: 'horizontal',
              groupIndices: [2],
              onPress: () => props?.onInteract?.("shuffle")
            }} />
          <IconButton
            icon={iconSettings}
            variant="none"
            width="24px"
            height="24px"
            iconInset="0px"
            style={{"flex-shrink": 0}}
            onClick={() => {
              props?.onInteract?.();
            }}
            focusableOpts={{
              groupId: 'actions',
              groupType: 'horizontal',
              groupIndices: [0],
              onPress: () => props?.onInteract?.(),
            }}
          />
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
                  onPlay={() => props?.onInteract?.({ url: (item() as IPlatformVideo).url, index: index() })}
                  focusableOpts={{
                    groupId: 'playlist',
                    groupType: 'vertical',
                    groupIndices: [index()],
                    groupEscapeTo: {
                      up: ['actions']
                    },
                    onPress: () => props?.onInteract?.({ url: (item() as IPlatformVideo).url, index: index() })
                  }} />
              );
            }} />
        </ScrollContainer>
      </LoaderContainer>
    </div>
  );
};

export default RemotePlaylistDetailView;
