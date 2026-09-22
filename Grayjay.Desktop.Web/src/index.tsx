/* @refresh reload */
import { render } from 'solid-js/web';

import './index.css';
import { Router, Route, RouteSectionProps, useNavigate, Navigator } from '@solidjs/router';
import SideBar from './components/menus/SideBar';
import { Component, Show, children, createSignal, lazy, onCleanup, onMount } from 'solid-js';
import VideoDetailView from './components/contentDetails/VideoDetailView';
import { VideoContextValue, VideoProvider, VideoState, useVideo } from './contexts/VideoProvider';
import SourcesPage from './pages/Sources';
import ChannelPage from './pages/Channel';
import SearchPage from './pages/Search';
import StateGlobal from './state/StateGlobal';
import DownloadsPage from './pages/Downloads';
import HistoryPage from './pages/History';
import OverlayModals from './overlays/OverlayModals';
import OverlayRoot from './overlays/OverlayRoot';
import OverlayCasting from './components/casting/OverlayCasting';
import { CastingProvider } from './contexts/Casting';
import WatchLaterPage from './pages/WatchLater';
import RemotePlaylistPage from './pages/RemotePlaylist';
import SyncPage from './pages/Sync';
import Globals from './globals';
import { WindowBackend } from './backend/WindowBackend';
import PostDetailView from './components/contentDetails/PostDetailsView';
import StateWebsocket from './state/StateWebsocket';
import GlobalContextMenu from './components/GlobalContextMenu';
import BuyPage from './pages/BuyPage';
import LoaderGameExamplePage from './pages/LoaderGameExamplePage';
import { FocusProvider } from './FocusProvider';
import { focusScope } from './focusScope';import ControllerOverlay from './components/ControllerOverlay';
 void focusScope;

const HomePage = lazy(() => import('./pages/Home'));
const SubscriptionsPage = lazy(() => import('./pages/Subscriptions'));
const CreatorsPage = lazy(() => import('./pages/Creators'));
const PlaylistsPage = lazy(() => import('./pages/Playlists'));
const PlaylistPage = lazy(() => import('./pages/Playlist'));
const VirtualExamplePage = lazy(() => import('./pages/VirtualExamplePage'));

const root = document.getElementById('root');

if (import.meta.env.DEV && !(root instanceof HTMLElement)) {
  throw new Error(
    'Root element not found. Did you forget to add it to your index.html? Or maybe the id attribute got misspelled?',
  );
}

root?.addEventListener("click", function(event) {
  console.log("Click", event);
  StateGlobal.onGlobalClick?.invoke(event);
});

// Cmd/Ctrl+click flag lives briefly so the next navigation can consume it.
const CMD_CLICK_FLAG_RESET_MS = 50;
// Small delay so the new window finishes mounting before we dispatch the nav.
const NEW_WINDOW_NAV_DELAY_MS = 200;

let cmdClickResetTimeout: ReturnType<typeof setTimeout> | undefined;
document.addEventListener('click', (e) => {
  const pressed = e.metaKey || e.ctrlKey;
  WindowBackend.markCmdClick(pressed);
  if (cmdClickResetTimeout !== undefined) clearTimeout(cmdClickResetTimeout);
  if (pressed)
    cmdClickResetTimeout = setTimeout(() => WindowBackend.markCmdClick(false), CMD_CLICK_FLAG_RESET_MS);
}, true);

// Router navigations go through history.pushState; we intercept it so a
// Cmd/Ctrl+click followed by navigate() opens in a new window instead.
const _origPushState = history.pushState.bind(history);
history.pushState = function(state: any, title: string, url?: string | URL | null) {
  if (WindowBackend.consumeCmdClick() && url) {
    WindowBackend.openInNewWindow({ route: url.toString() });
    return;
  }
  return _origPushState(state, title, url);
};

var navigate: Navigator | undefined = undefined;
var video: VideoContextValue | undefined = undefined;

StateWebsocket.registerHandlerNew("OpenUrl", (packet)=>{
  const videoNow = video;
  const navigateNow = navigate;
  if(videoNow && navigateNow && packet.payload.url) {
    Globals.handleUrl(packet.payload.url, videoNow, navigateNow, packet.payload.positionSeconds);
  }
}, "main");

const App: Component<RouteSectionProps> = (props) => {
  const [isDropping$, setIsDropping] = createSignal<boolean>();

  function dragOver(ev: any){
    setIsDropping(true);
    if(ev.dataTransfer?.types?.includes("prevent-drag") ?? false)
      setIsDropping(false);
    else
      setIsDropping(true);
    ev.preventDefault();
  }
  function dragLeave(ev: any){
    setIsDropping(false);
  }
  function dragMove(ev: any){
    console.log("Mousemove");
    if(ev.buttons == 0)
      dragLeave(ev);
  }

  onMount(()=> {
    if(root) {
      root.ondragover = dragOver;
    }
  });

  onCleanup(()=> {
    if(root){
      root.ondragover = null;
    }
  })

  const renderContent = () => {
    navigate = useNavigate();
    video = useVideo();

    // New window started by Cmd/Ctrl+click reads the intent left by the parent.
    try {
      const intent = WindowBackend.consumeNavIntent();
      const navigateNow = navigate;
      const videoNow = video;
      if (intent && navigateNow) {
        if (intent.route) {
          const route = intent.route;
          setTimeout(() => navigateNow(route), NEW_WINDOW_NAV_DELAY_MS);
        } else if (intent.url && videoNow) {
          const url = intent.url;
          setTimeout(() => Globals.handleUrl(url, videoNow, navigateNow), NEW_WINDOW_NAV_DELAY_MS);
        }
      }
    } catch (e) {
      console.warn("Failed to restore new-window navigation intent", e);
    }


    function dragDrop(ev: any){
      ev.stopPropagation();
      ev.preventDefault();
      setIsDropping(false);
      console.log(ev);
  
      for(let i = 0; i < ev.dataTransfer.items.length; i++) {
        const item = ev.dataTransfer.items[i];
        if(item.type == "text/uri-list") {
          console.log(item);
          item.getAsString(async function(url: string){
            if (video && navigate)
              Globals.handleUrl(url, video, navigate);
          });
          break;
        }
      }
    }

    return <div class="root-container" use:focusScope={{ id: 'root-container' }}>
      <CastingProvider>
        <SideBar />
          <Show when={useVideo()?.state() !== VideoState.Maximized && useVideo()?.state() !== VideoState.Fullscreen}>
            <div class="root-content">
              {props.children}
            </div>
          </Show>
          <VideoDetailView />
        <OverlayCasting />
      </CastingProvider>
      <OverlayModals />
      <OverlayRoot />
      <Show when={isDropping$()}>
        <div class="droparea" ondragover={dragOver} ondragleave={dragLeave} ondrop={dragDrop} onmousemove={dragMove} onClick={dragLeave} style="z-index:99999999">
          <div class="text">
            Dropping
          </div>
        </div>
      </Show>
      <GlobalContextMenu />
      <div style="position: absolute; bottom: 8px; right: 20px; z-index: 5;">
        <ControllerOverlay />
      </div>
    </div>;
  };

  return <>  
    <VideoProvider>
      <FocusProvider>
        {renderContent()}
      </FocusProvider>
    </VideoProvider>
  </>
};

render(() => (
  <Router root={App}>
    <Route path="/web/index.html" component={HomePage} />
    <Route path="/web" component={HomePage} />
    <Route path="/web/home" component={HomePage} />
    <Route path="/web/search" component={SearchPage} />
    <Route path="/web/subscriptions" component={SubscriptionsPage} />
    <Route path="/web/creators" component={CreatorsPage} />
    <Route path="/web/playlists" component={PlaylistsPage} />
    <Route path="/web/playlist" component={PlaylistPage} />
    <Route path="/web/remotePlaylist" component={RemotePlaylistPage} />
    <Route path="/web/watchLater" component={WatchLaterPage} />
    <Route path="/web/sources" component={SourcesPage} />
    <Route path="/web/virtualExample" component={VirtualExamplePage} />
    <Route path="/web/loaderGame" component={LoaderGameExamplePage} />
    <Route path="/web/channel" component={ChannelPage} />
    <Route path="/web/downloads" component={DownloadsPage} />
    <Route path="/web/history" component={HistoryPage} />
    <Route path="/web/sync" component={SyncPage} />
    <Route path="/web/details/post" component={PostDetailView} />
    <Route path="/web/buy" component={BuyPage} />
  </Router>
), root!);
