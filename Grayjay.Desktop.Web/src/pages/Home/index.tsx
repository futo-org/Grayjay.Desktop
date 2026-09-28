import { createResource, type Component, Show, onMount, createSignal, createEffect, createMemo } from 'solid-js';

import styles from './index.module.css';
import { HomeBackend } from '../../backend/HomeBackend';
import ContentGrid from '../../components/containers/ContentGrid';
import NavigationBar from '../../components/topbars/NavigationBar';
import ScrollContainer from '../../components/containers/ScrollContainer';
import StateGlobal, { HomeFilterState } from '../../state/StateGlobal';
import { DateTime } from 'luxon';
import IconButton from '../../components/buttons/IconButton';

import iconRefresh from "../../assets/icons/icon_reload_temp.svg"
import iconFilters from "../../assets/icons/iconfilters.svg"
import iconHome from "../../assets/icons/icon_nav_home.svg"
import iconSources from "../../assets/icons/ic_circles.svg"
import { useNavigate } from '@solidjs/router';
import EmptyContentView from '../../components/EmptyContentView';
import { focusable } from '../../focusable'; void focusable;
import LiveChatWindow from '../../components/LiveChatWindow';
import UIOverlay from '../../state/UIOverlay';
import { Portal } from 'solid-js/web';
import Anchor, { AnchorStyle } from '../../utility/Anchor';
import SettingsMenu, { Menu, MenuItemToggle } from '../../components/menus/Overlays/SettingsMenu';
import { dateFromAny } from '../../utility';
import { IPlatformContent } from '../../backend/models/content/IPlatformContent';

function buildHomeFilter(state: HomeFilterState) {
  return (obj: IPlatformContent): boolean => {
    const meta = (obj as any)?.metadata;
    if(state.hideWatched && meta?.watched)
      return false;
    if(state.inProgressOnly) {
      const position = meta?.position ?? 0;
      if(!(position > 0 && !meta?.watched))
        return false;
    }
    if(state.hideLive && (obj as any)?.isLive)
      return false;
    if(state.hidePlanned && ((dateFromAny(obj.dateTime)?.diffNow()?.milliseconds ?? 0) > 0))
      return false;
    return true;
  };
}

const HomePage: Component = () => {
  const homePager = StateGlobal.home$;

  const nav = useNavigate();
  
  //createResource(async () => await HomeBackend.homePagerLazy());
  const lastHomeMillis = Math.abs(StateGlobal.lastHomeTime$()?.diffNow().toMillis());
  if((lastHomeMillis ?? 0) > 2 * 60 * 1000) {
    StateGlobal.reloadHome();
  }
  console.log("Home page with resource: ", homePager());
  console.log("Home page with resource state: ", homePager.state);
  
  const [showFilterMenu$, setShowFilterMenu] = createSignal(false);
  const filterAnchor = new Anchor(null, showFilterMenu$, AnchorStyle.BottomRight);

  const hasActiveFilter$ = createMemo(() => {
    const filter = StateGlobal.homeFilter$();
    return filter.hideWatched || filter.inProgressOnly || filter.hideLive || filter.hidePlanned;
  });

  const filterMenu = createMemo<Menu>(() => {
    const filter = StateGlobal.homeFilter$();
    return {
      title: "Filter home feed",
      items: [
        new MenuItemToggle({
          name: "Hide watched",
          description: "Only show videos you haven't finished",
          isSelected: filter.hideWatched,
          onToggle: (v) => StateGlobal.setHomeFilter({ ...filter, hideWatched: v }),
        }),
        new MenuItemToggle({
          name: "In progress",
          description: "Only show videos you started watching",
          isSelected: filter.inProgressOnly,
          onToggle: (v) => StateGlobal.setHomeFilter({ ...filter, inProgressOnly: v }),
        }),
        new MenuItemToggle({
          name: "Hide live",
          description: "Hide live streams and premieres",
          isSelected: filter.hideLive,
          onToggle: (v) => StateGlobal.setHomeFilter({ ...filter, hideLive: v }),
        }),
        new MenuItemToggle({
          name: "Hide planned",
          description: "Hide scheduled and upcoming videos",
          isSelected: filter.hidePlanned,
          onToggle: (v) => StateGlobal.setHomeFilter({ ...filter, hidePlanned: v }),
        }),
      ]
    };
  });

  createEffect(() => {
    const pager = homePager();
    const filter = StateGlobal.homeFilter$();
    if(pager)
      pager.setFilter(buildHomeFilter(filter));
  });

  let scrollContainerRef: HTMLDivElement | undefined;
  return (
    <div class={styles.container}>
        <NavigationBar isRoot={true} castGroupIndex={3} childrenAfter={
          <>
          <IconButton
            icon={iconRefresh}
            variant="none"
            shape="circle"
            width="30px"
            height="30px"
            iconInset="0px"
            style={{ "margin-left": "24px" }}
            onClick={() => {
              StateGlobal.reloadHome();
            }}
            focusableOpts={{
              groupId: 'nav-bar',
              groupIndices: [1],
              groupType: 'horizontal',
              onPress: () => StateGlobal.reloadHome(),
            }}
          />
          <div style={{ position: "relative", "margin-left": "24px" }}>
            <IconButton
              icon={iconFilters}
              variant="none"
              shape="circle"
              width="30px"
              height="30px"
              iconInset="0px"
              onClick={(e) => {
                filterAnchor.setElement(e.currentTarget as HTMLElement);
                setShowFilterMenu(true);
              }}
              focusableOpts={{
                groupId: 'nav-bar',
                groupIndices: [2],
                groupType: 'horizontal',
                onPress: (el) => {
                  filterAnchor.setElement(el);
                  setShowFilterMenu(true);
                },
              }}
            />
            <Show when={hasActiveFilter$()}>
              <div style={{ position: "absolute", right: "0px", top: "0px", width: "8px", height: "8px", "border-radius": "50%", background: "#019BE7", "pointer-events": "none" }} />
            </Show>
          </div>
          </>
        } />
        <Show when={homePager.state == 'ready'}>
          <Show when={homePager() && homePager()!.data.length > 0}>
            <ScrollContainer ref={scrollContainerRef}>
              <ContentGrid pager={homePager()} outerContainerRef={scrollContainerRef} openChannelButton={true} />
            </ScrollContainer>
          </Show>
          <Show when={homePager() && homePager()!.data.length == 0}>
            <EmptyContentView icon={iconHome} title='No home results' description='Install, configure, or enable more sources' actions={[
              {
                icon: iconSources,
                title: "Go to Sources",
                action: ()=>nav("/web/sources")
              }
            ]} />
          </Show>
        </Show>
        <Portal>
          <SettingsMenu menu={filterMenu()} show={showFilterMenu$()} anchor={filterAnchor} onHide={()=>setShowFilterMenu(false)} />
        </Portal>
    </div>
  );
};

export default HomePage;
