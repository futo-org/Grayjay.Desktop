import { createResource, type Component, For, Show, createMemo, onCleanup, createSignal, Switch, Match, batch } from 'solid-js';
import { createResourceDefault, getBestThumbnail, getDummyVideo, getPlaylistThumbnail, proxyImage, toHumanBitrate, toHumanBytesSize } from '../../utility';
import { PlatformBackend } from '../../backend/PlatformBackend';
import { ChannelBackend } from '../../backend/ChannelBackend';
import { useVideo } from '../../contexts/VideoProvider';
import ScrollContainer from '../../components/containers/ScrollContainer';
import VirtualGrid from '../../components/containers/VirtualGrid';
import VideoThumbnailView from '../../components/content/VideoThumbnailView';
import { useNavigate, useSearchParams } from '@solidjs/router';
import ContentGrid from '../../components/containers/ContentGrid';
import LoaderContainer from '../../components/basics/loaders/LoaderContainer';
import { DownloadBackend } from '../../backend/DownloadBackend';
import DownloadingView from '../../components/downloads/DownloadingView';
import StateWebsocket from '../../state/StateWebsocket';
import { IVideoLocal } from '../../backend/models/downloads/IVideoLocal';

import storageIcon from '../../assets/icons/icon_storage.svg';
import playlistIcon from '../../assets/icons/icon_nav_playlists.svg';
import videoIcon from '../../assets/icons/videos.svg';
import audioIcon from '../../assets/icons/ic_audio.svg';
import searchIcon from '../../assets/icons/icon24_search.svg';
import folderIcon from '../../assets/icons/icon_folder.svg';
import fileIcon from '../../assets/icons/icon_import_filled.svg';
import iconDownloads from '../../assets/icons/icon24_download.svg';
import iconTrash from '../../assets/icons/icon_trash.svg';
import iconQueue from '../../assets/icons/icon_add_to_queue.svg';
import iconPlaylist from '../../assets/icons/icon_nav_playlists.svg';
import iconWatchLater from '../../assets/icons/icon24_watch_later.svg';
import iconAddToPlaylist from '../../assets/icons/icon24_add_to_playlist.svg';
import iconDelete from '../../assets/icons/icon_trash.svg';
import iconHide from '../../assets/icons/icon24_hide.svg';

import styles from './index.module.css';
import { IVideoDownload } from '../../backend/models/downloads/IVideoDownload';
import DownloadedView from '../../components/downloads/DownloadedView';
import TogglePill from '../../components/basics/inputs/TogglePill';
import InputText from '../../components/basics/inputs/InputText';
import ViewTypeToggles from '../../components/basics/ViewTypeToggles';
import DataTable from '../../components/containers/DataTable';
import UIOverlay from '../../state/UIOverlay';
import ExceptionModel from '../../backend/exceptions/ExceptionModel';
import EmptyContentView from '../../components/EmptyContentView';
import { ContentType } from '../../backend/models/ContentType';
import PlaylistView from '../../components/content/PlaylistView';
import { Portal } from 'solid-js/web';
import SettingsMenu, { Menu, MenuItemButton, MenuSeperator } from '../../components/menus/Overlays/SettingsMenu';
import Anchor, { AnchorStyle } from '../../utility/Anchor';
import { PlayList } from 'dashjs';
import { IPlaylist } from '../../backend/models/IPlaylist';
import { IPlatformContent } from '../../backend/models/content/IPlatformContent';
import { IPlatformVideo } from '../../backend/models/content/IPlatformVideo';
import { WatchLaterBackend } from '../../backend/WatchLaterBackend';
import Button from '../../components/buttons/Button';

const DownloadsPage: Component = () => {
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const [storageInfo$, storageInfoResource] = createResourceDefault(async () => [], async () => await DownloadBackend.getStorageInfo());
  const [downloading$, downloadingResource] = createResourceDefault(async () => [], 
  
  /*async () => ([1,2,3,4,5,6,7, 8,9].map(x=>
      { return {
        video: getDummyVideo(),
        videoDetails: getDummyVideo(),
        progress: 1 * Math.random(),
        downloadSpeedVideo: parseInt(100000 * Math.random()),
        downloadSpeedAudio: parseInt(10000 * Math.random()),
        downloadSpeed: parseInt(100000 * Math.random()),
        videoFileSize: 123500,
        audioFileSize: 27300
      } as IVideoDownload})
    ));*/
    async () => {
      let result = (await DownloadBackend.getDownloading()).sort((a,b)=>b.state - a.state);

      const wasRetryable = isDownloadingRetryable$();
      const isRetryable = result && (result?.length ?? 0) > 0 && !!result?.find(x=>x.state == 7) && !result?.find(x=>x.state != 7);
      if(wasRetryable != isRetryable)
        setIsDownloadingRetryable(isRetryable);
      return result;
    });
  const [downloadingPlaylists$, downloadingPlaylistsResource] = createResourceDefault(async ()=> [], async () => await DownloadBackend.getDownloadingPlaylists());

  
  /*
  const [downloading$, downloadingResource] = createResourceDefault(async () => [], async () => {
    const item = await DownloadBackend.getDownloaded();

    return [{
      video: item[0],
      videoDetails: item[0],
      state: 1,
      downloadSpeed: 1000 * 1000 * 3.4,
      downloadSpeedAudio: 1000 * 1.1,
      downloadSpeedVideo: 1000 * 1000 * 2.3,
      videoFileSize: 1000 * 1000 * 214,
      audioFileSize: 1000 * 1000 * 4.5,
      videoSource: {
        width: 1920,
        height: 1080
      },
      audioSource: {
        bitrate: 1000 * 1000 * 5.31
      },
      progress: 0.73,
      error: ""
    } as unknown as IVideoDownload];
  });
  */
  const [downloaded$, downloadedResource] = createResourceDefault(async () => [], async () => await DownloadBackend.getDownloaded());
  const [playlists$, playlistsResource] = createResourceDefault(async () => [], async () => []);

  const isReady$ = createMemo(()=>downloading$.state == "ready" && downloaded$.state == "ready");

  StateWebsocket.registerHandlerNew("DownloadCompleted", (packet)=>{
    const videoLocal = packet.payload as IVideoLocal;
    downloadingResource.refetch();
    downloadedResource.refetch();
  }, "downloads");
  StateWebsocket.registerHandlerNew("DownloadsChanged", (packet)=>{
    downloadingResource.refetch();
    downloadedResource.refetch();
  }, "downloads");
  StateWebsocket.registerHandlerNew("DownloadChanged", (packet)=>{
    const download = packet.payload as IVideoDownload;
    if(download.state == 7 || download.state == 6 ) {
      downloadingResource.refetch();
    }
    else if(isDownloadingRetryable$()) {
      downloadingResource.refetch();
    }
  }, "downloads");

  onCleanup(()=>{
    StateWebsocket.unregisterHandler("DownloadCompleted", "downloads");
  });

  const [videoType$, setVideoType] = createSignal("media");
  const [videoSearch$, setVideSearch] = createSignal("");
  const [viewType$, setViewType] = createSignal("grid");

  async function exportDownloads(ids: IPlatformID[]) {
    try {
      await DownloadBackend.exportDownloads(ids);
      downloadedResource.refetch();
      storageInfoResource.refetch();
    }
    catch(ex) {
      if(ex instanceof ExceptionModel) {
        UIOverlay.overlayError((ex as ExceptionModel), { });
      }
      else
        UIOverlay.dialog({ title: "Failed to export download", description: ex + "", buttons: []})
    }
  }
  async function exportDownload(id: IPlatformID) {
    try {
      await DownloadBackend.exportDownload(id);
      downloadedResource.refetch();
      storageInfoResource.refetch();
    }
    catch(ex) {
      if(ex instanceof ExceptionModel) {
        UIOverlay.overlayError((ex as ExceptionModel), { });
      }
      else
        UIOverlay.dialog({ title: "Failed to export download", description: ex + "", buttons: []})
    }
  }

  async function deleteDownloads(downloadIds: IPlatformID[]) {
    try {
      for(let downloadId of downloadIds)
        await DownloadBackend.deleteDownload(downloadId);
      downloadedResource.refetch();
      storageInfoResource.refetch();
    }
    catch(ex) {
      if(ex instanceof ExceptionModel) {
        UIOverlay.overlayError((ex as ExceptionModel), { });
      }
      else
        UIOverlay.dialog({ title: "Failed to delete download", description: ex + "", buttons: []})
    }
  }

  async function deleteDownload(downloadId: IPlatformID) {
    try {
      await DownloadBackend.deleteDownload(downloadId);
      downloadedResource.refetch();
      storageInfoResource.refetch();
    }
    catch(ex) {
      if(ex instanceof ExceptionModel) {
        UIOverlay.overlayError((ex as ExceptionModel), { });
      }
      else
        UIOverlay.dialog({ title: "Failed to delete download", description: ex + "", buttons: []})
    }
  }

  async function deleteDownloadPlaylist(playlistId: string) {
    try {
      await DownloadBackend.deleteDownloadPlaylist(playlistId);
      downloadedResource.refetch();
      downloadingPlaylistsResource.refetch();
      storageInfoResource.refetch();
    }
    catch(ex) {
      if(ex instanceof ExceptionModel) {
        UIOverlay.overlayError((ex as ExceptionModel), { });
      }
      else
        UIOverlay.dialog({ title: "Failed to delete download", description: ex + "", buttons: []})
    }
  }

  function getDownloadedItems(): IVideoLocal[] {
    switch(videoType$()) {
      case "media":
        return downloaded$()?.filter(x=>x.name.indexOf(videoSearch$()) >= 0 && !x.groupID) ?? [];
      case "video":
        return downloaded$()?.filter(x=>(x.videoSources?.length ?? 0) != 0)
          ?.filter(x=>x.name.indexOf(videoSearch$()) >= 0 && !x.groupID) ?? [];
      case "audio":
        return downloaded$()?.filter(x=>(x.videoSources?.length ?? 0) == 0 && (x.audioSources?.length ?? 0) != 0)
          ?.filter(x=>x.name.indexOf(videoSearch$()) >= 0 && !x.groupID) ?? [];
      default:
          return [];
    }
  }
  
  const [playlistMenu$, setPlaylistMenu] = createSignal<Menu>()
  const playlistMenuShow$ = createMemo(()=>!!playlistMenu$());
  const playlistMenuAnchor = new Anchor(null, playlistMenuShow$, AnchorStyle.BottomRight);
  function showPlaylistMenu(playlist: IPlaylist, element: HTMLElement) {
    playlistMenuAnchor.setElement(element);
    setPlaylistMenu({
      title: "",
      items: [
        new MenuItemButton("Export Playlist", folderIcon, undefined, ()=>{
          exportDownloads(playlist?.playlist?.videos?.map(x=>x.id) ?? []);
        }),
        new MenuItemButton("Delete Downloads", iconTrash, undefined, ()=>{
          deleteDownload
          deleteDownloadPlaylist(playlist?.playlist?.id);
        }),
      ]
    } as Menu)
  }


  const [settingsContent$, setSettingsContent] = createSignal<IVideoLocal>();
  const settingsMenu$ = createMemo(() => {
      const content = settingsContent$();        
      return {
          title: "",
          items: [
          
              ... (content?.contentType === ContentType.MEDIA ? [ 
                  new MenuItemButton("Add to queue", iconQueue, undefined, ()=>{
                      video?.actions.addToQueue(content as any as IPlatformVideo);
                  }),
                  /*
                  new MenuItemButton("Play feed as queue", iconPlaylist, undefined, ()=>{

                  }),
                  new MenuSeperator(),*/
                  new MenuItemButton("Watch later", iconWatchLater, undefined, async () => {
                      await WatchLaterBackend.add(content as any as IPlatformVideo);
                      await video?.actions?.refetchWatchLater();
                  }),
                  new MenuItemButton("Add to playlist", iconAddToPlaylist, undefined, async () => {
                      await UIOverlay.overlayAddToPlaylist(content as any as IPlatformVideo);
                  })
              ] : []),
              new MenuSeperator(),
              new MenuItemButton("Export", fileIcon, undefined, ()=>{
                  if(content)
                    exportDownload(content?.id);
              }),
              new MenuItemButton("Delete", iconDelete, undefined, ()=>{
                  if(content)
                    deleteDownload(content?.id);
              })
              /*
              new MenuSeperator(),
              new MenuItemButton("Hide creator from feed", iconHide, undefined, ()=>{

              }),*/
          ]
      } as Menu;
  });
    const [show$, setShow] = createSignal<boolean>(false);
    const contentAnchor = new Anchor(null, show$, AnchorStyle.BottomRight);
    function onSettingsClicked(element: HTMLElement, content: IVideoLocal) {
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



  function gridUI(scrollContainerRef: HTMLDivElement | undefined) {
    const data = getDownloadedItems();
    return (
      <Switch>
        <Match when={videoType$() != "playlist"}>          
          <VirtualGrid outerContainerRef={scrollContainerRef}
              items={data!}
              itemWidth={300}
              calculateHeight={(width) => {
                  const aspectRatio = 16 / 9;
                  const thumbnailHeight = width / aspectRatio;
                  const margin1 = 16;
                  const fontSize = 18;
                  const textHeight = 1.2 * fontSize;
                  const margin2 = 16;
                  const dataHeight = 32;
                  const totalHeight = thumbnailHeight + margin1 + textHeight + margin2 + dataHeight;
                  return totalHeight;                        
              }}
              autosizeWidth={true}
              notifyEndOnLast={5}
              style={{
                  "margin-left": "20px",
                  "margin-top": "20px",
                  "margin-bottom": "10px"
              }}
              builder={(index, item) =>
                <DownloadedView downloaded={item()} onSettings={(e, content)=> onSettingsClicked(e, content)} />
              } />
        </Match>
        <Match when={videoType$() == "playlist"}>
          <VirtualGrid outerContainerRef={scrollContainerRef}
            items={downloadingPlaylists$()}
            itemWidth={300}
            calculateHeight={(width) => {
                const aspectRatio = 16 / 9;
                const thumbnailHeight = width / aspectRatio;
                const margin1 = 16;
                const fontSize = 18;
                const textHeight = 1.2 * fontSize;
                const margin2 = 16;
                const dataHeight = 32;
                const totalHeight = thumbnailHeight + margin1 + textHeight + margin2 + dataHeight;
                return totalHeight;                        
            }}
            autosizeWidth={true}
            notifyEndOnLast={5}
            style={{
                "margin-left": "30px",
                "margin-top": "15px",
                "margin-bottom": "10px"
            }}
            builder={(index, item) =>
              <Show when={!!item()}>
                <PlaylistView name={item().playlist.name} 
                  itemCount={item().playlist.videos.length}
                  thumbnail={getPlaylistThumbnail(item().playlist) ?? ""} onClick={()=>navigate("/web/playlist?id=" + item().playlist.id)}
                  onSettings={(el)=>{ showPlaylistMenu(item(), el)}} />
              </Show>
            } />
                
            <Portal>
                <Show when={playlistMenu$()}>
                  <SettingsMenu menu={playlistMenu$()!!} show={playlistMenuShow$()} onHide={()=>{setPlaylistMenu(undefined)}} anchor={playlistMenuAnchor} />
                </Show>
            </Portal>
        </Match>
      </Switch>
    );
  }

  let [selected$, setSelected] = createSignal<IVideoLocal[]>([]);
  function listUI(){
    const data = getDownloadedItems();
      
    function calcSize(downloaded: IVideoLocal): number{
      let size = 0;
      if(downloaded.videoSources)
        for(let source of downloaded.videoSources)
          size += parseInt(source.fileSize);
      if(downloaded.audioSources)
        for(let source of downloaded.audioSources)
          size += parseInt(source.fileSize);
        return size;
    }
    function openVideo(item: IVideoLocal) {
      video?.actions.openVideo(item.videoDetails);
    }
    function openPlaylist(item: IPlaylist) {
      navigate("/web/playlist?id=" + item.id);
    }
    const columns = [
      {
        name: "",
        resolve: (row:any)=> getBestThumbnail(row.videoDetails.thumbnails)?.url,
        type: "image",
        onClick(video: IVideoLocal){ openVideo(video); }
      },
      {
        name: "Creator",
        resolve: (row:any)=> row.author.name
      },
      {
        name: "Name",
        resolve: (row:any)=> row.name,
        onClick(video: IVideoLocal){ openVideo(video); }
      },
      {
        name: "Quality",
        resolve: (row:any)=> [
            ((row.videoSources?.length ?? 0) > 0) ? (row.videoSources[0].width + "x" + row.videoSources[0].height) : null,
            ((row.audioSources?.length ?? 0) > 0) ? (toHumanBitrate(row.audioSources[0].bitrate)) : null
          ].filter(x=>x).join(" • "),
        style: {
          color: "#8C8C8C"
        },
        onClick(video: IVideoLocal){ openVideo(video); }
      },
      {
        name: "Size",
        resolve: (row: any)=> toHumanBytesSize(calcSize(row)),
        style: {
          color: "#8C8C8C"
        },
        onClick(video: IVideoLocal){ openVideo(video); }
      },
      {
        name: "",
        resolve: (row: any) =>{
          return (
            <div style="text-align: right; text-wrap: nowrap">
              <button class={styles.selectedButton} onClick={()=>{exportDownload(row.id)}}>
                Export
              </button>
              <button class={styles.selectedButton} onClick={()=>{deleteDownload(row.id)}} style="color: #F97066; margin-right: 20px;">
                Delete
              </button>
            </div>
          )
        }
      }
    ]
    return (
      <div style="margin-left: 30px; margin-right: 30px">
        <Switch>
            <Match when={videoType$() != "playlist"}>
              <DataTable selectable={true}
                onSelectionChanged={(selected)=>{setSelected(selected)}} 
                columnInfo={columns} 
                data={data!} 
                style={{"margin-top": "20px"}} />
            </Match>
            <Match when={videoType$() == "playlist"}>
              <DataTable selectable={true}
                  onSelectionChanged={(selected)=>{setSelected(selected)}} 
                  columnInfo={[
                    {
                      name: "",
                      resolve: (row:any)=> getPlaylistThumbnail(row.playlist),
                      type: "image",
                      onClick(row: any){ openPlaylist(row.playlist); }
                    },
                    {
                      name: "Name",
                      resolve: (row:any)=> row.playlist.name,
                      onClick(row:any){ openPlaylist(row.playlist); }
                    },
                    {
                      name: "Resolution",
                      resolve: (row:any)=> row.targetPixelCount,
                      style: {
                        color: "#8C8C8C"
                      },
                      onClick(video: IVideoLocal){ openVideo(video); }
                    },
                    {
                      name: "",
                      resolve: (row: any) =>{
                        return (
                          <div style="text-align: right; text-wrap: nowrap">
                            <button class={styles.selectedButton} onClick={()=>{exportDownloads(row?.playlist?.videos?.map(x=>x.id) ?? [])}}>
                              Export
                            </button>
                            <button class={styles.selectedButton} onClick={()=>{deleteDownloadPlaylist(row?.playlist?.id)}} style="color: #F97066; margin-right: 20px;">
                              Delete
                            </button>
                          </div>
                        )
                      }
                    }
                  ]} 
                  data={downloadingPlaylists$()} 
                  style={{"margin-top": "20px"}} />
            </Match>
        </Switch>
      </div>
    )
  }

  const [isDownloadingRetryable$, setIsDownloadingRetryable] = createSignal(false);

  const video = useVideo();
  let scrollContainerRef: HTMLDivElement | undefined;
  return (
    <LoaderContainer isLoading={!isReady$()} loadingText={"Loading Downloads"} loadingSubText={params.url} background='var(--black-color-1)'>
      <ScrollContainer ref={scrollContainerRef}>
        <Show when={storageInfo$()}>
        <div class={styles.storageContainer}>
          <div class={styles.sizeLine}>
            <img src={storageIcon} class={styles.sizeImage} />
            <div class={styles.sizeCurrent}>
              {toHumanBytesSize(storageInfo$()?.usedBytes)}
            </div>
            <div class={styles.sizeInter}>
              used out of
            </div>
            <div class={styles.sizeTotal}>
              {toHumanBytesSize((storageInfo$()?.availableBytes ?? 0) + (storageInfo$()?.usedBytes ?? 0))}
            </div>
            <div class={styles.storagePath}>
              <img src={folderIcon} style=" margin-top:0.5px; cursor: pointer;" onClick={()=>DownloadBackend.changeDownloadDirectory()} />
              <span style="vertical-align: top;">
                {storageInfo$()?.storageLocation}
              </span>
              <div class={styles.tooltip}>
                {storageInfo$()?.storageLocation}
              </div>
            </div>
            <div class={styles.right}>
              <div class={styles.rightItem}>
                <img class={styles.itemImage} src={playlistIcon} />
                <div class={styles.itemText}>
                  {0} Playlists
                </div>
              </div>
              <div class={styles.rightItem}>
                <img class={styles.itemImage} src={videoIcon} />
                <div class={styles.itemText}>
                  {downloaded$()?.filter(x=>(x.videoSources?.length ?? 0) > 0).length} Videos
                </div>
              </div>
              <div class={styles.rightItem}>
                <img class={styles.itemImage} src={audioIcon} />
                <div class={styles.itemText}>
                {downloaded$()?.filter(x=>(x.videoSources?.length ?? 0) == 0 && (x.audioSources?.length ?? 0) > 0).length} Audios
                </div>
              </div>
            </div>
          </div>
          <div class={styles.storageIndicator}>
            <div class={styles.bar} style={{"width": (storageInfo$()!.usedBytes / (storageInfo$()!.availableBytes + storageInfo$()!.usedBytes)) * 100 + "%"}}>

            </div>
          </div>
        </div>
        </Show>
        <Show when={downloading$() && downloading$()!.length > 0}>
          <div style="margin-left: 30px; margin-right: 30px; margin-bottom: 30px; white-space: nowrap; overflow-y: hidden; min-height: 300px; position: relative;">
            <h2>Downloading</h2>
            <Show when={isDownloadingRetryable$()}>
              <div style="position: absolute; right: 16px; top: 16px">
                <Button text='Retry' style={{"height": "42px", "padding-top": "10px"}} onClick={()=>{DownloadBackend.downloadCycle()}} />
              </div>
            </Show>
            <div>
              <For each={downloading$()}>{ downloading =>
                <DownloadingView downloading={downloading} />
              }</For>
            </div>
          </div>
        </Show>

        <Show when={downloaded$() && downloaded$()!.length > 0}>
          <div style="margin-left: 30px; margin-right: 30px; margin-bottom: 30px; flex-grow: 1; display: flex; flex-direction: column; overflow: hidden;">
            <h2 style="margin-bottom: 5px;">Downloaded</h2>
            <div class={styles.downloadFilterBar}>
              <div class={styles.searchBar}>
                <img src={searchIcon} />
                <input class={styles.input} oninput={(e)=>setVideSearch(e.target.value)} placeholder='Search' />
              </div>
              <div class={styles.filters}>
                  <TogglePill name='Media' value={videoType$() == "media"} onToggle={()=>{setVideoType("media")}} />
                  <TogglePill name='Videos' value={videoType$() == "video"} onToggle={()=>{setVideoType("video")}} />
                  <TogglePill name='Audio' value={videoType$() == "audio"} onToggle={()=>{setVideoType("audio")}} />
                  <TogglePill name='Playlists' value={videoType$() == "playlist"} onToggle={()=>{setVideoType("playlist")}} />
              </div>
              <div class={styles.viewTypes}>
                <Show when={selected$().length > 0}>
                  <div style="display: inline-block; text-align: right;">
                    
                    <button class={styles.selectedButton} onClick={()=>{
                        if(videoType$() != "playlist")
                          selected$().forEach(x=>exportDownload(x.id));
                        else if(selected$().length > 0)
                          exportDownloads(selected$().flatMap(x=>(x as any).playlist?.videos?.map(y=>y.id)));
                      }}>
                      Export
                    </button>
                    <button class={styles.selectedButton} onClick={()=>{ 
                        if(videoType$() != "playlist")
                          deleteDownloads(selected$().map(x=>x.id));
                        else
                          selected$().forEach(x=>deleteDownloadPlaylist((x as any).playlist?.id));
                        }} style="color: #F97066; margin-right: 5px;">
                      Delete
                    </button>
                  </div>
                </Show>

                <ViewTypeToggles value={viewType$()} onToggle={(val)=>{setViewType(val)}} />
              </div>
            </div>
          </div>
          <Switch>
            <Match when={viewType$() == "grid"}>
              {gridUI(scrollContainerRef)}
            </Match>
            <Match when={viewType$() == "list"}>
              {listUI()}
            </Match>
          </Switch>
        </Show>
        <Show when={!(downloaded$() && downloaded$()!.length > 0)}>
          <EmptyContentView 
            icon={iconDownloads}
            title='You have no finished downloads'
            description='Download some videos and come back here'
            actions={[
              {
                icon: searchIcon,
                title: "Search Videos",
                color: "var(--blue-color-1)",
                action: ()=>{navigate("/web/search?type=" + ContentType.MEDIA)}
              }
            ]} />
        </Show>
            <Portal>
                <SettingsMenu menu={settingsMenu$()} show={show$()} onHide={()=>onSettingsHidden()} anchor={contentAnchor} />
            </Portal>
      </ScrollContainer>
    </LoaderContainer>
  );
};

export default DownloadsPage;
