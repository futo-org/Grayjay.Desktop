import { Component, Show, createMemo, createSignal, onCleanup } from 'solid-js'

import styles from './index.module.css';
import { getBestThumbnail, positiveOrQ, proxyImage, resolutionOrUnknown, toHumanBitrate, toHumanBytesSize, toHumanBytesSpeed, toHumanNumber, toHumanTime } from '../../../utility';
import StateGlobal from '../../../state/StateGlobal';
import SubscribeButton from '../../buttons/SubscribeButton';
import settings from '../../../assets/icons/icon24_settings.svg';
import TransparentIconButton from '../../buttons/TransparentIconButton';
import { IVideoDownload } from '../../../backend/models/downloads/IVideoDownload';
import StateWebsocket from '../../../state/StateWebsocket';
import { IVideoLocal } from '../../../backend/models/downloads/IVideoLocal';
import { useNavigate } from '@solidjs/router';
import { useVideo } from '../../../contexts/VideoProvider';
import IconButton from '../../buttons/IconButton';
import { IPlatformVideo } from '../../../backend/models/content/IPlatformVideo';
import { focusable } from '../../../focusable'; void focusable;
import more from '../../../assets/icons/more_horiz_FILL0_wght400_GRAD0_opsz24.svg';
import { OpenIntent } from '../../../nav';

interface DownloadedViewProps {
  downloaded?: IVideoLocal
  onSettings?: (element: HTMLElement, content: IVideoLocal, openIntent: OpenIntent) => void;
}

const DownloadedView: Component<DownloadedViewProps> = (props) => {

  function metaString(downloading?: IVideoLocal): string | undefined {
    if (!downloading) {
      return undefined;
    }

    const parts = [];
    if(downloading.videoSources && downloading.videoSources.length > 0)
      parts.push(`${resolutionOrUnknown(downloading.videoSources[0].width, downloading.videoSources[0].height)}`);
    if(downloading.audioSources && downloading.audioSources.length > 0)
      parts.push(toHumanBitrate(downloading.audioSources[0].bitrate));
    return parts.join(" • ");
  }

  function calcSize(): number{
    let size = 0;
    if(props.downloaded?.videoSources)
      for(let source of props.downloaded?.videoSources)
        size += parseInt(source.fileSize);
    if(props.downloaded?.audioSources)
      for(let source of props.downloaded?.audioSources)
        size += parseInt(source.fileSize);
      return size;
  }

  const video = useVideo();
  function navigate() {
    if(props.downloaded) {
      video?.actions.openVideo(props.downloaded.videoDetails);
    }
  }

  let refMoreButton: HTMLDivElement | undefined;

  return (
    <div class={styles.downloadingCard} use:focusable={props.downloaded ? {
      onPress: navigate,
      onOptions: (el, openIntent) => props.onSettings?.(el, props.downloaded!, openIntent)
    } : undefined}>
        <div class={styles.downloadThumbnail} onClick={navigate} style={{"background-image": "url(" + getBestThumbnail(props.downloaded?.videoDetails.thumbnails)?.url + ")"}}>
          <div class={styles.badgeStatus}>
            {toHumanTime(props.downloaded?.videoDetails?.duration)}
          </div>
            <div class={styles.badgeSize}>
              {toHumanBytesSize(calcSize())}
            </div>
        </div>
        <div class={styles.title} onClick={navigate}>
          {props.downloaded?.name}
        </div>
        <div class={styles.author}>
          {props.downloaded?.author.name}
        </div>
        <div class={styles.meta}>
          {metaString(props.downloaded)}
        </div>
        <Show when={props.onSettings}>
          <IconButton icon={more} ref={refMoreButton}
            onClick={() => props.onSettings?.(refMoreButton!, props.downloaded!, OpenIntent.Pointer)}
            style={{position: 'absolute', bottom: '10px', right: '10px' }} />
        </Show>
    </div>
  )
  /*
  return (
    <div style="background-color: #111; padding: 30px; margin-bottom: 20px; margin-left: 30px; margin-right: 30px; position: relative;">
      <div>
        <img src={proxyImage(getBestThumbnail(downloading$().video.thumbnails)?.url)} style="position: absolute; left: 20px; top: 40px; width: 70px; height: 40px;"/>
        <div style="margin-left: 80px; margin-bottom: 20px;">
          <div>
            {downloading$().video.name}
          </div>
          <div>
            Download Speed: {downloading$().downloadSpeed}
          </div>
          <div>
            State: {downloading$().state}
          </div>
          <Show when={downloading$().error}>
            <div style="color: red;">
              Error: {downloading$().error};
            </div>
          </Show>
        </div>
        <div style={{"height": "2px", width: parseInt(downloading$().progress * 100 + "") + "%", "background-color": "red"}}>

        </div>
      </div>
    </div>
  );*/
};

export default DownloadedView;