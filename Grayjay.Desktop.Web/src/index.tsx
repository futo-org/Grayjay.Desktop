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
import SettingsPage from './pages/Settings';
import OverlayModals from './overlays/OverlayModals';
import OverlayRoot from './overlays/OverlayRoot';
import OverlayCasting from './components/casting/OverlayCasting';
import { CastingProvider } from './contexts/Casting';
import WatchLaterPage from './pages/WatchLater';
import RemotePlaylistPage from './pages/RemotePlaylist';
import SyncPage from './pages/Sync';
import { HandlingBackend } from './backend/HandlingBackend';
import Globals from './globals';
import PostDetailView from './components/contentDetails/PostDetailsView';
import StateWebsocket from './state/StateWebsocket';
import GlobalContextMenu from './components/GlobalContextMenu';
import BuyPage from './pages/BuyPage';
import UIOverlay from './state/UIOverlay';
import ExceptionModel from './backend/exceptions/ExceptionModel';
import { SettingsBackend } from './backend/SettingsBackend';

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

  let mvEvent: ((a:any)=>void) | null = null;
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
            Globals.handleUrl(url, video, navigate);
          });
          break;
        }
      }
    }

    return <div class="root-container">
      <CastingProvider>
        <SideBar />
          <Show when={useVideo()?.state() !== VideoState.Maximized}>
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
    </div>;
  };

  return <>  
    <VideoProvider>
      {renderContent()}
    </VideoProvider>
  </>
};

const DefaultPage=(await SettingsBackend.settings())?.object?.home?.recommendationsHomepage? HomePage: SubscriptionsPage;

render(() => (
  <Router root={App}>
    <Route path="/web/index.html" component={DefaultPage} />
    <Route path="/web" component={DefaultPage} />
    <Route path="/web/home" component={DefaultPage} />
    <Route path="/web/recommended" component={HomePage} />
    <Route path="/web/search" component={SearchPage} />
    <Route path="/web/subscriptions" component={SubscriptionsPage} />
    <Route path="/web/creators" component={CreatorsPage} />
    <Route path="/web/playlists" component={PlaylistsPage} />
    <Route path="/web/playlist" component={PlaylistPage} />
    <Route path="/web/remotePlaylist" component={RemotePlaylistPage} />
    <Route path="/web/watchLater" component={WatchLaterPage} />
    <Route path="/web/sources" component={SourcesPage} />
    <Route path="/web/virtualExample" component={VirtualExamplePage} />
    <Route path="/web/channel" component={ChannelPage} />
    <Route path="/web/downloads" component={DownloadsPage} />
    <Route path="/web/history" component={HistoryPage} />
    <Route path="/web/sync" component={SyncPage} />
    <Route path="/web/details/post" component={PostDetailView} />
    <Route path="/web/buy" component={BuyPage} />
  </Router>
), root!);
