import { createResource, type Component, Show } from 'solid-js';

import styles from './index.module.css';
import { HomeBackend } from '../../backend/HomeBackend';
import ContentGrid from '../../components/containers/ContentGrid';
import NavigationBar from '../../components/topbars/NavigationBar';
import ScrollContainer from '../../components/containers/ScrollContainer';
import StateGlobal from '../../state/StateGlobal';
import { DateTime } from 'luxon';
import IconButton from '../../components/buttons/IconButton';

import iconRefresh from "../../assets/icons/icon_reload_temp.svg"
import iconHome from "../../assets/icons/icon_nav_home.svg"
import iconSources from "../../assets/icons/ic_circles.svg"
import { useNavigate } from '@solidjs/router';
import EmptyContentView from '../../components/EmptyContentView';
import { focusable } from '../../focusable'; void focusable;
import LiveChatWindow from '../../components/LiveChatWindow';

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
  
  let scrollContainerRef: HTMLDivElement | undefined;
  return (
    <div class={styles.container}>
        <NavigationBar isRoot={true} childrenAfter={
          <img src={iconRefresh} style={{"margin-left": "24px", "cursor": "pointer", "height": "30px", "width": "30px" }} 
            onClick={()=>{ StateGlobal.reloadHome() }} use:focusable={{
              onPress: () => StateGlobal.reloadHome()
            }} />
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
    </div>
  );
};

export default HomePage;
