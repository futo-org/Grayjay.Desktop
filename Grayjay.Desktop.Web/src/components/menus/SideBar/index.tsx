import { createSignal, type Component, Show, Switch, Match, createResource, createEffect, onMount, onCleanup, batch, JSX } from 'solid-js';

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
import VirtualFlexibleList from '../../containers/VirtualFlexibleList';
import VirtualFlexibleArrayList from '../../containers/VirtualFlexibleArrayList';
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

export interface SideBarProps {
  alwaysMinimized?: boolean;
  classList?: { [k: string]: boolean | undefined; };
  style?: JSX.CSSProperties;
  onNavigate?: (path: string) => void;
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

  const [subscriptions$] = createResourceDefault(async () => [], async () => await SubscriptionsBackend.subscriptions());

  let scrollContainerRef: HTMLDivElement | undefined;

  const subs = subscriptions$();
  createEffect(() => {
    const subs = subscriptions$();
    console.log("Subs loaded: " + !!subs);
  });

  const handleResize = () => {
    if (props.alwaysMinimized === true) {
      return;
    }

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
        <div class={styles.grayjay}>
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
        <SideBarButton collapsed={collapsed()} onClick={() => navigateTo("/web/home", options)} icon={home} name="Home" selected={location.pathname === "/web/home" || location.pathname === "/web/index.html"} />
        <SideBarButton collapsed={collapsed()} onClick={() => navigateTo("/web/subscriptions", options)} icon={subscriptions} name="Subscriptions" selected={location.pathname === "/web/subscriptions"} />
        <SideBarButton collapsed={collapsed()} onClick={() => navigateTo("/web/creators", options)} icon={creators} name="Creators" selected={location.pathname === "/web/creators"} />
        <SideBarButton collapsed={collapsed()} onClick={() => navigateTo("/web/playlists", options)} icon={playlists} name="Playlists" selected={location.pathname === "/web/playlists"} />
        <Show when={video?.watchLater()?.length}>
          <SideBarButton collapsed={collapsed()} onClick={() => navigateTo("/web/watchLater", options)} icon={iconWatchLater} name="Watch Later" selected={location.pathname === "/web/watchLater"} />
        </Show>
        <SideBarButton collapsed={collapsed()} onClick={() => navigateTo("/web/sources", options)} icon={iconSources} name="Sources" selected={location.pathname === "/web/sources"} />
        <SideBarButton collapsed={collapsed()} onClick={() => navigateTo("/web/downloads", options)} icon={download} name="Downloads" selected={location.pathname === "/web/downloads"} />
        <SideBarButton collapsed={collapsed()} onClick={() => navigateTo("/web/history", options)} icon={history} name="History" selected={location.pathname === "/web/history"} />
        <SideBarButton collapsed={collapsed()} onClick={() => navigateTo("/web/sync", options)} icon={iconSync} name="Sync" selected={location.pathname === "/web/sync"} />
        <SideBarButton collapsed={collapsed()} onClick={() => WindowBackend.startWindow()} icon={iconPlus} name="New Window" selected={false} />
        <Show when={StateGlobal.isDeveloper$()}>
        <SideBarButton collapsed={collapsed()} onClick={() => {
              const host = window.location.host;
              window.location.href = ("http://" + host + "/Developer/Index");
        }} icon={iconLink} name="Developer" selected={location.pathname === "/Developer/Index"} />
        </Show>
      </div>
      <Show when={!collapsed() && subscriptions$()?.length} fallback={<div style="flex-grow:1"></div>}>
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
        <Show when={!StateGlobal.didPurchase$()}>
          <SideBarButton collapsed={collapsed()} onClick={() => navigate("/web/buy", options)} icon={iconBuy} name="Buy Grayjay" selected={location.pathname === "/web/buy"} />
        </Show>
        <SideBarButton collapsed={collapsed()} onClick={() => UIOverlay.overlaySettings()} icon={iconSettings} name="Settings" selected={location.pathname === "/web/settings"} />
      </div>
    </div>
  );
};

export default SideBar;
