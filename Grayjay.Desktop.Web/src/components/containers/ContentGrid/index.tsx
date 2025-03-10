import { Component, createSignal, createMemo, Show, Switch, Match, batch, createEffect, onCleanup, onMount } from "solid-js";
import styles from './index.module.css';
import { Pager } from "../../../backend/models/pagers/Pager";
import VideoThumbnailView from "../../content/VideoThumbnailView";
import VirtualGrid from "../VirtualGrid";
import { useVideo } from "../../../contexts/VideoProvider";
import PlaceholderThumbnailView from "../../content/PlaceholderThumbnailView";
import SettingsMenu, { Menu, MenuSeperator, MenuItemButton } from "../../menus/Overlays/SettingsMenu";

import iconQueue from '../../../assets/icons/icon_add_to_queue.svg';
import iconPlaylist from '../../../assets/icons/icon_nav_playlists.svg';
import iconWatchLater from '../../../assets/icons/icon24_watch_later.svg';
import iconAddToPlaylist from '../../../assets/icons/icon24_add_to_playlist.svg';
import iconHide from '../../../assets/icons/icon24_hide.svg';
import iconDownload from '../../../assets/icons/icon24_download.svg';
import Anchor, { AnchorStyle } from "../../../utility/Anchor";
import { Portal } from "solid-js/web";
import { WatchLaterBackend } from "../../../backend/WatchLaterBackend";
import UIOverlay from "../../../state/UIOverlay";
import { ContentType } from "../../../backend/models/ContentType";
import { IPlatformVideo } from "../../../backend/models/content/IPlatformVideo";
import { IPlatformContentPlaceholder } from "../../../backend/models/content/IPlatformContentPlaceholder";
import { IPlatformContent } from "../../../backend/models/content/IPlatformContent";
import PlaylistView from "../../content/PlaylistView";
import { IPlatformPlaylist } from "../../../backend/models/content/IPlatformPlaylist";
import { useNavigate } from "@solidjs/router";
import CreatorView from "../../content/CreatorView";
import { IPlatformAuthorLink } from "../../../backend/models/IPlatformAuthorLink";
import StateGlobal from "../../../state/StateGlobal";
import { toHumanNumber } from "../../../utility";
import PostThumbnailView from "../../content/PostThumbnailView";
import { IPlatformPost } from "../../../backend/models/content/IPlatformPost";
import NestedMediaThumbnailView from "../../content/NestedMediaThumbnailView";
import { IPlatformNestedMedia } from "../../../backend/models/content/IPlatformNestedMedia";
import Globals from "../../../globals";
import LockedContentThumbnailView from "../../content/LockedContentThumbnailView";
import { IPlatformLockedContent } from "../../../backend/models/content/IPlatformLockedContent";
import { LocalBackend } from "../../../backend/LocalBackend";
import { Event0, Event1 } from "../../../utility/Event";

export interface ContentGridProps {
    pager: Pager<IPlatformContent> | undefined;
    outerContainerRef: HTMLDivElement | undefined;
    useCache?: boolean;
};

const ContentGrid: Component<ContentGridProps> = (props) => {
    const video = useVideo();
    const navigate = useNavigate();

    let isLoading = false;
    let loadNextPageWhenFinishedLoading = false;
    let dataFiltered0Counter = 0;
    async function onScrollEnd() {
        if (props.pager?.hasMore) {
            if (isLoading) {
                loadNextPageWhenFinishedLoading = true;
            } else {
                loadNextPageWhenFinishedLoading = false;

                if (dataFiltered0Counter < 3) {
                    isLoading = true;
                    console.log("Fetching next page");
                    await props.pager?.nextPage();
                    isLoading = false;

                    if (loadNextPageWhenFinishedLoading)
                        onScrollEnd();
                }
            }
        }
    }

    const [settingsContent$, setSettingsContent] = createSignal<IPlatformContent>();
    const settingsMenu$ = createMemo(() => {
        const content = settingsContent$();        
        return {
            title: "",
            items: [
            
                ... (content?.contentType === ContentType.MEDIA ? [ 
                    new MenuItemButton("Add to queue", iconQueue, undefined, ()=>{
                        video?.actions.addToQueue(content as IPlatformVideo);
                    }),
                    /*
                    new MenuItemButton("Play feed as queue", iconPlaylist, undefined, ()=>{

                    }),
                    new MenuSeperator(),*/
                    new MenuItemButton("Watch later", iconWatchLater, undefined, async () => {
                        await WatchLaterBackend.add(content as IPlatformVideo);
                        await video?.actions?.refetchWatchLater();
                    }),
                    new MenuItemButton("Add to playlist", iconAddToPlaylist, undefined, async () => {
                        await UIOverlay.overlayAddToPlaylist(content as IPlatformVideo);
                    }),
                    new MenuItemButton("Download video", iconDownload, undefined, ()=>{
                        UIOverlay.overlayDownload(content.url);
                    }),
                ] : []),
                /*
                new MenuSeperator(),
                new MenuItemButton("Hide creator from feed", iconHide, undefined, ()=>{

                }),*/
            ]
        } as Menu;
    });
    const [show$, setShow] = createSignal<boolean>(false);
    const contentAnchor = new Anchor(null, show$, AnchorStyle.BottomRight);
    function onSettingsClicked(element: HTMLElement, content: IPlatformContent) {
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

    createEffect(() => {
        console.log("content changed", { itemCount: props.pager?.dataFiltered.length, dataFiltered: props.pager?.dataFiltered });
    });

    let lastNoFilteredItems: Event0 | undefined;
    const attachNoFilteredItems = (noFilteredItems: Event0 | undefined) => {
        lastNoFilteredItems?.unregister(this);
        noFilteredItems?.registerOne(this, () => {
            dataFiltered0Counter++;
        });
        lastNoFilteredItems = noFilteredItems;
    };

    let lastAddedItems: Event1<{ startIndex: number; endIndex: number }> | undefined;
    const attachAddedItems = (addedItems: Event1<{ startIndex: number; endIndex: number }> | undefined) => {
        lastAddedItems?.unregister(this);
        addedItems?.registerOne(this, (_) => {
            dataFiltered0Counter = 0;
        });
        lastAddedItems = addedItems;
    };

    let lastFilterChangedEvent: Event0 | undefined;
    const attachFilterChanged = (filterChangedEvent: Event0 | undefined) => {
        lastFilterChangedEvent?.unregister(this);
        props.pager?.filterChangedEvent?.register(() => {
            dataFiltered0Counter = 0;
        }, this);
        lastFilterChangedEvent = filterChangedEvent;
    };
    
    createEffect(() => attachAddedItems(props.pager?.addedFilteredItemsEvent));
    createEffect(() => attachNoFilteredItems(props.pager?.noFilteredItemsEvent));
    createEffect(() => attachFilterChanged(props.pager?.filterChangedEvent));

    onCleanup(() => {
        props.pager?.addedFilteredItemsEvent?.unregister(this);
        props.pager?.noFilteredItemsEvent?.unregister(this);
        props.pager?.filterChangedEvent?.unregister(this);
        lastAddedItems?.unregister(this);
        lastNoFilteredItems?.unregister(this);
        lastFilterChangedEvent?.unregister(this);
    });

    const renderCreator = (creator: IPlatformAuthorLink) => {
        return (
            <CreatorView id={creator.id}
                name={creator.name}
                onClick={() => navigate("/web/channel?url=" + encodeURIComponent(creator.url), { state: { author: creator
                 } })}
                thumbnail={creator.thumbnail}
                metadata={((creator.subscribers && creator.subscribers > 0) ? (toHumanNumber(creator.subscribers) + " subscribers") : "")}
                url={creator.url} />
        );
    };

    const renderPlaylist = (playlist: IPlatformPlaylist) => {
        const pluginIconUrl$ = createMemo(() => {
            const plugin = StateGlobal.getSourceConfig(playlist?.id?.pluginID);
            return plugin?.absoluteIconUrl;
        });
        
        return (
            <PlaylistView itemCount={playlist.videoCount}
                name={playlist.name}
                thumbnail={playlist.thumbnail}
                platformIconUrl={pluginIconUrl$()}
                onClick={() => navigate("/web/remotePlaylist?url=" + encodeURIComponent(playlist.url))}
                onSettings={(e) => onSettingsClicked(e, playlist)} />
        );
    };
    
    let containerRef: HTMLDivElement | undefined;
    return (
        <div class={styles.container} ref={containerRef}>
            <Show when={props.pager}>
                <VirtualGrid outerContainerRef={props.outerContainerRef}
                    items={props.pager?.dataFiltered}
                    addedItems={props.pager?.addedFilteredItemsEvent}
                    modifiedItems={props.pager?.modifiedFilteredItemsEvent}
                    removedItems={props.pager?.removedFilteredItemsEvent}
                    itemWidth={320}
                    overscan={2}
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
                    autosizeWidth={true}
                    notifyEndOnLast={5}
                    onEnd={onScrollEnd}
                    style={{
                        /*"margin-left": "15px",*/
                        /*"margin-top": "15px",*/
                        "margin-bottom": "10px"
                    }}
                    elementStyle={{
                        "margin-left": "0px"
                    }}
                    builder={(index, item) =>
                        <Switch>
                            <Match when={item()?.contentType == ContentType.MEDIA}>
                                <VideoThumbnailView video={item() as IPlatformVideo}
                                    useCache={!!props?.useCache}
                                    onSettings={(e, content)=> onSettingsClicked(e, content)}
                                    onClick={() => {
                                        const url = item().backendUrl ?? item().url;
                                        if (url)
                                            video?.actions.openVideo(item() as IPlatformVideo);
                                        }} />
                            </Match>
                            <Match when={item()?.contentType == ContentType.POST}>
                                <PostThumbnailView post={item() as IPlatformPost}
                                    onSettings={(e, content)=> onSettingsClicked(e, content)}
                                    onClick={() =>{
                                        const url = item().backendUrl ?? item().url;
                                        if(url)
                                            navigate("/web/details/post?url=" + encodeURIComponent(url));
                                    }} />
                            </Match>
                            <Match when={item()?.contentType == ContentType.NESTED_VIDEO}>
                                <NestedMediaThumbnailView video={item() as IPlatformNestedMedia}
                                    onSettings={(e, content)=> onSettingsClicked(e, content)}
                                    onClick={() =>{
                                        const url = item().backendUrl ?? item().contentUrl;
                                        if(url) {
                                            Globals.handleUrl(url, video!, navigate);
                                        }
                                    }} />
                            </Match>
                            <Match when={item()?.contentType == ContentType.LOCKED}>
                                <LockedContentThumbnailView content={item() as IPlatformLockedContent}
                                    onSettings={(e, content)=> onSettingsClicked(e, content)}
                                    onClick={() =>{
                                        const url = item().backendUrl ?? item().url;
                                        if(url) {
                                            LocalBackend.open(url);
                                        }
                                    }} />
                            </Match>
                            <Match when={item()?.contentType == ContentType.PLAYLIST}>
                                {renderPlaylist(item() as IPlatformPlaylist)}
                            </Match>
                            <Match when={item()?.contentType == ContentType.CHANNEL}>
                                {renderCreator(item() as any)}
                            </Match>
                            <Match when={item()?.contentType == ContentType.PLACEHOLDER}>
                                <PlaceholderThumbnailView placeholder={item() as IPlatformContentPlaceholder} />
                            </Match>
                        </Switch>
                    } />
            </Show>
            <Portal>
                <SettingsMenu menu={settingsMenu$()} show={show$()} onHide={()=>onSettingsHidden()} anchor={contentAnchor} />
            </Portal>
        </div>
    );
};

export default ContentGrid;
