
import { Accessor, Component, For, Index, JSX, Match, Show, Suspense, Switch, batch, createEffect, createMemo, createResource, createSignal, onCleanup, onMount } from 'solid-js';
import styles from './index.module.css';
import iconClose from '../../assets/icons/icon24_close.svg';
import UIOverlay from '../../state/UIOverlay';

import iconCheck from '../../assets/icons/icon_checkmark.svg'
import { positiveOrQ, resolutionOrUnknown, toHumanBitrate } from '../../utility';
import ButtonFlex from '../../components/buttons/ButtonFlex';
import Button from '../../components/buttons/Button';
import { DownloadBackend } from '../../backend/DownloadBackend';
import Loader from '../../components/basics/loaders/Loader';
import CenteredLoader from '../../components/basics/loaders/CenteredLoader';

export interface OverlayDownloadDialogProps {
  url?: string,
  onResult?: (video: number, audio: number, subs: number) => void
};
interface SourceItem {
  name: string,
  meta?: string,
  subSources?: SourceItem[]
}
const OverlayDownloadDialog: Component<OverlayDownloadDialogProps> = (props: OverlayDownloadDialogProps) => {

  const [sources$, sourcesResource] = createResource(async ()=>{
    return await UIOverlay.catchDialogExceptions<IDownloadSources>(async ()=>{
      return await DownloadBackend.loadDownloadSources(props.url);
    }, ()=>{
      UIOverlay.dismiss();
    }, ()=>{
      UIOverlay.overlayDownload(props.url);
    }, ()=>UIOverlay.dismiss());
  });

    const hasVideo$ = createMemo(()=>{
      return ((sources$()?.videoSources?.length ?? 0) > 0) ?? false;
    });
    const hasAudio$ = createMemo(()=>{
      return ((sources$()?.audioSources?.length ?? 0) > 0) ?? false;
    });



    const videoSources$: Accessor<SourceItem[]> = createMemo(()=>(sources$()) ? sources$()!.videoSources?.map((x: any, index: number)=>({
      name: x.name,
      meta: resolutionOrUnknown(x.width, x.height),
      subSources: (x.type == "HLSSource" && sources$()?.manifestSources[index])
        ? sources$()?.manifestSources[index].map((z: any)=>({name: z.name, meta: `${resolutionOrUnknown(z.width, z.height)}`})) : []
    })) : ((sources$()?.audioSources?.length ?? 0) == 0) ? [
      {name: "2160p"},
      {name: "1440p"},
      {name: "1080p"},
      {name: "720p"},
      {name: "480p"},
      {name: "360p"},
      {name: "144p"}
    ] : []);
    const audioSources$: Accessor<SourceItem[]> = createMemo(()=>(sources$()?.audioSources && sources$()!.audioSources.length > 0) ? 
        sources$()!.audioSources?.map((x: any)=>({
      name: x.name,
      meta: toHumanBitrate(x.bitrate)
    })) ?? [] : ((sources$()?.videoSources?.length ?? 0) == 0 && (sources$()?.audioSources?.length ?? 0) > 0) ? [
      {name: "High Bitrate"},
      {name: "Low Bitrate"}
    ] : []);
    const subtitleSources$ = createMemo(()=>(sources$()) ? sources$()!.subtitleSources?.map((x: any)=>({
      name: x.name
    })) ?? [] : []);

    let [selectedVideo$, setSelectedVideo] = createSignal(0);
    let [selectedManifestIndex$, setSelectedManifestIndex] = createSignal(-1);
    let [selectedAudio$, setSelectedAudio] = createSignal(0);
    let [selectedSubtitles$, setSelectedSubtitles] = createSignal(-1);

    createEffect(()=>{
      const sources = sources$();
      if(!sources)
        return;
      if(sources?.videoSources?.length != 0) {
        if(sources?.videoSources[0].type == "HLSSource")
          setVideo(0, 0);
        else
          setVideo(0, -1);
      }
      else
        setSelectedVideo(-1);
      if(sources?.audioSources?.length != 0)
        setSelectedAudio(0);
      else
        setSelectedAudio(-1);

        if(sources.videoSources.length == 0 && sources.audioSources.length == 0) {
          UIOverlay.dismiss();
          UIOverlay.toastTitled("No downloads available", "This video has no supported downloadable sources")
        }
    });

    function setVideo(index: number, manifestIndex: number = -1) {
      setSelectedVideo(index);
      setSelectedManifestIndex(manifestIndex);
    }

    function download() {
      if(!isDownloadable$())
        return false;
      UIOverlay.dismiss();

      if(sources$()) {
        DownloadBackend.download(sources$()!.id, selectedVideo$(), selectedAudio$(), selectedSubtitles$(), selectedManifestIndex$());

        if(props.onResult)
          props.onResult(selectedVideo$(), selectedAudio$(), selectedSubtitles$());
      }
    }

    const isDownloadable$ = createMemo(()=>{
      if(!sources$())
        return false;
      if(hasVideo$() && selectedVideo$() < 0 && selectedAudio$() < 0)
        return false;
      if(hasAudio$() && selectedAudio$() < 0)
        return false;
      return true;
    });

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
        <Show when={!sources$()}>
          <div style="min-width: 500px; padding: 12px;">
            <CenteredLoader />
          </div>
        </Show>
        <Show when={sources$()}>
          <div>
            <Show when={videoSources$() && videoSources$().length > 0}>
              <div class={styles.sources}>
                <Show when={hasVideo$()}>
                  <div class={styles.source} classList={{[styles.enabled]: -1 == selectedVideo$()}} onClick={()=>setVideo(-1)}>
                    <div class={styles.imgContainer}><img src={iconCheck} /></div>
                    <div class={styles.name}>
                      None
                    </div>
                    <div class={styles.meta}>
                    </div>
                  </div>
                </Show>
                <Index each={videoSources$()}>{(video$, i) =>
                  <Show when={video$()?.subSources?.length == 0}>
                    <div class={styles.source} classList={{[styles.enabled]: i == selectedVideo$()}} onClick={()=>setVideo(i)}>
                    <div class={styles.imgContainer}><img src={iconCheck} /></div>
                      <div class={styles.name}>
                        {video$().name}
                      </div>
                      <div class={styles.meta}>
                        {video$().meta}
                      </div>
                    </div>
                  </Show>
                }</Index>
                <Show when={videoSources$().filter(x=>x.subSources?.length != 0).length > 0}>
                  <Index each={videoSources$()}>{(video$, i)=>
                    <Show when={video$()?.subSources?.length != 0}>
                      <div class={styles.subSourceHeader}>{video$().name}</div>
                      <Index each={video$().subSources}>{(subSource$, i2)=>
                        <div class={styles.source} classList={{[styles.enabled]: i == selectedVideo$() && i2 == selectedManifestIndex$()}} 
                            onClick={()=>setVideo(i, i2)}>
                          <div class={styles.imgContainer}><img src={iconCheck} /></div>
                          <div class={styles.name}>
                            {subSource$().name}
                          </div>
                          <div class={styles.meta}>
                            {subSource$().meta}
                          </div>
                        </div>
                      }</Index>
                    </Show>
                  }</Index>
                </Show>
              </div>  
            </Show>
            <Show when={audioSources$() && audioSources$().length > 0}>
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
            </Show>
            <Show when={subtitleSources$() && subtitleSources$().length > 0}>
              <div class={styles.sources}>
                <Index each={subtitleSources$()}>{(subtitle$, i) =>
                  <div class={styles.source} classList={{[styles.enabled]: i == selectedSubtitles$(),[styles.full]: true}} onClick={()=> (selectedSubtitles$() == i) ? setSelectedSubtitles(-1) : setSelectedSubtitles(i)}>
                  <img src={iconCheck} />
                    <div class={styles.name}>
                      {subtitle$().name}
                    </div>
                  </div>
                }</Index>
              </div>
            </Show>
          </div>
        </Show>
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
  
  export default OverlayDownloadDialog;