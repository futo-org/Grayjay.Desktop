import { Component, Match, Show, Switch, createMemo, createSignal, onCleanup } from 'solid-js'

import styles from './index.module.css';
import { getBestThumbnail, positiveOrQ, proxyImage, resolutionOrUnknown, toHumanBitrate, toHumanBytesSize, toHumanBytesSpeed, toHumanNumber } from '../../../utility';
import StateGlobal from '../../../state/StateGlobal';
import SubscribeButton from '../../buttons/SubscribeButton';
import settings from '../../../assets/icons/icon24_settings.svg';
import TransparentIconButton from '../../buttons/TransparentIconButton';
import { IVideoDownload } from '../../../backend/models/downloads/IVideoDownload';
import StateWebsocket from '../../../state/StateWebsocket';

import iconDownloadOngoing from '../../../assets/icons/icon_download_ongoing.svg'
import iconDownloadQueued from '../../../assets/icons/icon_download_queued.svg'
import iconDownloadError from '../../../assets/icons/icon_error.svg'
import iconClose from '../../../assets/icons/icon24_close.svg'
import UIOverlay from '../../../state/UIOverlay';
import { DialogButton, DialogDescriptor } from '../../../overlays/OverlayDialog';
import { DownloadBackend } from '../../../backend/DownloadBackend';

interface CreatorViewProps {
  downloading: IVideoDownload
}

const DownloadingView: Component<CreatorViewProps> = (props) => {
  const [downloading$, setDownloading] = createSignal(props.downloading);

  const id = props.downloading.video.id;

  const [hoveringError$, setHoveringError] = createSignal(false);

  StateWebsocket.registerHandlerNew("DownloadChanged", (packet)=>{
    const download = packet.payload as IVideoDownload;
    if(download.video.id.value == id.value)
      setDownloading(download);
  }, id);

  onCleanup(()=>{
    StateWebsocket.unregisterHandler("DownloadChanged", id);
  });

  function statusString(downloading: IVideoDownload): string{
    const parts = [];
    if(downloading.state == 7)
      return "Error";

    if(downloading.state < 3)
      return "Download Queued"

    if(downloading.progress)
      parts.push(Math.floor(downloading.progress * 100) + "%");
    if(downloading.downloadSpeed)
      parts.push(toHumanBytesSpeed(downloading.downloadSpeed));

      return parts.join(" • ");
  }
  function metaString(downloading: IVideoDownload): string{
    const parts = [];
    if(downloading.videoSource)
      parts.push(`${resolutionOrUnknown(downloading.videoSource.width, downloading.videoSource.height)}`);
    if(downloading.audioSource)
      parts.push(toHumanBitrate(downloading.audioSource.bitrate));
    return parts.join(" • ");
  }

  function cancel() {
    UIOverlay.dialog({
      title: "Would you like to cancel this download?",
      description: "[" + downloading$().video.name + "]?",
      buttons: [
        {
          title: "Nevermind",
          onClick() {
            
          }
        } as DialogButton,
        {
          title: "Cancel",
          style: "accent",
          onClick() {
            DownloadBackend.deleteDownload(downloading$().video.id);
          }
        } as DialogButton
      ]
    } as DialogDescriptor)
  }

  function clicked() {
    if(downloading$().state == 7 && downloading$().error) {
      UIOverlay.dialog({
        title: "Download failed",
        description: "[" + downloading$().video.name + "] failed to download due to:\n\n" + downloading$().error + "\n\nDownloads are automatically retried on occasion",
        buttons: [
          {
            title: "Delete",
            style: "accent",
            onClick() {
              DownloadBackend.deleteDownload(downloading$().video.id);
            }
          } as DialogButton,
          {
            title: "Ok",
            style: "primary",
            onClick() {
              
            }
          } as DialogButton
        ]
      } as DialogDescriptor)
    }
  }

  return (
    <div class={styles.downloadingCard} style="position: relative;">
        <div class={styles.downloadThumbnail} style={{"background-image": "url(" + getBestThumbnail(downloading$().video.thumbnails)?.url + ")"}} onClick={clicked}>
          <div class={styles.badgeStatus}>
            <div>
              <Show when={downloading$().state < 3}>
                <img src={iconDownloadQueued} class={styles.statusIcon} />
              </Show>
              <Show when={downloading$().state == 3}>
                <img src={iconDownloadOngoing} class={styles.statusIcon} />
              </Show>
              <Show when={downloading$().state > 3 && downloading$().state != 7}>
                <img src={iconDownloadOngoing} class={styles.statusIcon} />
              </Show>
              <Show when={downloading$().state == 7}>
                <img src={iconDownloadError} class={styles.statusIcon} />
              </Show>
            </div>
            {statusString(downloading$())}
          </div>

          <div class={styles.buttonCancel} onClick={cancel}>
            <img src={iconClose} />
          </div>

          <Show when={downloading$().videoFileSize || downloading$().audioFileSize}>
            <div class={styles.badgeSize}>
              {toHumanBytesSize(downloading$().videoFileSize + downloading$().audioFileSize)}
            </div>
          </Show>
          <Show when={downloading$().state == 7}>
            <div class={styles.downloadingErrorHover}>
              Click to show error
            </div>
            <div class={styles.progress} style={{"width": "100%", "background-color": "red"}}>

            </div>
          </Show>
          <Show when={downloading$().state != 7}>
            <div class={styles.progress} style={{"width": parseInt(downloading$().progress * 100 + "") + "%"}}>

            </div>
          </Show>
        </div>

        <div class={styles.title}>
          {downloading$().video.name}
        </div>
        <div class={styles.author}>
          {downloading$().video.author.name}
        </div>
        <div class={styles.meta}>
          {metaString(downloading$())}
        </div>
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

export default DownloadingView;