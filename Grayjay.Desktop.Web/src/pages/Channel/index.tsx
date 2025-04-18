import { createResource, type Component, Show, createMemo, createSignal, Switch, Match, createEffect, untrack } from 'solid-js';
import { createResourceDefault, toHumanNumber } from '../../utility';
import { ChannelBackend } from '../../backend/ChannelBackend';
import { useLocation, useSearchParams } from '@solidjs/router';
import { Event1 } from "../../utility/Event";
import ContentGrid from '../../components/containers/ContentGrid';
import LoaderContainer from '../../components/basics/loaders/LoaderContainer';
import LoaderGrid from '../../components/basics/loaders/LoaderGrid';
import SkeletonDiv from '../../components/basics/loaders/SkeletonDiv';

import styles from './index.module.css';
import StickyShrinkOnScrollContainer from '../../components/containers/StickyShrinkOnScrollContainer';
import { useShrinkProgress } from '../../contexts/ShrinkProgress';
import TransparentIconButton from '../../components/buttons/TransparentIconButton';
import { easeOutExpo } from '../../animation';
import ButtonGroup from '../../components/ButtonGroup';
import SubscribeButton from '../../components/buttons/SubscribeButton';

import more from '../../assets/icons/more_horiz_FILL0_wght400_GRAD0_opsz24.svg';
import NavigationBar from '../../components/topbars/NavigationBar';
import SettingsMenu, { Menu, MenuItem, ShowEvent } from '../../components/menus/Overlays/SettingsMenu';
import ScrollContainer from '../../components/containers/ScrollContainer';
import ic_search from '../../assets/icons/search.svg';
import InputText from '../../components/basics/inputs/InputText';
import { Pager } from '../../backend/models/pagers/Pager';
import { IPlatformContent } from '../../backend/models/content/IPlatformContent';
import Anchor, { AnchorStyle } from '../../utility/Anchor';
import StateGlobal from '../../state/StateGlobal';
import { SubscriptionsBackend } from '../../backend/SubscriptionsBackend';
import { Menus } from '../../Menus';
import { Portal } from 'solid-js/web';
import UIOverlay from '../../state/UIOverlay';

interface ChannelTopBarProps {
  authorUrl?: string;
  bannerUrl?: string;
  thumbnailUrl?: string;
  name?: string;
  metadata?: string;
  description?: string;
  activeTab?: string;
  onActiveTabChanged?: (tab: string) => void;
}

const ChannelTopBar: Component<ChannelTopBarProps> = (props) => {
  let moreElement: HTMLDivElement | undefined;

  const [subscriptionMenu$, setSubscriptionMenu] = createSignal<{
    menu: Menu,
    subscription?: ISubscription,
    subscriptionSettings?: ISubscriptionSettings
  }>({ 
    menu: { title: "", items: []  } 
  });
  const [showSettings$, setShowSettings] = createSignal(false);
  const anchor = new Anchor(null, showSettings$, AnchorStyle.BottomRight);
  const showSubscriptionSettings = async (el: HTMLElement, subscription: ISubscription | undefined) => {
    if (!subscription) {
      return;
    }

    const sourceState = StateGlobal.getSourceState(subscription.channel.id.pluginID);
    const subscriptionSettings: ISubscriptionSettings = await SubscriptionsBackend.subscriptionSettings(subscription.channel.url);
    anchor.setElement(el);
    setSubscriptionMenu(Menus.getSubscriptionMenu(subscription, subscriptionSettings, sourceState));
    setShowSettings(true);
  };

  const hideSubscriptionSettings = () => {
    const subscription = subscriptionMenu$().subscription;
    if (!subscription) {
      return;
    }

    const subscriptionSettings = subscriptionMenu$().subscriptionSettings;
    if (!subscriptionSettings) {
      return;
    }

    console.log("subscriptionSettings", subscriptionSettings);

    SubscriptionsBackend.updateSubscriptionSettings(subscription.channel.url, subscriptionSettings);
    setShowSettings(false);
  };
  
  const progress = useShrinkProgress();
  const p = createMemo(() => easeOutExpo(progress()));
  const [subscription$, subscriptionResource] = createResourceDefault(props.authorUrl, async (u) => await SubscriptionsBackend.subscription(u));

  return (
    <div class={styles.containerTopBar}>
      <div class={styles.containerTopBarImage} style={{opacity: 1 - p()}}>
        <div class={styles.containerTopBarGradient}></div>
        <Show when={props.bannerUrl && props.bannerUrl.length}>
          <img
            src={props.bannerUrl} 
            alt="Channel banner"
          />
        </Show>
      </div>
      <div class={styles.containerTopBarControls}>
        <NavigationBar />
        <div style="flex-grow: 1"></div>
        <div class={styles.containerActions} style={{opacity: 1 - p()}}>
          <div class={styles.containerCreator}>
            <Show when={props.thumbnailUrl && props.thumbnailUrl.length}>
              <img class={styles.imageCreator} src={props.thumbnailUrl} referrerPolicy='no-referrer' />
            </Show>
            <div class={styles.creatorName}>{props.name}</div>
            <div class={styles.creatorMetadata}>{props.metadata}</div>
            <Show when={props.description}>
              <div class={styles.creatorDescription}>{props.description}</div>
            </Show>
          </div>
          <div style="flex-grow: 1"></div>
          <div class={styles.containerChannelButtons}>
            <Show when={subscription$()}>
              <TransparentIconButton ref={moreElement} icon={more} style={{"width": "42px", "height": "42px"}} onClick={(ev) => showSubscriptionSettings(ev.target as HTMLElement, subscription$()!)} />
            </Show>
            <SubscribeButton small={true} author={props.authorUrl} style={{"width": "110px"}} onIsSubscribedChanged={() => subscriptionResource.refetch()} />
          </div>
        </div>
        <div class={styles.containerTabButtons}>
          <ButtonGroup defaultSelectedItem="Videos" items={["Videos"/*, "Channels", "Support"*/, "About"]}
          style={{opacity: 1 - p(), "flex-shrink": 0}} onItemChanged={(item) => props.onActiveTabChanged?.(item)} />
        </div>
      </div>
      <Portal>
        <SettingsMenu menu={subscriptionMenu$().menu} anchor={anchor} show={showSettings$()} onHide={hideSubscriptionSettings} />
      </Portal>
    </div>
  );
};

const ChannelPage: Component = () => {
  const [params, setParams] = useSearchParams();
  const location = useLocation();

  const [error$, setError] = createSignal<any>(undefined);


  const authorSummary$ = createMemo(() => {
    const author = (location.state as any)?.author;
    console.log("state changed", author);
    return author;
  });

  const [canSearchChannel$] = createResourceDefault(async () => [], async () => params.url ? await ChannelBackend.CanSearchChannel(params.url) : false, undefined, false);

  const updatePager = async (query: string, url?: string) => {
    if (!url) {
      setChannelPager(undefined);
      return;
    }

    setChannelPager(undefined);

    if (!query || query.length < 1) {
      setChannelPager(await ChannelBackend.channelContentPager(url));
    } else {
      console.log("Searching for " + query);
      setChannelPager(await ChannelBackend.channelContentSearchPager(url, query));
    }
  };

  
  const [channelPager$, setChannelPager] = createSignal<Pager<IPlatformContent>>();
  const [activeTab$, setActiveTab] = createSignal("Videos");
  
  const [query$, setQuery] = createSignal<string>("");
  
  const [channel$, channelResource] = createResourceDefault(params.url, async (u) => {
    console.log("get channel", params.url);

    if (!u) {
      return undefined;
    }

    updatePager(untrack(query$), u);

    try {
      return await UIOverlay.catchDialogExceptions(async ()=>{
        const reqUrl = u;
        const result = await ChannelBackend.channelLoad(u);
        if(reqUrl == u && u != result.url) {
          setParams({
            ...params,
            url: result.url
          })
        }
        setError(undefined);
        return result;
      }, null, ()=>{
        channelResource.refetch();
      });
    }
    catch(ex) {
      setError(ex);
      return undefined;
    }
  });
  //createEffect(() => updatePager(untrack(query$), channel$()));
  
  const isReady$ = createMemo(()=>params?.url && (channel$() && channel$()?.url == params?.url || authorSummary$()))
  const isReadyContents$ = createMemo(()=>!!(params?.url && channelPager$()))

  let scrollContainerRef: HTMLDivElement | undefined;

  return (
    <div class={styles.container}>
      <LoaderContainer isLoading={!isReady$()} loadingText={"Loading Channel"} loadingSubText={params.url} loader={
        <div>
          <SkeletonDiv style={{height: "540px"}} />
          <Show when={error$()}>
            <div>
              <div>
                Failed to load channel
              </div>
              <div>
                {error$()}
              </div>
            </div>
          </Show>
          <Show when={!error$()}>
            <LoaderGrid itemCount={18} />
          </Show>
        </div>
      }>
          <ScrollContainer ref={scrollContainerRef}>
            <StickyShrinkOnScrollContainer outerContainerRef={scrollContainerRef}
                minimumHeight={136} 
                maximumHeight={540}>
              <ChannelTopBar bannerUrl={channel$()?.banner ?? authorSummary$()?.banner} 
                thumbnailUrl={channel$()?.thumbnail ?? authorSummary$()?.thumbnail}
                metadata={(((channel$()?.subscribers ?? 0) > 0) ? (toHumanNumber(channel$()?.subscribers) + " subscribers") : "")}
                name={channel$()?.name ?? authorSummary$()?.name}
                authorUrl={channel$()?.url ?? authorSummary$()?.url}
                activeTab={activeTab$()}
                onActiveTabChanged={(tab) => setActiveTab(tab)} />
            </StickyShrinkOnScrollContainer>
            <Switch>
              <Match when={activeTab$() === "Videos"}>
                <Show when={canSearchChannel$()}>
                <InputText icon={ic_search} placeholder={"Search channel"}
                  value={query$()}
                  showClearButton={true}
                  onTextChanged={(v) => setQuery(v)}
                  style={{
                    "margin-left": "24px",
                    "margin-right": "24px",
                    "margin-bottom": "24px",
                    "width": "calc(100% - 48px)"
                  }}
                  onSubmit={() => {
                    console.log("Channel Search Submit");
                    updatePager(query$(), channel$()?.url ?? params.url)
                    }} />
                </Show>
                      
                <Show when={error$()}>
                  <div style="text-align: center;">
                    <div style="color: #555">
                      Failed to load channel
                    </div>
                    <div style="color: #AA0000">
                      {error$()?.message ?? error$()}
                    </div>
                  </div>
                </Show>
                <Show when={!error$()}>
                  <LoaderContainer isLoading={!isReadyContents$()} loader={
                    <div>
                      <LoaderGrid itemCount={18} />
                    </div>
                  }>
                   <ContentGrid pager={channelPager$()} outerContainerRef={scrollContainerRef} />
                  </LoaderContainer>
                </Show>
              </Match>
              <Match when={activeTab$() === "About"}>
                  About
              </Match>
            </Switch>
          </ScrollContainer>
      </LoaderContainer>
    </div>
  );
};

export default ChannelPage;