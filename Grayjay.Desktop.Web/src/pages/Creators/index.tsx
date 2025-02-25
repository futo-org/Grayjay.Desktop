import { createResource, type Component, createMemo, createSignal, onCleanup, Signal, Show } from 'solid-js';
import { SubscriptionsBackend } from '../../backend/SubscriptionsBackend';
import ScrollContainer from '../../components/containers/ScrollContainer';
import VirtualGrid from '../../components/containers/VirtualGrid';
import CreatorView from '../../components/content/CreatorView';
import { useNavigate } from '@solidjs/router';
import styles from './index.module.css';
import NavigationBar from '../../components/topbars/NavigationBar';
import { createResourceDefault, toHumanNumber } from '../../utility';
import InputText from '../../components/basics/inputs/InputText';
import Dropdown from '../../components/basics/inputs/Dropdown';
import Anchor, { AnchorStyle } from '../../utility/Anchor';
import SettingsMenu, { Menu, MenuItemCheckbox } from '../../components/menus/Overlays/SettingsMenu';
import { Portal } from 'solid-js/web';
import StateGlobal from '../../state/StateGlobal';
import SettingsDropdown from '../../components/basics/inputs/SettingsDropdown';
import { ContentType } from '../../backend/models/ContentType';
import ic_search from '../../assets/icons/search.svg';

import iconSubscriptions from '../../assets/icons/icon_nav_subscriptions.svg'
import iconSearch from '../../assets/icons/icon24_search.svg'
import UIOverlay from '../../state/UIOverlay';
import EmptyContentView from '../../components/EmptyContentView';
import { Menus } from '../../Menus';
import StateWebsocket from '../../state/StateWebsocket';

const CreatorsPage: Component = () => {
  const navigate = useNavigate();

  let scrollContainerRef: HTMLDivElement | undefined;
  const [subs$, subsResource] = createResourceDefault(async () => [], async () => await SubscriptionsBackend.subscriptions());
  StateWebsocket.registerHandlerNew("SubscriptionsChanged", (packet)=>{
    subsResource.refetch();
  }, "playlistsPage");
  const [filterText, setFilterText] = createSignal("");
  const [sortBy, setSortBy] = createSignal(0);
  const [disabledSources, setDisabledSources] = createSignal([] as string[]);
  const filteredSubs = createMemo(() => {
    let result: ISubscription[] | undefined;

    if (filterText() && filterText().length > 0)
      result = subs$()?.filter(v => v.channel.name.toLowerCase().indexOf(filterText().toLowerCase()) !== -1);
    else
      result = subs$();

    const allDisabled = disabledSources();
    result = result?.filter(v => allDisabled.indexOf(v.channel.id.pluginID) === -1);

    switch (sortBy()) {
      case 0:
        result?.sort((a, b) => a.channel.name.localeCompare(b.channel.name));
        break;
      case 1:
        result?.sort((a, b) => b.channel.name.localeCompare(a.channel.name));
        break;
    }
    return result;
  });
  
  const sortOptions = [
    "Name (A-Z)",
    "Name (Z-A)",
  ];
  
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

  const valueString$ = createMemo(() => {
    const a = StateGlobal.sources$()?.filter(v => disabledSources().indexOf(v.id) === -1)?.map(v => v.name) ?? [];
    return a.length > 0 ? a.join(", ") : "None";
  });

  onCleanup(() => {
    anchor.dispose();
  });

  return (
    <div class={styles.containerCreators}>
      <NavigationBar isRoot={true} defaultSearchType={ContentType.CHANNEL} />
      <Show when={subs$() && subs$()!.length > 0}>
        <ScrollContainer ref={scrollContainerRef}>
          <div class={styles.containerFilters}>
            <SettingsDropdown label="Sources" valueString={valueString$()} style={{"max-width": "200px"}} anchorStyle={AnchorStyle.BottomLeft} menu={{
              items: StateGlobal.sources$()?.map(i => new MenuItemCheckbox({
                isSelected: true,
                name: i.name,
                icon: i.absoluteIconUrl,
                onToggle: (v) => {
                  if (v)
                    setDisabledSources(disabledSources().filter(x=>x != i.id));
                  else
                    setDisabledSources([... disabledSources(), i.id]);
                }
              })) ?? []
            }} />
            <InputText icon={ic_search} placeholder={"Search subscriptions"}
              value={filterText()}
              showClearButton={true}
              inputContainerStyle={{
                "height": "70px", 
                "background": "#141414"
              }}
              onTextChanged={(v) => {
                setFilterText(v);
              }} />
            <Dropdown label="Sort by" onSelectedChanged={(v) => setSortBy(v)} value={sortBy()} options={sortOptions} anchorStyle={AnchorStyle.BottomLeft} style={{"width": "230px"}} />
          </div>

          <VirtualGrid outerContainerRef={scrollContainerRef}
            items={filteredSubs()}
            itemHeight={276}
            itemWidth={200}
            autosizeWidth={true}
            notifyEndOnLast={5}
            style={{
              "margin-left": "24px",
              "margin-top": "24px",
              "margin-bottom": "24px",
              "margin-right": "24px",
            }}
            elementStyle={{
             /* "margin-left": "7px",
              "margin-top": "7px"*/
            }}
            builder={(index, item) =>
              <CreatorView {... item()?.channel} 
                metadata={`${toHumanNumber(item()?.channel?.subscribers)} subscribers`}
                onClick={() => {
                  const url = item()?.channel?.url;
                  if(url)
                    navigate("/web/channel?url=" + encodeURIComponent(url), { state: { author: item()?.channel } })
                }}
                onSettingsClick={(el) => {
                  showSubscriptionSettings(el, item());
                }}
                isSubscribedInitialState={true} />
            } />
        </ScrollContainer>
      </Show>
      <Show when={!subs$() || subs$()!.length == 0}>
        <EmptyContentView 
            icon={iconSubscriptions}
            title='You have no subscriptions'
            description='Subscribe to some creators or import them from elsewhere.'
            actions={[
              {
                icon: iconSubscriptions,
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
      <Portal>
        <SettingsMenu menu={subscriptionMenu$().menu} anchor={anchor} show={showSettings$()} onHide={hideSubscriptionSettings} />
      </Portal>
    </div>
  );
};

export default CreatorsPage;