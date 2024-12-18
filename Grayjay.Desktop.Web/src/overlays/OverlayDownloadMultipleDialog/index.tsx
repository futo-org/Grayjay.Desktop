
import { Accessor, Component, For, Index, JSX, Match, Show, Suspense, Switch, batch, createEffect, createMemo, createResource, createSignal, onCleanup, onMount } from 'solid-js';
import styles from './index.module.css';
import iconClose from '../../assets/icons/icon24_close.svg';
import UIOverlay from '../../state/UIOverlay';

import iconCheck from '../../assets/icons/icon_checkmark.svg'
import { toHumanBitrate } from '../../utility';
import ButtonFlex from '../../components/buttons/ButtonFlex';
import Button from '../../components/buttons/Button';
import { DownloadBackend } from '../../backend/DownloadBackend';
import Loader from '../../components/basics/loaders/Loader';
import { IPlatformVideo } from '../../backend/models/content/IPlatformVideo';

export interface OverlayDownloadMultipleDialogProps {
  videos?: IPlatformVideo[],
  playlistId?: string,
  onResult?: (videoPixelCount: number, audioBitrate: number) => void
};
interface SourceItem {
  name: string,
  meta?: string,
  pixelCount?: number,
  bitrate?: number
}
const OverlayDownloadMultipleDialog: Component<OverlayDownloadMultipleDialogProps> = (props: OverlayDownloadMultipleDialogProps) => {

    const videoSources$: Accessor<SourceItem[]> = createMemo(()=>[
      {name: "2160p", pixelCount: 3840 * 2160},
      {name: "1440p", pixelCount: 2560 * 1440},
      {name: "1080p", pixelCount: 1920 * 1080},
      {name: "720p", pixelCount: 1280 * 720},
      {name: "480p", pixelCount: 856 * 480},
      {name: "360p", pixelCount: 640 * 360},
      {name: "144p", pixelCount: 256 * 144}
    ]);
    const audioSources$: Accessor<SourceItem[]> = createMemo(()=> [
      {name: "High Bitrate", bitrate: 9999999},
      {name: "Low Bitrate", bitrate: 1}
    ]);


    let [selectedVideo$, setSelectedVideo] = createSignal(0);
    let [selectedAudio$, setSelectedAudio] = createSignal(0);
    
    const isDownloadable$: Accessor<Boolean> = createMemo(()=>
      (selectedVideo$() !== undefined && selectedVideo$() >= 0) ||
      (selectedAudio$() !== undefined && selectedAudio$() >= 0));

    function setVideo(index: number, manifestIndex: number = -1) {
      setSelectedVideo(index);
      setSelectedManifestIndex(manifestIndex);
    }

    function download() {
      if(!isDownloadable$())
        return false;
      UIOverlay.dismiss();

      const selectedVideoIndex = selectedVideo$();
      const selectedAudioIndex = selectedAudio$();
      const selectedVideo = selectedVideoIndex !== undefined && selectedVideoIndex >= 0 ? videoSources$()[selectedVideoIndex] : undefined;
      const selectedAudio = selectedAudioIndex !== undefined && selectedAudioIndex >= 0 ? audioSources$()[selectedAudioIndex] : undefined;


      if(selectedVideo || selectedAudio) {
        if(props.playlistId)
          DownloadBackend.downloadPlaylist(props.playlistId, selectedVideo?.pixelCount, selectedAudio?.bitrate);
        else
          DownloadBackend.downloadMultiple(props.videos, selectedVideo?.pixelCount, selectedAudio?.bitrate);
        if(props.onResult)
          props.onResult(selectedVideo?.pixelCount ?? -1, selectedAudio?.bitrate ?? -1);
      }
    }

    return (
      <div class={styles.container}> 
        <div class={styles.dialogHeader}>
          <div class={styles.headerText}>
            Download
          </div>
          <div class={styles.headerSubText}>
            Select the quality and subtitles you like to download.
          </div>
          <div class={styles.closeButton} onClick={()=>UIOverlay.dismiss()}>
            <img src={iconClose} />
          </div>
        </div>
          <div>
              <div class={styles.sources}>
                  <div class={styles.source} classList={{[styles.enabled]: -1 == selectedVideo$()}} onClick={()=>setVideo(-1)}>
                    <div class={styles.imgContainer}><img src={iconCheck} /></div>
                    <div class={styles.name}>
                      None
                    </div>
                    <div class={styles.meta}>
                    </div>
                  </div>
                      <Index each={videoSources$()}>{(subSource$, i)=>
                        <div class={styles.source} classList={{[styles.enabled]: i == selectedVideo$()}} 
                            onClick={()=>setVideo(i)}>
                          <div class={styles.imgContainer}><img src={iconCheck} /></div>
                          <div class={styles.name}>
                            {subSource$().name}
                          </div>
                          <div class={styles.meta}>
                            {subSource$().meta}
                          </div>
                        </div>
                      }</Index>
              </div>
              <div class={styles.sources}>
                <Index each={audioSources$()}>{(audio$, i) =>
                  <div class={styles.source} classList={{[styles.enabled]: i == selectedAudio$()}} onClick={()=>setSelectedAudio(i)}>
                    <div class={styles.imgContainer}><img src={iconCheck} /></div>
                    <div class={styles.name}>
                      {audio$().name}
                    </div>
                    <div class={styles.meta}>
                      {audio$()?.meta}
                    </div>
                  </div>
                }</Index>
              </div>
          </div>
        <div style="height: 1px; background-color: rgba(255, 255, 255, 0.09); margin-top: 10px; margin-bottom: 10px;"></div>
        <div style="text-align: right">
            <Button text='Download'
              onClick={()=>download()}
              style={{"margin-left": "auto", cursor: ((isDownloadable$() ? "pointer" : "default"))}} 
              color={(isDownloadable$()) ? "linear-gradient(267deg, #01D6E6 -100.57%, #0182E7 90.96%)" : "gray"} />
        </div>
      </div>
    );
  };
  
  export default OverlayDownloadMultipleDialog;