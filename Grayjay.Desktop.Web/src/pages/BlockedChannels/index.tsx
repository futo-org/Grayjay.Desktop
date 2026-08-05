import { Component, For, Show, createSignal } from 'solid-js';
import { useNavigate } from '@solidjs/router';
import styles from './index.module.css';
import NavigationBar from '../../components/topbars/NavigationBar';
import ScrollContainer from '../../components/containers/ScrollContainer';
import Button from '../../components/buttons/Button';
import StateBlockedChannels from '../../state/StateBlockedChannels';
import UIOverlay from '../../state/UIOverlay';
import EmptyContentView from '../../components/EmptyContentView';
import iconBlocked from '../../assets/icons/icon24_hide.svg';
import iconSearch from '../../assets/icons/icon24_search.svg';
import { proxyImage } from '../../utility';

const BlockedChannelsPage: Component = () => {
  const navigate = useNavigate();

  const blocked$ = StateBlockedChannels.blocked$;

  async function unblock(url: string) {
    await StateBlockedChannels.unblock(url);
  }

  async function clearAll() {
    const count = blocked$()?.length ?? 0;
    if (count <= 0) {
      return;
    }
    UIOverlay.overlayConfirm({
      no: () => { },
      yes: async () => {
        await StateBlockedChannels.clearAll();
      }
    }, `Unblock all ${count} blocked channel${count > 1 ? "s" : ""}? Content from these channels will appear again in your home feed and be playable.`);
  }

  return (
    <div class={styles.container}>
      <NavigationBar isRoot={true} />
      <div class={styles.header}>
        <h1 class={styles.title}>Blocked Channels</h1>
        <Show when={(blocked$()?.length ?? 0) > 0}>
          <Button text="Clear all" onClick={clearAll} style={{"width": "140px", "height": "42px"}} focusableOpts={{
            onPress: clearAll
          }} />
        </Show>
      </div>
      <Show when={blocked$() && blocked$()!.length > 0}>
        <ScrollContainer>
          <div class={styles.list}>
            <For each={blocked$()}>{(item) =>
              <div class={styles.item}>
                <Show when={item.thumbnail}>
                  <img class={styles.thumbnail} src={proxyImage(item.thumbnail!)} referrerPolicy='no-referrer' alt="" onClick={() => {
                    navigate("/web/channel?url=" + encodeURIComponent(item.url), { state: { author: item } });
                  }} />
                </Show>
                <div class={styles.name} onClick={() => {
                  navigate("/web/channel?url=" + encodeURIComponent(item.url), { state: { author: item } });
                }}>{item.name || item.url}</div>
                <Button text="Unblock" onClick={() => unblock(item.url)} style={{"width": "120px", "height": "42px"}} focusableOpts={{
                  onPress: () => unblock(item.url)
                }} />
              </div>
            }</For>
          </div>
        </ScrollContainer>
      </Show>
      <Show when={blocked$() && blocked$()!.length == 0}>
        <EmptyContentView
          icon={iconBlocked}
          title='No blocked channels'
          description='Blocked channels will not appear in your home feed and their videos will not play.'
          actions={[
            {
              icon: iconSearch,
              title: "Search Creators",
              color: "#019BE7",
              action: () => navigate("/web/search?type=channel")
            }
          ]} />
      </Show>
    </div>
  );
};

export default BlockedChannelsPage;
