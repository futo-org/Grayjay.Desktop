import { createSignal, type Component, Show, Switch, Match, createResource, createEffect, onMount, onCleanup, batch, JSX, createMemo, on, For, Accessor } from 'solid-js';

import styles from './index.module.css';
import SideBarButton from '../SideBarButton';
import { NavigateOptions, useLocation, useNavigate } from '@solidjs/router';
import { useVideo, VideoState } from '../../../contexts/VideoProvider';
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
import iconWatchLater from '../../../assets/icons/icon24_watch_later.svg';
import iconBlocked from '../../../assets/icons/icon24_hide.svg';
import iconSettings from '../../../assets/icons/ic_settings_color.svg';
import iconBuy from '../../../assets/icons/ic_buy.svg';
import iconLink from '../../../assets/icons/icon_link.svg';
import iconSources from '../../../assets/icons/ic_circles.svg';
import iconChevronDown from '../../../assets/icons/icon16_chevron_down.svg';
import iconPlus from '../../../assets/icons/icon24_add.svg';
import iconExitToApp from '../../../assets/icons/icon_exit_to_app.svg';
import iconFitScreen from '../../../assets/icons/fit_screen_24dp_E3E3E3_FILL0_wght300_GRAD0_opsz24.svg';
import ScrollContainer from '../../containers/ScrollContainer';
import { SubscriptionsBackend } from '../../../backend/SubscriptionsBackend';
import SideBarCreator from '../SideBarCreator';
import UIOverlay from '../../../state/UIOverlay';
import { WindowBackend } from '../../../backend/WindowBackend';
import FlexibleArrayList from '../../containers/FlexibleArrayList';
import StateGlobal from '../../../state/StateGlobal';
import { createResourceDefault } from '../../../utility';
import { LocalBackend } from '../../../backend/LocalBackend';
import { Portal } from 'solid-js/web';
import { Direction, FocusableOptions } from '../../../nav';
import { focusScope } from '../../../focusScope'; void focusScope;
import { focusable } from "../../../focusable"; void focusable;
import { useFocus } from '../../../FocusProvider';

export interface SideBarProps {
  alwaysMinimized?: boolean;
  classList?: { [k: string]: boolean | undefined; };
  style?: JSX.CSSProperties;
  onNavigate?: (path: string) => void;
  onMoreOpened?: () => void;
  onMoreClosed?: () => void;
};

const SideBar: Component<SideBarProps> = (props: SideBarProps) => {
  const focus = useFocus();
  const video = useVideo();
  const location = useLocation();
  const navigate = useNavigate();
  const options: Partial<NavigateOptions> = { 
    replace: true
  };

  const defaultCanToggleCollapse = props.alwaysMinimized === true ? false : true;
  const [canToggleCollapse, setCanToggleCollapse] = createSignal(defaultCanToggleCollapse);
  const [collapsed, setCollapsed] = createSignal(props.alwaysMinimized === true);
  let wasAutoCollapsed = false;
  const handleCollapse = () => {
    setCollapsed(!collapsed());
  };
  const [sidebarFocused$, setSidebarFocused] = createSignal(false);
  const [suppressAutoExpand$, setSuppressAutoExpand] = createSignal(false);
  const inputSource = createMemo(() => focus?.lastInputSource?.() ?? 'pointer');
  const canAutoExpand = createMemo(() => {
    return (
      !suppressAutoExpand$() &&
      !canToggleCollapse() &&
      collapsed() &&
      inputSource() !== 'pointer' &&
      sidebarFocused$()
    );
  });

  const isCollapsed = createMemo(() => {
    const base = collapsed();
    return canAutoExpand() ? false : base;
  });

  const autoExpanded = createMemo(() => canAutoExpand());
  const isLayoutCollapsed = createMemo(() => collapsed());
  const isVisualCollapsed = createMemo(() => collapsed() && !autoExpanded());

  const handleBackToHideAutoExpand = () => {
    setSuppressAutoExpand(true);
    return true;
  };

  createEffect(on(() => location.pathname, () => setSuppressAutoExpand(false)));

  createEffect(() =>
    console.info("sidebar collapse", {
      layoutCollapsed: isLayoutCollapsed(),
      visualCollapsed: isVisualCollapsed(),
      autoExpanded: autoExpanded(),
      sidebarFocused: sidebarFocused$(),
      collapsed: collapsed(),
    })
  );

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

  type ButtonItem = {
    icon?: string;
    name: string;
    getSelected: Accessor<boolean>;
    path?: string;
    action?: () => any;
    onRightClick?: () => void;
    autoPressOnFocus?: boolean;
    style?: JSX.CSSProperties;
    styleText?: JSX.CSSProperties;
  };
  
  const homeBtn: ButtonItem = { icon: home, name: 'Home', path: '/web/home', getSelected: createMemo(() => location.pathname === '/web/home' || location.pathname === '/web/index.html'), autoPressOnFocus: false };
  const subscriptionsBtn: ButtonItem = { icon: subscriptions, name: 'Subscriptions', path: '/web/subscriptions', getSelected: createMemo(() => location.pathname === '/web/subscriptions'), autoPressOnFocus: false };
  const creatorsBtn: ButtonItem = { icon: creators, name: 'Creators', path: '/web/creators', getSelected: createMemo(() => location.pathname === '/web/creators'), autoPressOnFocus: false };
  const playlistsBtn: ButtonItem = { icon: playlists, name: 'Playlists', path: '/web/playlists', getSelected: createMemo(() => location.pathname === '/web/playlists'), autoPressOnFocus: false };
  const watchLaterBtn: ButtonItem = { icon: iconWatchLater, name: 'Watch Later', path: '/web/watchLater', getSelected: createMemo(() => location.pathname === '/web/watchLater'), autoPressOnFocus: false };
  const sourcesBtn: ButtonItem = { icon: iconSources, name: 'Sources', path: '/web/sources', getSelected: createMemo(() => location.pathname === '/web/sources'), autoPressOnFocus: false };
  const downloadsBtn: ButtonItem = { icon: download, name: 'Downloads', path: '/web/downloads', getSelected: createMemo(() => location.pathname === '/web/downloads'), autoPressOnFocus: false };
  const historyBtn: ButtonItem = { icon: history, name: 'History', path: '/web/history', getSelected: createMemo(() => location.pathname === '/web/history'), autoPressOnFocus: false };
  const syncBtn: ButtonItem = { icon: iconSync, name: 'Sync', path: '/web/sync', getSelected: createMemo(() => location.pathname === '/web/sync'), autoPressOnFocus: false };
  const blockedBtn: ButtonItem = { icon: iconBlocked, name: 'Blocked', path: '/web/blocked', getSelected: createMemo(() => location.pathname === '/web/blocked'), autoPressOnFocus: false };
  const newWindowBtn: ButtonItem = { icon: iconPlus, name: 'New Window', action: () => WindowBackend.startWindow(), getSelected: createMemo(() => false) };
  const closeWindowBtn: ButtonItem = { icon: iconExitToApp, name: 'Close', action: () => WindowBackend.closeWindow(), getSelected: createMemo(() => false) };
  const maximizeVideoBtn: ButtonItem = { icon: iconFitScreen, name: 'Expand Video', action: () => video?.actions.setState(VideoState.Maximized), getSelected: createMemo(() => false) };
  const delayBtn: ButtonItem = { icon: iconPlus, name: 'Delay', action: () => { WindowBackend.echo('test'); WindowBackend.delay(10000); }, getSelected: createMemo(() => false) };
  const developerBtn: ButtonItem = { icon: iconLink, name: 'Developer', path: '/Developer/Index', getSelected: createMemo(() => location.pathname === '/Developer/Index'), onRightClick: () => LocalBackend.open(`http://${window.location.host}/Developer/Index`) };
  
  const topButtons$ = createMemo(() => {
    let list: ButtonItem[] = [homeBtn, subscriptionsBtn, creatorsBtn, playlistsBtn];
  
    if (video?.watchLater()?.length) {
      list.push(watchLaterBtn);
    }
  
    list = list.concat([sourcesBtn, downloadsBtn, historyBtn, syncBtn, blockedBtn]);
    if (focus?.isControllerMode() === true && video?.state() === VideoState.Minimized) {
      list.push(maximizeVideoBtn);
    }

    if (focus?.isControllerMode() !== true) {
      list.push(newWindowBtn);
    } else {
      list.push(closeWindowBtn);
    }
  
    if (devClicked$() > 5) {
      list.push(delayBtn);
    }
  
    if (StateGlobal.isDeveloper$()) {
      list.push(developerBtn);
    }
  
    return list;
  });

  const [visibleTopButtonCount$, setVisibleTopButtonCount] = createSignal<number>(0);
  const [moreTopButtonCount$, setMoreTopButtonCount] = createSignal<number>(0);
  const [remainingSpace$, setRemainingSpace] = createSignal<number>(0);
  const [topButtonListHeight$, setTopButtonListHeight] = createSignal<number>(0);
  
  const buyBtn: ButtonItem = { 
    icon: iconBuy,
    name: 'Buy Grayjay', 
    path: '/web/buy', 
    getSelected: createMemo(() => location.pathname === '/web/buy')
  };
  const settingsBtn: ButtonItem = { icon: iconSettings, name: 'Settings', action: () => UIOverlay.overlaySettings(), getSelected: createMemo(() => location.pathname === '/web/settings') };

  const bottomButtons$ = createMemo(() => {
    const list: ButtonItem[] = [];
  
    if (!StateGlobal.didPurchase$()) {
      list.push(buyBtn);
    }
  
    list.push(settingsBtn);
  
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
        setCanToggleCollapse(defaultCanToggleCollapse);

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

        //console.log({topButtonsRootHeight, remainingSpace, innerHeight: window.innerHeight, bottomButtonCount, totalBottomButtonHeight, availableSideBarTopHeight, availableSidebarTopButtonsHeight, topButtonCount, topButtonsVisible});
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
  createEffect(on(topButtons$, () => handleResize()));
  createEffect(on(bottomButtons$, () => handleResize()));

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
  };

  const globalFocus = () => {
    console.info("sidebar globalFocus");
    setSidebarFocused(true);
  };
  const globalBlur = () => {
    console.info("sidebar globalBlur");
    setSidebarFocused(false);
    setSuppressAutoExpand(false);
  };

  return (
    <div
      class={styles.sidebar}
      style={props.style}
      classList={{
        [styles.collapsed]: isLayoutCollapsed(),
        [styles.autoExpanded]: autoExpanded(),
        ...props.classList,
      }}
    >
      <div class={styles.content}>
        <div class={styles.buttonList}>
          <Show when={canToggleCollapse()}>
            <div class={styles.containerCollapse}>
              <img
                src={isCollapsed() ? ic_sidebarOpen : ic_sidebarClose}
                class={styles.collapse}
                alt=""
                role="button"
                use:focusable={{ 
                  navAnchor: { x: 'left', y: 'center' },
                  groupId: 'sidebar',
                  groupIndices: [0],
                  groupType: 'vertical',
                  groupEscapeDirs: ['right'],
                  groupEscapeTo: { right: ['sources'] },
                  groupRememberLast: true,
                  groupExitDirs: ["right"],
                  onPress: handleCollapse,
                  onBack: handleBackToHideAutoExpand
                }}
                onClick={handleCollapse}
                onFocus={globalFocus}
                onBlur={globalBlur}
              />
            </div>
          </Show>
          <div class={styles.grayjay} oncontextmenu={()=>setDevClicked(devClicked$() + 1)}>
            <img src={grayjay} />
            <Show when={!isCollapsed()}>
              <div style="font-size: 20px; top: 2px; left: 60px; position: absolute;">
              Grayjay
              </div>
            </Show>
            <Show when={!isCollapsed()}>
              <div style="font-size: 12px; top: 25px; left: 60px; position: absolute;">
                Alpha
              </div>
            </Show>
            <Show when={isCollapsed()}>
              <div style="font-size: 12px; top: 45px; left: 0px; position: absolute; width: 50px; text-align: center;">
                Alpha
              </div>
            </Show>
          </div>
          <For each={topButtons$().slice(0, visibleTopButtonCount$())}>
            {(btn, i) => {
              const press = () => btn.action ? btn.action() : navigateTo(btn.path!, options);
              return (
                <SideBarButton
                  collapsed={isCollapsed()}
                  icon={btn.icon}
                  name={btn.name}
                  selected={btn.getSelected()}
                  onClick={press}
                  onRightClick={btn.onRightClick}
                  focusableOpts={{
                    navAnchor: { x: 'left', y: 'center' },
                    groupId: 'sidebar',
                    groupType: 'vertical',
                    groupIndices: [i() + 1],
                    groupEscapeDirs: ['right'],
                    groupRememberLast: true,
                    onPress: () => press(),
                    autoPressOnFocus: btn.autoPressOnFocus,
                    groupEscapeTo: { right: ['sources'] },
                    groupExitDirs: ["right"],
                    onBack: handleBackToHideAutoExpand,
                  }}
                  onFocus={globalFocus}
                  onBlur={globalBlur}
                  style={btn.style}
                  styleText={btn.styleText}
                />
              );
            }}
          </For>
          <Show when={moreTopButtonCount$() > 0}>
            <SideBarButton
              collapsed={isCollapsed()}
              icon={ic_more}
              name={"More"}
              selected={false}
              onClick={() => { props?.onMoreOpened?.(); setMoreOverlayVisible(true); }}
              focusableOpts={{
                navAnchor: { x: 'left', y: 'center' },
                groupId: 'sidebar',
                groupType: 'vertical',
                groupIndices: [visibleTopButtonCount$() + 1],
                groupEscapeDirs: ['right'],
                groupRememberLast: true,
                groupEscapeTo: { right: ['sources'] },
                groupExitDirs: ["right"],
                onPress: () => {
                  props?.onMoreOpened?.(); 
                  setMoreOverlayVisible(true);
                },
                onBack: handleBackToHideAutoExpand
              }}
              onFocus={globalFocus}
              onBlur={globalBlur}
            />
          </Show>
        </div>
        <Show when={!isCollapsed() && subscriptions$()?.length && remainingSpace$() > 200 && focus?.isControllerMode() !== true} fallback={<div style="flex-grow:1"></div>}>
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
          <For each={bottomButtons$()}>
            {(btn, i) => {
              const press = () => btn.action ? btn.action() : navigateTo(btn.path!, options);
              return (
                <SideBarButton
                  collapsed={isCollapsed()}
                  icon={btn.icon}
                  name={btn.name}
                  selected={btn.getSelected()}
                  onClick={press}
                  focusableOpts={{
                    navAnchor: { x: 'left', y: 'center' },
                    groupId: 'sidebar',
                    groupType: 'vertical',
                    groupEscapeDirs: ['right'],
                    groupIndices: [visibleTopButtonCount$() + 1 + (moreTopButtonCount$() > 0 ? 1 : 0) + i()],
                    groupRememberLast: true,
                    groupEscapeTo: { right: ['sources'] },
                    groupExitDirs: ["right"],
                    onPress: press,
                    onBack: handleBackToHideAutoExpand
                  }}
                  onFocus={globalFocus}
                  onBlur={globalBlur}
                  style={btn.style}
                  styleText={btn.styleText}
                />
              );
            }}
          </For>
        </div>
        <Portal>
          <Show when={moreTopButtonCount$() > 0 && moreOverlayVisible$()}>
            <div style="height: 100%; width: 100%; position: absolute; top: 0px; left: 0px; background-color: #0000009e; z-index: 3" onClick={(ev) => {
              props?.onMoreClosed?.();
              setMoreOverlayVisible(false);
              ev.preventDefault();
              ev.stopPropagation();
            }} onMouseMove={(ev) => {
              ev.preventDefault();
              ev.stopPropagation();
            }} use:focusScope={{
              initialMode: 'trap'
            }}>
              <div style="background-color: #141414; width: 260px; height: calc(100% - 20px); border-right: #2a2a2a 1px solid; padding: 10px; display: flex; flex-direction: column; align-items: center; gap: 6px;">
                <For each={topButtons$().slice(visibleTopButtonCount$(), visibleTopButtonCount$() + moreTopButtonCount$())}>
                  {(btn, i) => {
                    const press = () => {
                      props?.onMoreClosed?.();
                      setMoreOverlayVisible(false);
                      btn.action ? btn.action() : navigateTo(btn.path!, options);
                    };
                    return (
                      <SideBarButton
                        collapsed={false}
                        icon={btn.icon}
                        name={btn.name}
                        selected={btn.getSelected()}
                        onClick={press}
                        onRightClick={btn.onRightClick}
                        focusableOpts={{
                          groupId: 'sidebar-overlay',
                          groupType: 'vertical',
                          groupIndices: [i()],                        
                          onPress: press,
                          onBack: () => {
                            if (moreOverlayVisible$()) {
                              props?.onMoreClosed?.(); 
                              setMoreOverlayVisible(false); 
                              return true;
                            }
                            return false;
                          }
                        }}
                        data-more-first={i() === 0 ? "1" : undefined}
                        onFocus={globalFocus}
                        onBlur={globalBlur}
                        style={btn.style}
                        styleText={btn.styleText}
                      />
                    );
                  }}
                </For>
              </div>
            </div>
          </Show>
        </Portal>
      </div>
    </div>
  );
};

export default SideBar;
