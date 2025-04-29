import { createSignal, type Component, For, createResource, Show, createEffect, createMemo } from 'solid-js';
import styles from './index.module.css';
import { SubscriptionsBackend } from '../../backend/SubscriptionsBackend';
import SearchBar from '../../components/topbars/SearchBar';
import ScrollContainer from '../../components/containers/ScrollContainer';
import VideoThumbnailView from '../../components/content/VideoThumbnailView';
import VirtualGrid from '../../components/containers/VirtualGrid';
import { useVideo } from '../../contexts/VideoProvider';
import { createResourceDefault, dateFromAny, proxyImage, proxyImageVariable } from '../../utility';
import { HomeBackend } from '../../backend/HomeBackend';
import ContentGrid from '../../components/containers/ContentGrid';
import NavigationBar from '../../components/topbars/NavigationBar';

import iconRefresh from "../../assets/icons/icon_reload_temp.svg"
import StateWebsocket from '../../state/StateWebsocket';
import StateGlobal from '../../state/StateGlobal';
import IconButton from '../../components/buttons/IconButton';
import SettingsMenu, { Menu, MenuItemButton } from '../../components/menus/Overlays/SettingsMenu';
import Anchor, { AnchorStyle } from '../../utility/Anchor';
import { Portal } from 'solid-js/web';
import LoaderGrid from '../../components/basics/loaders/LoaderGrid';
import { Pager } from '../../backend/models/pagers/Pager';
import Button from '../../components/buttons/Button';
import UIOverlay from '../../state/UIOverlay';

import { IPlatformContent } from '../../backend/models/content/IPlatformContent';
import { IPlatformVideo } from '../../backend/models/content/IPlatformVideo';
import { ContentType } from '../../backend/models/ContentType';

import { ImportBackend } from '../../backend/ImportBackend';

import iconChevDown from '../../assets/icons/icon_chrevron_down.svg'
import iconEdit from '../../assets/icons/icon24_edit.svg'
import iconChevUp from '../../assets/icons/icon_chevron_right.svg'
import iconQuestion from '../../assets/icons/icon24_faq.svg'
import iconSubscriptions from '../../assets/icons/icon_nav_subscriptions.svg'
import iconSearch from '../../assets/icons/icon24_search.svg'
import { useNavigate } from '@solidjs/router';
import EmptyContentView from '../../components/EmptyContentView';
import { DialogButton, DialogDescriptor, IDialogOutput } from '../../overlays/OverlayDialog';
import { SettingsBackend } from '../../backend/SettingsBackend';

//const subs = await SubscriptionsBackend.subscriptions();
//const subPager = await SubscriptionsBackend.subscriptionPager();
const video = useVideo();

//console.log(subs);
//console.log(subPager);

const SubscriptionsPage: Component = () => {
  const navigate = useNavigate();

  const [subProgress$, setSubProgress] = createSignal<number>(0);
  
  let [selectedCreators$, setSelectedCreators] = createSignal<string[]>([]);
  const hasSelectedCreator$ = createMemo(()=> selectedCreators$() && selectedCreators$().length > 0);
  let [selectedGroup$, setSelectedGroup] = createSignal<string>();

  StateWebsocket.registerHandlerNew("subProgress", (packet)=>{
    setSubProgress(packet.payload.progress / packet.payload.total);
  }, "subsbar");
  StateWebsocket.registerHandlerNew("SubscriptionGroupsChanged", (packet)=>{
    subGroupsResource.refetch()
  }, "subsbar");
  StateWebsocket.registerHandlerNew("SubscriptionsChanged", (packet)=>{
    subsResource.refetch();
  }, "subsbar");

  let doUpdate = false;
  const [subs$, subsResource] = createResourceDefault(async () => [], async () => {
    return await SubscriptionsBackend.subscriptions();
  });
  const [subGroups$, subGroupsResource] = createResourceDefault(async () => [], async () => await SubscriptionsBackend.subscriptionGroups());
  
  const [subCachePager$, subPagerCacheResource] = createResourceDefault(async () => {
    return await SubscriptionsBackend.subscriptionCachePager();
  });
  const [subPager$, subPagerResource] = createResourceDefault(async () => {
    const shouldUpdate = doUpdate;
    doUpdate = false;
    throw "test"
    const subscriptionPager = await SubscriptionsBackend.subscriptionPagerLazy(shouldUpdate);
    subPagerCacheResource.refetch();
    return subscriptionPager;
  });
  const [subGroupPager$, subGroupPagerResource] = createResourceDefault(async () => {
    const id = selectedGroup$();
    if(!id)
      return undefined;

    return await SubscriptionsBackend.subscriptionGroupPager(id, false);
  });
  const [filterPager$, filterPagerResource] = createResourceDefault(async () => {
    const url = selectedCreators$();
    if(!url || url.length == 0)
      return undefined;

    const filterPager =  await SubscriptionsBackend.subscriptionFilterChannelPager(url[0]);
    return filterPager;
  });
  createEffect(()=>{
    console.log("Group changed: " + selectedGroup$());
    subGroupPagerResource.refetch();
  });
  const currentPager$ = createMemo(()=>{
    if(!selectedGroup$()) {
      if(selectedCreators$() && selectedCreators$().length > 0 && filterPager$.state == "ready") {
        const filterPagerResult = filterPager$();
        return filterPagerResult;
      }
      if(subPager$.state == "ready" && subPager$()?.hadInitialUpdate$())
        return subPager$();
      else
        return subCachePager$();
    }
    else
      return subGroupPager$();
  });
  createEffect(()=>{
    const pager = currentPager$();
    console.log("Current pager changed", {filteredItems: pager?.dataFiltered.length, items: pager?.data.length});
    if(pager)
      updateFilter(pager, getFilter());
  })

  enum FilterType {
    Media = 0,
    Playlists,
    Posts,
    Live,
    Planned,
    Watched
  }
  
  const filters = [
    {
      name: "Media",
      active: createSignal(true)
    },
    {
      name: "Playlists",
      active: createSignal(true)
    },
    {
      name: "Posts",
      active: createSignal(true)
    },
    {
      name: "Live",
      active: createSignal(true)
    },
    {
      name: "Planned",
      active: createSignal(false)
    },
    {
      name: "Watched",
      active: createSignal(true)
    }
  ];
  function toggleFilter(index: number){
    filters[index].active[1](!filters[index].active[0]());
    const pager = currentPager$();
    if(pager)
      updateFilter(pager, getFilter());
  }
  function getFilter() {
    return (obj: IPlatformContent)=>{
      //const creators = selectedCreators$();
      //if(creators.length > 0 && creators.indexOf(obj.author.url) < 0)
      //  return false;

      if(obj.contentType == ContentType.MEDIA) {
        const video = obj as IPlatformVideo;
        if (!filters[FilterType.Media].active[0]())
          return false;
        if(!filters[FilterType.Live].active[0]() && video.isLive)
          return false;
        if(!filters[FilterType.Planned].active[0]() && ((dateFromAny(video.dateTime)?.diffNow()?.milliseconds ?? 0) > 0))
          return false;
      }
      else if(obj.contentType == ContentType.POST && !filters[FilterType.Posts].active[0]())
        return false;
      else if(obj.contentType == ContentType.PLAYLIST && !filters[FilterType.Playlists].active[0]())
        return false;
      else if(obj.contentType == ContentType.NESTED_VIDEO && !filters[FilterType.Media].active[0]())
        return false;

      if(!filters[FilterType.Watched].active[0]()) {
        if(((obj as any)?.metadata)?.watched)
          return false;
      }
      return true;
    };
  }

  function toggleCreator(channelUrl: string) {
    if(selectedCreators$() && selectedCreators$().indexOf(channelUrl) >= 0)
      setSelectedCreators([]);
    else
      setSelectedCreators([channelUrl]);
    filterPagerResource.refetch();
    /*
    const existingIndex = selectedCreators$().indexOf(creatorId);
    if(existingIndex >= 0)
      setSelectedCreators(selectedCreators$().filter(x=>x != creatorId));
    else
      setSelectedCreators(selectedCreators$().concat([creatorId]));
      const pager = currentPager$();
      if(pager)
        updateFilter(pager, getFilter());
      */
  }

  function updateFilter(pager: Pager<IPlatformContent>, condition: (obj: IPlatformContent)=>boolean){
    pager.setFilter(condition);
  }

  const [showReloadMenu$, setShowReloadMenu] = createSignal(false);
  const anchor = new Anchor(null, showReloadMenu$, AnchorStyle.BottomRight);
  const reloadMenu = {
    title: "",
    items: [
      new MenuItemButton("Reload from Update", iconRefresh, "Updates the subscriptions", ()=>{
        doUpdate = true;
        subPagerResource.refetch();
        setShowReloadMenu(false);
      }),
      new MenuItemButton("Reload from Cache", iconRefresh, "Just reloads the cached view", ()=>{
        doUpdate = false;
        subPagerResource.refetch();
        setShowReloadMenu(false);
      })
    ]
  } as Menu;
  function setReloadButtonRef(element: HTMLElement) {
    anchor.setElement(element);
  }

  const [subGroupsExpanded$, setSubGroupsExpanded] = createSignal(false);
  function newSubscriptionGroup(){
    UIOverlay.overlayNewSubscriptionGroup((group)=>{
      subGroupsResource.refetch();
    });
  }
  function dismissSubscriptionGroups() {
    UIOverlay.dialog({
      icon: iconQuestion,
      title: "Are you sure?",
      description: "If you change your mind later you can re-enable it in settings.",
      buttons: [
        {
          title: "Cancel",
          onClick(output: IDialogOutput) {

          }
        } as DialogButton,
        {
          title: "Dismiss",
          style: "primary",
          onClick(output: IDialogOutput) {
            SettingsBackend.dismissSubscriptionGroups();
          }
        } as DialogButton
      ]
    } as DialogDescriptor)
  }

  const hasSubGroups$ = createMemo(()=>{
    return subGroups$() && subGroups$()!.length > 0;
  })
  createEffect(()=>{
    const subs = subGroups$();
    const a = subs;
  })

  let scrollContainerRef: HTMLDivElement | undefined;

  return (
    <div class={styles.container}>
      <NavigationBar isRoot={true} childrenAfter={
          <img src={iconRefresh} style={{"margin-left": "24px", "cursor": "pointer", "height": "30px", "width": "30px" }}
            ref={setReloadButtonRef}
            onClick={()=>{ setShowReloadMenu(true) }} />
      } />
      <Show when={subs$() && subs$()!.length > 0}>
        <div style="flex-shrink: 0; position: relative;">
          <div class={styles.subBar}>
            <For each={subs$()}>{(sub, i) =>
              <div class={styles.channel} onClick={()=>toggleCreator(sub.channel.url)} classList={{[styles.active]: selectedCreators$().indexOf(sub.channel.url) >= 0}}>
                  <div>
                    <img src={sub.channel.thumbnail} class={styles.channelImg} />
                    <div class={styles.sourceIcon}>
                      <img src={StateGlobal.getSourceConfig(sub.channel.id.pluginID)?.absoluteIconUrl} />
                    </div>
                  </div>
                  <div class={styles.channelText}>
                    {sub.channel.name}
                  </div>
              </div>
            }</For>
          </div>
          <Show when={!hasSubGroups$() && StateGlobal.settings$().object.subscriptions?.showSubscriptionGroups}>
            <div class={styles.subgroupBanner}>
              <div class={styles.bannerText}>
                <div class={styles.bannerTitle}>
                  Stay organized with Subscription Groups
                </div>
                <div class={styles.bannerDescription}>
                  Subscription groups are your personalized way to organize and enjoy content from multiple creators.
                </div>
              </div>
              <div style="flex-grow: 1"></div>
              <div class={styles.bannerButtons}>
                <Button text="Create a subscription group" color="linear-gradient(267deg, #01D6E6 -100.57%, #0182E7 90.96%)"
                  onClick={()=>newSubscriptionGroup()} />
                <Button text="Dismiss" color="transparant" style={{ border: "1px solid rgba(1, 155, 231, 0)", "margin-left": "16px" }} 
                  onClick={()=>dismissSubscriptionGroups()} />
              </div>
            </div>
          </Show>
          <Show when={hasSubGroups$()}>
              <div class={styles.subgroups} style={{
                height: subGroupsExpanded$() ? undefined : '82px',
                overflow: 'hidden'
              }}>
                <div class={styles.subGroupExpand} onClick={()=>setSubGroupsExpanded(!subGroupsExpanded$())}>
                  <Show when={subGroupsExpanded$()}>
                    <img src={iconChevUp} />
                  </Show>
                  <Show when={!subGroupsExpanded$()}>
                    <img src={iconChevDown} />
                  </Show>
                </div>
                <For each={subGroups$()}>{(subGroup: ISubscriptionGroup, i) =>
                  <div class={styles.subgroup} classList={{[styles.active]: subGroup.id == selectedGroup$() }} 
                      onClick={()=>(subGroup.id == selectedGroup$()) ? setSelectedGroup(undefined) : setSelectedGroup(subGroup.id)}>
                    <div class={styles.image} style={{"background-image": "url(" + proxyImageVariable(subGroup.image) + ")"}} />
                    
                    <img class={styles.editIcon} src={iconEdit} onClick={(ev)=>{ UIOverlay.overlayEditSubscriptionGroup(subGroup); ev.stopPropagation()}} />
                    <div class={styles.name}>
                      {subGroup.name}
                    </div>
                  </div>
                }</For>
                <div class={styles.subgroup} style="cursor: pointer" onClick={()=>newSubscriptionGroup()}>
                  <div class={styles.image} style={{"background": "#222"}} />
                  <div class={styles.centerText}>
                    New Group
                  </div>
                </div>
              </div>
          </Show>
          <Show when={!!filters}>
            <div class={styles.filters}>
              <For each={filters}>{(filter, i) =>
                <div class={styles.filter} classList={{[styles.active]: filter.active[0]()}} onClick={()=>toggleFilter(i())}>
                  <div class={styles.name}>
                    {filter.name}
                  </div>
                </div>
              }</For>
            </div>
          </Show>
          <Show when={subProgress$() > 0 && subProgress$() < 1}>
              <div style={{height: "2px", width: (subProgress$() * 100) + "%", position: "absolute", bottom: "1px", background: "linear-gradient(267deg, rgb(1, 214, 230) -100.57%, rgb(1, 130, 231) 90.96%)"}}>
              </div>
            </Show>
        </div>
        <Show when={!currentPager$() || (hasSelectedCreator$() && filterPager$.state != "ready")}>
          <LoaderGrid itemCount={18} />
        </Show>
        <Show when={currentPager$() && (!hasSelectedCreator$() || filterPager$.state == "ready")}>
          <div class={styles.content}>
            <Portal>
              <SettingsMenu menu={reloadMenu} show={showReloadMenu$()} anchor={anchor} onHide={()=>setShowReloadMenu(false)} />
            </Portal>
            <ScrollContainer ref={scrollContainerRef}>
              <ContentGrid pager={currentPager$()} outerContainerRef={scrollContainerRef} useCache={true} />
            </ScrollContainer>
          </div>
        </Show>
      </Show>
      <Show when={(!subs$() || subs$()!.length == 0) && !subs$.loading}>

        <EmptyContentView 
          icon={iconSubscriptions}
          title='You have no subscriptions'
          description='Subscribe to some creators or import them from elsewhere.'
          actions={[
            {
              icon:iconSubscriptions,
              title: "Import Subscriptions",
              action: ()=>{UIOverlay.dismiss(); UIOverlay.overlayImportSelect()}
            },
            {
              icon: iconSearch,
              title: "Search Creators",
              color: "#019BE7",
              action: ()=>{navigate("/web/search?type=" + ContentType.CHANNEL)}
            }
          ]} />
      </Show>
      <Show when={(!subs$() || subs$()!.length == 0) && subs$.loading}>
        <LoaderGrid />
      </Show>
    </div>
  );
};

export default SubscriptionsPage;
