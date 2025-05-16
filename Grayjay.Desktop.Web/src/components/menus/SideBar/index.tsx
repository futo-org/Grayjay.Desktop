import { createSignal, type Component, Show, Switch, Match, createResource, createEffect, onMount, onCleanup, batch, JSX, createMemo } from 'solid-js';

import styles from './index.module.css';
import SideBarButton from '../SideBarButton';
import { NavigateOptions, useLocation, useNavigate } from '@solidjs/router';
import { useVideo } from '../../../contexts/VideoProvider';
import grayjay from '../../../assets/grayjay.svg';

import home from '../../../assets/icons/icon_nav_home.svg';
import subscriptions from '../../../assets/icons/icon_nav_subscriptions.svg';
import playlists from '../../../assets/icons/icon_nav_playlists.svg';
import creators from '../../../assets/icons/icon_nav_creators.svg';
import ic_sidebarOpen from '../../../assets/icons/sidebar-open.svg';
import ic_sidebarClose from '../../../assets/icons/sidebar-close.svg';
import ic_more from '../../../assets/icons/icon_button_more.svg';
import history from '../../../assets/icons/icon_nav_history.svg';
import download from '../../../assets/icons/icon24_download.svg';
import iconSync from '../../../assets/icons/ic_sync.svg';
import settings from '../../../assets/icons/icon_nav_settings.svg';
import iconWatchLater from '../../../assets/icons/icon24_watch_later.svg';
import iconSettings from '../../../assets/icons/ic_settings_color.svg';
import iconBuy from '../../../assets/icons/ic_buy.svg';
import iconLink from '../../../assets/icons/icon_link.svg';
import iconSources from '../../../assets/icons/ic_circles.svg';
import iconChevronDown from '../../../assets/icons/icon16_chevron_down.svg';
import iconPlus from '../../../assets/icons/icon24_add.svg'
import ScrollContainer from '../../containers/ScrollContainer';
import { SubscriptionsBackend } from '../../../backend/SubscriptionsBackend';
import SideBarCreator from '../SideBarCreator';
import UIOverlay from '../../../state/UIOverlay';
import { WindowBackend } from '../../../backend/WindowBackend';
import FlexibleArrayList from '../../containers/FlexibleArrayList';
import Globals from '../../../globals';
import StateGlobal from '../../../state/StateGlobal';
import { createResourceDefault } from '../../../utility';
import { LocalBackend } from '../../../backend/LocalBackend';
import { Portal } from 'solid-js/web';

export interface SideBarProps {
  alwaysMinimized?: boolean;
  classList?: { [k: string]: boolean | undefined; };
  style?: JSX.CSSProperties;
  onNavigate?: (path: string) => void;
  onMoreOpened?: () => void;
  onMoreClosed?: () => void;
};

const SideBar: Component<SideBarProps> = (props: SideBarProps) => {
  const video = useVideo();
  const location = useLocation();
  const navigate = useNavigate();
  const options: Partial<NavigateOptions> = { 
    replace: true
  };

  const [canToggleCollapse, setCanToggleCollapse] = createSignal(props.alwaysMinimized === true ? false : true);
  const [collapsed, setCollapsed] = createSignal(props.alwaysMinimized === true);
  let wasAutoCollapsed = false;
  const handleCollapse = () => {
    setCollapsed(!collapsed());
  };

  const [expand$, setExpand] = createSignal(true);
  const [expandType$, setExpandType] = createSignal("subs");

  const [devClicked$, setDevClicked] = createSignal(0);
  const [moreOverlayVisible$, setMoreOverlayVisible] = createSignal(false);
  const [subscriptions$] = createResourceDefault(async () => [], async () => await SubscriptionsBackend.subscriptions());

  let scrollContainerRef: HTMLDivElement | undefined;

  createEffect(() => {
    const subs = subscriptions$();
    console.log("Subs loaded: " + !!subs);
  });

  const topButtons$ = createMemo(() => {
    const list: Array<{
      icon: string,
      name: string,
      selected: boolean,
      path?: string,
      action?: () => void,
      onRightClick?: () => void
    }> = [
      { icon: home,          name: 'Home',          path: '/web/home',          selected: location.pathname === '/web/home' || location.pathname === '/web/index.html' },
      { icon: subscriptions, name: 'Subscriptions',  path: '/web/subscriptions', selected: location.pathname === '/web/subscriptions' },
      { icon: creators,      name: 'Creators',       path: '/web/creators',      selected: location.pathname === '/web/creators' },
      { icon: playlists,     name: 'Playlists',      path: '/web/playlists',     selected: location.pathname === '/web/playlists' },
    ];
  
    if (video?.watchLater()?.length) {
      list.push({
        icon: iconWatchLater,
        name: 'Watch Later',
        path: '/web/watchLater',
        selected: location.pathname === '/web/watchLater'
      });
    }
  
    list.push(
      { icon: iconSources, name: 'Sources',   path: '/web/sources',   selected: location.pathname === '/web/sources' },
      { icon: download,    name: 'Downloads', path: '/web/downloads', selected: location.pathname === '/web/downloads' },
      { icon: history,     name: 'History',   path: '/web/history',   selected: location.pathname === '/web/history' },
      { icon: iconSync,    name: 'Sync',      path: '/web/sync',      selected: location.pathname === '/web/sync' },
      { icon: iconPlus,    name: 'New Window', action: () => WindowBackend.startWindow(), selected: false }
    );
  
    if (devClicked$() > 5) {
      list.push({
        icon: iconPlus,
        name: 'Delay',
        action: () => { WindowBackend.echo('test'); WindowBackend.delay(10000); },
        selected: false
      });
    }
  
    if (StateGlobal.isDeveloper$()) {
      const host = window.location.host;
      list.push({
        icon: iconLink,
        name: 'Developer',
        path: '/Developer/Index',
        selected: location.pathname === '/Developer/Index',
        onRightClick: () => LocalBackend.open(`http://${host}/Developer/Index`)
      });
    }
  
    return list;
  });

  const [visibleTopButtonCount$, setVisibleTopButtonCount] = createSignal<number>(0);
  const [moreTopButtonCount$, setMoreTopButtonCount] = createSignal<number>(0);
  const [remainingSpace$, setRemainingSpace] = createSignal<number>(0);
  const [topButtonListHeight$, setTopButtonListHeight] = createSignal<number>(0);
  
  const bottomButtons$ = createMemo(() => {
    const list: Array<{ icon: string, name: string, selected: boolean, path?: string, action?: () => void }> = [];
  
    if (!StateGlobal.didPurchase$()) {
      list.push({
        icon: iconBuy,
        name: 'Buy Grayjay',
        path: '/web/buy',
        selected: location.pathname === '/web/buy'
      });
    }
  
    list.push({
      icon: iconSettings,
      name: 'Settings',
      action: () => UIOverlay.overlaySettings(),
      selected: location.pathname === '/web/settings'
    });
  
    return list;
  });

  const useMoreButton = true;
  const handleResize = () => {
    batch(() => {
      if (window.innerWidth < 1200) {
        if (!collapsed()) {
          setCollapsed(true);
          wasAutoCollapsed = true;
        }
        setCanToggleCollapse(false);
      } else {
        setCanToggleCollapse(true);

        if (wasAutoCollapsed) {
          setCollapsed(false);
          wasAutoCollapsed = false;
        }
      }

      const bottomButtonCount = bottomButtons$().length;
      const totalBottomButtonHeight = 44 /* button height */ * bottomButtonCount + 4 /* gap */ * (bottomButtonCount - 1) + 10 /* margin top */ + 1 /* divider */;
      const availableSideBarTopHeight = window.innerHeight - 10 /* margin top */ - 10 /* margin bottom */ - totalBottomButtonHeight;
      const topButtonsRootHeight = (canToggleCollapse() ? (56 + 16 /* top margin */ + 4 /* bottom margin */ + 6 /* gap */) : 0) + (48 + 8 /* bottom margin */ + 6 /* gap */);
      const availableSidebarTopButtonsHeight = availableSideBarTopHeight - topButtonsRootHeight;
      const topButtonCount = topButtons$().length;
      const topButtonsVisible = Math.min(Math.floor((availableSidebarTopButtonsHeight + 6 /* gap */) / (44 + 6 /* gap */)), topButtonCount);

      if (useMoreButton) {
        const topButtonListHeight = topButtonsVisible * 44 + (topButtonsVisible - 1) * 6 /* gap */;
        setTopButtonListHeight(topButtonListHeight);
        const remainingSpace = Math.max(availableSidebarTopButtonsHeight - topButtonListHeight, 0);
        setRemainingSpace(remainingSpace);

        if (topButtonsVisible == topButtonCount) {
          //All buttons visible
          setVisibleTopButtonCount(topButtonsVisible);
          setMoreTopButtonCount(0);
          props?.onMoreClosed?.();
          setMoreOverlayVisible(false);
        } else {
          //Not all buttons visible, no potential for showing subscriptions
          setVisibleTopButtonCount(topButtonsVisible - 1);
          setMoreTopButtonCount(topButtonCount - (topButtonsVisible - 1));
        }

        console.log({topButtonsRootHeight, remainingSpace, innerHeight: window.innerHeight, bottomButtonCount, totalBottomButtonHeight, availableSideBarTopHeight, availableSidebarTopButtonsHeight, topButtonCount, topButtonsVisible});
      } else {
        const topButtonListHeight = topButtonCount * 44 + (topButtonCount - 1) * 6 /* gap */;
        setTopButtonListHeight(topButtonListHeight);

        const remainingSpace = Math.max(availableSidebarTopButtonsHeight - topButtonListHeight, 0);
        setRemainingSpace(remainingSpace);

        setVisibleTopButtonCount(topButtonCount);
        setMoreTopButtonCount(0);

        console.log({topButtonsRootHeight, remainingSpace, innerHeight: window.innerHeight, bottomButtonCount, totalBottomButtonHeight, availableSideBarTopHeight, availableSidebarTopButtonsHeight, topButtonCount, topButtonsVisible});
      }
    });
  };

  onMount(() => {
    window.addEventListener('resize', handleResize);
    handleResize();
  });

  onCleanup(() => {
    window.removeEventListener('resize', handleResize);
  });

  const navigateTo = (to: string, options: Partial<NavigateOptions>) => {
    props.onNavigate?.(to); 
    navigate(to, options);
  }

  return (
    <div class={styles.sidebar} style={props.style} classList={{ [styles.collapsed]: collapsed(), ... props.classList }}>
      <div class={styles.buttonList}>
        <div class={styles.containerCollapse}>
          <Show when={canToggleCollapse()}>
          <img src={collapsed() ? ic_sidebarOpen : ic_sidebarClose} class={styles.collapse} onClick={handleCollapse} />
          </Show>
        </div>
        <div class={styles.grayjay} oncontextmenu={()=>setDevClicked(devClicked$() + 1)}>
          <img src={grayjay} />
          <Show when={!collapsed()}>
            <div style="font-size: 20px; top: 2px; left: 60px; position: absolute;">
            Grayjay
            </div>
          </Show>
          <Show when={!collapsed()}>
            <div style="font-size: 12px; top: 25px; left: 60px; position: absolute;">
              Alpha
            </div>
          </Show>
          <Show when={collapsed()}>
            <div style="font-size: 12px; top: 45px; left: 0px; position: absolute; width: 50px; text-align: center;">
              Alpha
            </div>
          </Show>
        </div>
        {topButtons$().slice(0, visibleTopButtonCount$()).map(btn => (
          <SideBarButton
            collapsed={collapsed()}
            icon={btn.icon}
            name={btn.name}
            selected={btn.selected}
            onClick={() => btn.action ? btn.action() : navigateTo(btn.path!, options)}
            onRightClick={btn.onRightClick}
          />
        ))}
        <Show when={moreTopButtonCount$() > 0}>
          <SideBarButton
              collapsed={collapsed()}
              icon={ic_more}
              name={"More"}
              selected={false}
              onClick={() => {
                props?.onMoreOpened?.();
                setMoreOverlayVisible(true);
              }}
            />
        </Show>
      </div>
      <Show when={!collapsed() && subscriptions$()?.length && remainingSpace$() > 200} fallback={<div style="flex-grow:1"></div>}>
        <div class={styles.buttonListFill}>
          <div classList={{[styles.expandHeader]: true, [styles.expanded]: expand$()}} onClick={()=>setExpand(!expand$())}>
              Subscriptions
              <div class={styles.toggle}>
                  <img src={iconChevronDown} />
              </div>
          </div>
          <Show when={expand$()}>
            <div class={styles.expandItems}>
              <Switch>
                <Match when={expandType$() == "subs"}>
                  <ScrollContainer ref={scrollContainerRef}>
                    <FlexibleArrayList outerContainerRef={scrollContainerRef} 
                      items={subscriptions$()}
                      builder={(_, item$) => 
                        <SideBarCreator onClick={() => {
                          const author = item$()?.channel;
                          if (!author) {
                            return;
                          }
                          navigate("/web/channel?url=" + encodeURIComponent(author!.url), { state: { author } });
                        }} icon={item$()?.channel?.thumbnail} name={item$()?.channel?.name} selected={false} />
                      } />
                  </ScrollContainer>
                </Match>
              </Switch>
            </div>
          </Show>
        </div>
      </Show>
      <div class={styles.buttonListBottom}>
        {bottomButtons$().map(btn => (
          <SideBarButton
            collapsed={collapsed()}
            icon={btn.icon}
            name={btn.name}
            selected={btn.selected}
            onClick={() => btn.action ? btn.action() : navigateTo(btn.path!, options)}
          />
        ))}
      </div>
      <Portal>
        <Show when={moreTopButtonCount$() > 0 && moreOverlayVisible$()}>
          <div style="height: 100%; width: 100%; position: absolute; top: 0px; left: 0px; background-color: #0000009e; z-index: 2" onClick={(ev) => {
            props?.onMoreClosed?.();
            setMoreOverlayVisible(false);
            ev.preventDefault();
            ev.stopPropagation();
          }} onMouseMove={(ev) => {
            ev.preventDefault();
            ev.stopPropagation();
          }}>
            <div style="background-color: var(--black-color-1); width: 200px; height: calc(100% - 20px); border-right: #2a2a2a 1px solid; padding: 10px; display: flex;
    flex-direction: column; align-items: center; gap: 6px;">
              {topButtons$().slice(visibleTopButtonCount$(), visibleTopButtonCount$() + moreTopButtonCount$()).map(btn => (
                  <SideBarButton
                    collapsed={false}
                    icon={btn.icon}
                    name={btn.name}
                    selected={btn.selected}
                    onClick={() => btn.action ? btn.action() : navigateTo(btn.path!, options)}
                    onRightClick={btn.onRightClick}
                  />
                ))}
            </div>
          </div>

        </Show>
      </Portal>
    </div>
  );
};

export default SideBar;
