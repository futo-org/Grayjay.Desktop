
import { Component, For, Index, JSX, Match, Show, Switch, batch, createEffect, createMemo, createResource, createSignal, onCleanup, onMount } from 'solid-js';
import styles from './index.module.css';
import iconClose from '../../assets/icons/icon24_close.svg';
import UIOverlay from '../../state/UIOverlay';

import iconCheck from '../../assets/icons/icon_checkmark.svg'
import { createResourceDefault, promptFile, toHumanBitrate } from '../../utility';
import Button from '../../components/buttons/Button';
import InputText from '../../components/basics/inputs/InputText';
import { SubscriptionsBackend } from '../../backend/SubscriptionsBackend';
import { focusScope } from '../../focusScope'; void focusScope;
import { focusable } from '../../focusable'; void focusable;
import { ImagesBackend } from '../../backend/ImagesBackend';


export interface OverlayImageSelectorDialogProps {
  title: string,
  description: string,
  channels: string[],
  noDismiss?: boolean,
  onResult?: (selected: IImageVariable) => void
};
const OverlayImageSelector: Component<OverlayImageSelectorDialogProps> = (props: OverlayImageSelectorDialogProps) => {

    const [subscriptions$] = createResourceDefault(props, async p=>(await SubscriptionsBackend.subscriptions()).filter(x=>p.channels && p.channels.indexOf(x.channel.url) >= 0));
    const [images$, imagesResource] = createResourceDefault(async p=>(await ImagesBackend.images()));

    const [selected$, setSelected] = createSignal<IImageVariable>();
    const [imageUrl$, setImageUrl] = createSignal<string>();

    function select(image: IImageVariable) {
      setSelected(image);
    }
    function selectSubscription(url: string, imageUrl?: string){
      setSelected({
        subscriptionUrl: url,
        url: imageUrl
      })
    }
    function selectUrl(url: string){
      setSelected({
        url: url
      })
    }

    function isSelectedUrl(url: string) {
      if(!selected$()?.url)
        return false;
      return selected$()?.url == url;
    }
    function isSelectedSubscription(url: string) {
      if(!selected$()?.subscriptionUrl)
        return false;
      return selected$()?.subscriptionUrl == url;
    }


    function uploadImage() {
      promptFile(async (file)=>{
        const upload = await ImagesBackend.imageUpload(file);
        if(upload) {
          imagesResource.refetch();
        }
      });
    }

    function submit(){
      if(!props.noDismiss)
        UIOverlay.dismiss();
      if(props.onResult && selected$())
        props.onResult(selected$()!);
    }

    function globalBack() {
      UIOverlay.dismiss();
      return true;
    }

    return (
      <div class={styles.container} use:focusScope={{
        trap: true,
        wrap: true,
        orientation: "spatial"
      }}> 
        <div class={styles.dialogHeader}>
          <div class={styles.headerText}>
            {props.title}
          </div>
          <div class={styles.headerSubText}>
            {props.description}
          </div>
          <div class={styles.closeButton} onClick={()=>UIOverlay.dismiss()}>
            <img src={iconClose} />
          </div>
        </div>
        <div style="margin-left: 15px;">
          <div class={styles.sectionTitle}>Custom Image</div>
          <div class={styles.sectionDescription}>Select a new or previously used custom image</div>
          <div class={styles.horizontalList}>
            <div class={styles.image} style="background: linear-gradient(267deg, rgb(1, 214, 230) -100.57%, rgb(1, 130, 231) 90.96%); margin-left: 0px;">
              <div onClick={()=>uploadImage()}
                  style="width: 100%; height: 100%; display: grid; align-items: center; justify-items: center; cursor: pointer"
                  use:focusable={{
                    onPress: uploadImage,
                    onBack: globalBack
                  }}>
                Add Image
              </div>
            </div>
            <div class={styles.image}
                onClick={()=>selectUrl(imageUrl$())}
                style={{"background-image": "url(" + imageUrl$() + ")"}}
                classList={{[styles.enabled]: isSelectedUrl(imageUrl$() ?? "nothing")}}
                use:focusable={{
                  onPress: () => selectUrl(imageUrl$()),
                  onBack: globalBack
                }}>
              
            </div>
            <For each={images$()}>{ img =>
              <div class={styles.image}
                style={{"background-image": "url(" + img + ")"}}
                classList={{[styles.enabled]: isSelectedUrl(img)}}
                onClick={()=>selectUrl(img)}
                use:focusable={{
                  onPress: () => selectUrl(img),
                  onBack: globalBack
                }} />
            }</For>
          </div>
          <div class={styles.sectionTitle}>Image Url</div>
          <div class={styles.sectionDescription}>Select a image from an url</div>
          <div style="display: flex; justify-content: center;">
            <InputText placeholder='Enter an image url' style={{"justify-content": "center"}} onTextChanged={(str)=>{setImageUrl(str); selectUrl(str)}} focusableOpts={{ onBack: globalBack }} />
            
          </div>
          <div class={styles.sectionTitle}>Creator Thumbnails</div>
          <div class={styles.sectionDescription}>Select a creator thumbnail as image</div>
          <div class={styles.subscriptionsContainer}>
            <For each={subscriptions$()}>{ sub =>
              <div class={styles.subscription} classList={{[styles.enabled]: isSelectedSubscription(sub.channel.url)}} onClick={()=>selectSubscription(sub.channel.url, sub.channel.thumbnail)} use:focusable={{
                  onPress: () => selectSubscription(sub.channel.url, sub.channel.thumbnail),
                  onBack: globalBack
                }}>
                <div class={styles.check}>
                  <img src={iconCheck} />
                </div>
                <div class={styles.image} style={{"background-image": "url(" + sub.channel.thumbnail + ")"}}>

                </div>
                <div class={styles.name}>
                  {sub.channel.name}
                </div>
              </div>
            }</For>
          </div>
        </div>
        <div style="height: 1px; background-color: rgba(255, 255, 255, 0.09); margin-top: 10px; margin-bottom: 10px;"></div>
        <div style="text-align: right">
            <Button text={"Select Image"}
              onClick={()=>submit()}
              style={{"margin-left": "auto", cursor: ("pointer")}} 
              color={"linear-gradient(267deg, #01D6E6 -100.57%, #0182E7 90.96%)"}
              focusableOpts={{
                onPress: submit,
                onBack: globalBack
              }} />
        </div>
      </div>
    );
  };
  
  export default OverlayImageSelector;