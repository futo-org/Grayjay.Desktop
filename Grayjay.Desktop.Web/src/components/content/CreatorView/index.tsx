import { Component, Show, createMemo, createSignal } from 'solid-js'

import styles from './index.module.css';
import { proxyImage, toHumanSignificantDuration, toHumanTime } from '../../../utility';
import StateGlobal from '../../../state/StateGlobal';
import SubscribeButton from '../../buttons/SubscribeButton';
import settings from '../../../assets/icons/icon24_settings.svg';
import TransparentIconButton from '../../buttons/TransparentIconButton';
import { SubscriptionsBackend } from '../../../backend/SubscriptionsBackend';
import { FocusableOptions } from '../../../nav';
import { focusable } from '../../../focusable'; void focusable;

interface CreatorViewProps {
  id?: IPlatformID,
  name?: string;
  thumbnail?: string;
  metadata?: string;
  subscription?: ISubscription;
  url?: string;
  onClick?: () => void;
  onSettingsClick?: (el: HTMLElement) => void;
  isSubscribedInitialState?: boolean;
  focusableOpts?: FocusableOptions;
}

const CreatorView: Component<CreatorViewProps> = (props) => {
  const pluginIconUrl = createMemo(() => {
    const plugin = StateGlobal.getSourceConfig(props.id?.pluginID);
    return plugin?.absoluteIconUrl;
  });
  
  const watchTimeData$ = createMemo(()=>{
    const sub = props.subscription;
    const showMetrics = StateGlobal.settings$()?.object?.subscriptions?.showWatchMetrics
    if(!sub || !showMetrics)
      return "";
    const parts = [];
    if(sub.playbackViews > 0)
      parts.push(sub.playbackViews + " views");
    if(sub.playbackSeconds > 0)
      parts.push(toHumanSignificantDuration(sub.playbackSeconds));


    return parts.join(" - ");
  });

  return (
    <div class={styles.containerCreator} onClick={() => props.onClick?.()} use:focusable={props.focusableOpts}>
      <div style="position: relative; display: inline-block;"> 
        <img src={(props.thumbnail) ? "/Images/CachePassthrough?url=" + encodeURIComponent(props.thumbnail) : undefined} class={styles.thumbnail} referrerPolicy='no-referrer' />
        <Show when={pluginIconUrl()}>
          <div style="height: 28px; width: 28px; position: absolute; right: 0px; bottom: 0px; display: grid; justify-content: center; align-content: center;">
            <img src={pluginIconUrl()} style="max-width: 100%; max-height: 100%" /> 
          </div>
        </Show>
      </div>
      <div class={styles.name}>
        {props.name}
      </div>
      <Show when={props.metadata}>
        <div class={styles.metadata}>
          {props.metadata}
        </div>
      </Show>
      <Show when={watchTimeData$()}>
        <div class={styles.metadata} style={{"margin-top": "0px", "margin-bottom": "0px", "opacity": "0.4"}}>
          {watchTimeData$()}
        </div>
      </Show>
      <div style={{"display": "flex", "flex-direction": "row", "align-items": "end"}}>
        <SubscribeButton style={{"margin-top": "12px", "width": "100%"}} small={true} author={props.url} isSubscribedInitialState={props.isSubscribedInitialState} />
        <Show when={props.onSettingsClick}>
          <TransparentIconButton style={{"margin-left": "4px", "flex-shrink": "0", "width": "42px", "height": "42px"}} icon={settings} onClick={(ev) => {
            props.onSettingsClick?.(ev.target! as HTMLElement);
            ev.stopPropagation();
            ev.preventDefault();
          }} />
        </Show>
      </div>
      
  </div>
  );
};

export default CreatorView;