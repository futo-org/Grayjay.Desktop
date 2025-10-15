import { createRoot } from "solid-js";
import { OverlayRequest } from "../overlays/OverlayModals";
import { Event0, Event1 } from "../utility/Event"
import ExceptionModel from "../backend/exceptions/ExceptionModel";
import { DialogButton, DialogDescriptor, DialogInputCheckboxList, DialogInputText, IDialogOutput } from "../overlays/OverlayDialog";

import iconError from "../assets/icons/icon_error.svg";
import { generateUUID } from "../utility";
import OverlayDownloadDialog from "../overlays/OverlayDownloadDialog";
import OverlaySubscriptionsSelector from "../overlays/OverlaySubscriptionsSelector";
import OverlayImageSelector from "../overlays/OverlayImageSelector";
import { SubscriptionsBackend } from "../backend/SubscriptionsBackend";
import { IPlaylist } from "../backend/models/IPlaylist";
import { PlaylistsBackend } from "../backend/PlaylistsBackend";
import OverlayImportSelectDialog from "../overlays/OverlayImportSelectDialog";
import { IPlatformVideoDetails } from "../backend/models/contentDetails/IPlatformVideoDetails";
import { Navigate, useNavigate } from "@solidjs/router";
import OverlaySettings from "../overlays/OverlaySettings";
import OverlayShareDialog from "../overlays/OverlayShareDialog";
import { IPlatformVideo } from "../backend/models/content/IPlatformVideo";
import OverlaySubscriptionGroupEditDialog from "../overlays/OverlaySubscriptionGroupEdit";
import OverlayOfficialPluginsDialog from "../overlays/OverlayOfficialPluginsDialog";
import { SourcesBackend } from "../backend/SourcesBackend";


import iconCodeEditBlue from "../assets/icons/icon_code_edit_blue.svg"
import iconCheck from "../assets/icons/icon_checkmark.svg"
import iconWarning from "../assets/icons/icon_warning.svg"
import OverlaySourceInstall from "../overlays/OverlaySourceInstall";
import OverlaySyncNewDeviceDialog from "../overlays/OverlaySyncNewDeviceDialog";
import { SyncDevice } from "../backend/models/sync/SyncDevice";
import OverlaySelectOnlineSyncDeviceDialog from "../overlays/OverlaySelectOnlineSyncDevice";
import OverlayDownloadMultipleDialog from "../overlays/OverlayDownloadMultipleDialog";
import OverlayImage from "../overlays/OverlayImage";
import IPluginPrompt from "../backend/models/plugin/IPluginPrompt";
import StateGlobal from "./StateGlobal";
import OverlayFilePicker, { PickerSelectionMode } from "../overlays/OverlayFilePicker";

export interface IExceptionDialogHandlers {
  back?: () => void,
  ok?: () => void,
  retry?: () => void
}

export interface IConfirmationDialogHandlers {
  no?: () => void;
  yes: () => void;
}

export interface UIOverlay {
    onOverlay: Event1<OverlayRequest>
    onDismiss: Event1<string>,

    dialog: (dialog: DialogDescriptor) => Promise<IDialogOutput>,
    overlay: (arg0: OverlayRequest) => void,
    overlayError: (arg0: ExceptionModel)=>void,
    dismiss: ()=>void
  };
  function createUIOverlay() {
    console.log("Initializing UIOverlay");
  
    return {
      onOverlay: new Event1<OverlayRequest>(),
      onDismiss: new Event1<string>(),
      currentOverlay: (undefined as (OverlayRequest | undefined)),
      dialog(req: DialogDescriptor): Promise<IDialogOutput|undefined> {
        const reqId = generateUUID()
        return new Promise((resolve, reject)=>{
          this.overlay({
            id: reqId,
            dialog: req
          });
          this.onDismiss.register((id: string)=>{
            console.log("this.onDismiss.invoked", {reqId, req, id});
            if(reqId != id)
              return;
            resolve(req.output);
            this.onDismiss.unregister(reqId);
          }, reqId);
        });
      },
      toast(text: string) {
        StateGlobal.toast({text: text});
      },
      toastTitled(title: string, text: string) {
        StateGlobal.toast({title: title, text: text});
      },
      overlay(req: OverlayRequest): OverlayRequest {
        this.onOverlay.invoke(req);
        return req;
      },
      overlayError(exceptionModel: ExceptionModel, handlers: IExceptionDialogHandlers | undefined = undefined) {
        this.overlay({
          dialog: {
            icon: iconError,
            title: exceptionModel.title,
            description: exceptionModel.message,
            code: exceptionModel.code,

            buttons: [
              (handlers?.back) ? { title: "Back", onClick: ()=>{handlers?.back!()}, style: "none" } : { title: "Back", onClick: ()=>{(handlers?.ok ? handlers?.ok!() : null)}, style: "none" },
              (handlers?.retry) ? { title: "Retry", onClick: ()=>{handlers?.retry!()}, style: "primary" } : null
            ].filter(x=>x) as DialogButton[]
          }
        })
      },
      overlayConfirm(handlers: IConfirmationDialogHandlers, text?: string) {
        this.overlay({
          dialog: {
            title: "Please confirm",
            description: text ?? "Are you sure you want to remove this?",
            buttons: [
              { title: "No", onClick: () => handlers?.no?.(), style: "none" },
              { title: "Yes", onClick: () => handlers.yes(), style: "primary" }
            ].filter(x => x) as DialogButton[]
          }
        })
      },
      overlayImage(img: string) {
        this.overlay({
          custom: () => (
            <OverlayImage img={img} />
          )
        })
      },
      overlayOpenFilePicker(onPick?: (paths: string[]) => void, selectionMode?: PickerSelectionMode, allowMultiple?: boolean, filters?: { name: string, pattern: string }[]) {
        this.overlay({
          custom: () => (
            <OverlayFilePicker allowMultiple={allowMultiple} selectionMode={selectionMode} onPick={onPick} mode="open" filters={filters} />
          )
        })
      },
      overlaySaveFilePicker(onPick?: (paths: string[]) => void, defaultFileName?: string, filters?: { name: string, pattern: string }[]) {
        this.overlay({
          custom: () => (
            <OverlayFilePicker defaultFileName={defaultFileName} onPick={onPick} mode="save" filters={filters} />
          )
        })
      },
      overlayDownloadUrl(url?: string, onResult?: (video: number, audio: number, sub: number)=>void) {
        this.overlay({
          custom: () => (
            <OverlayDownloadDialog url={url} onResult={onResult} />
          )
        });
      },
      overlayDownload(contentUrl?: string) {
        if(!contentUrl)
          return;
        this.overlayDownloadUrl(contentUrl, async (video, audio, subtitles) => {
            try {
                this.toast("Download [" + contentUrl + "] started");
            }
            catch (ex) {
                this.dialog({
                    icon: iconError,
                    title: "Error",
                    description: "" + ex,
                    buttons: [
                        {
                            title: "Ok",
                            onClick: () => { }
                        }
                    ]
                })
            }
        });
      },
      overlayDownloadPlaylist(playlistId: string, onResult?: (videoPixelCount: number, audioBitrate: number)=>void) {
        this.overlay({
          custom: () => (
            <OverlayDownloadMultipleDialog playlistId={playlistId} onResult={onResult} />
          )
        })
      },
      overlayDownloadMultiple(videos: IPlatformVideo[], onResult?: (videoPixelCount: number, audioBitrate: number)=>void) {
        this.overlay({
          custom: () => (
            <OverlayDownloadMultipleDialog videos={videos} onResult={onResult} />
          )
        })
      },
      overlaySettings() {
        this.overlay({
          custom: () => (
            <OverlaySettings />
          )
        });
      },
      overlayImportSelect() {
        this.overlay({
          custom: () => (
            <OverlayImportSelectDialog />
          )
        });
      },
      overlayNewDeviceSync() {
        this.overlay({
          custom: () => (
            <OverlaySyncNewDeviceDialog />
          )
        });
      },
      overlayShare(str?: string) {
        if (!str) {
          return;
        }
        
        this.overlay({
          custom: () => (
            <OverlayShareDialog text={str} />
          )
        });
      },
      overlaySubscriptionSelector(title: string, description: string, ignore: string[], onResponse: (selected: string[])=>void) {
        this.overlay({
          custom: () => (
            <OverlaySubscriptionsSelector title={title} description={description} ignore={ignore} onResult={(results)=>onResponse(results)} />
          )
        });
      },
      overlayImageSelector(title: string, description: string, channels: string[], onResult: (selected: IImageVariable)=>void) {
        this.overlay({
          custom: () => (
            <OverlayImageSelector title={title} description={description} channels={channels} onResult={onResult} />
          )
        });
      },
      overlaySelectOnlineSyncDevice(title: string, description: string, onResult: (selected: SyncDevice)=>void) {
        this.overlay({
          custom: () => (
            <OverlaySelectOnlineSyncDeviceDialog title={title} description={description} act={onResult} />
          )
        });
      },

      async installPluginDialog(prompt: IPluginPrompt) {
        this.overlay({
          onGlobalDismiss: ()=>{
            this.dismiss();
          },
          custom: () => <OverlaySourceInstall
              prompt={prompt}
              onInstall={async ()=>{
                
                if(prompt.alreadyInstalled) {
                    const alreadyInstalledDialog = await this.dialog({
                      icon: iconWarning,
                      title: prompt.config.name + " is already installed",
                      description: "This plugin is already installed, would you like to reinstall?\nSettings and login should stay the same.",
                      buttons: [ { title: "Cancel", onClick: ()=>{ } }, { title: "Install Anyway", style: "primary", onClick: ()=>{ } } ]
                    });
                    if((alreadyInstalledDialog?.button ?? 0) <= 0)
                      return;
                }
                else if(prompt.warnings.length > 0) {
                  const warningsDialog = await this.dialog({
                    icon: iconWarning,
                    title: "This plugin has warnings, are you sure you want to install it.",
                    description: "Installing plugins with warnings may expose you to vunerability or malicious behavior.",
                    buttons: [ { title: "Cancel", onClick: ()=>{ } }, { title: "Install Anyway", style: "primary", onClick: ()=>{ } } ]
                  });
                  if((warningsDialog?.button ?? 0) <= 0)
                    return;
                }

                const onDismiss = new Event1<string>();
                const overlayObj = this.overlay({
                  loader: {
                    icon: iconCodeEditBlue,
                    title: "Installing source",
                    description: "Please wait until the plugin is installed",
                    onDismiss: onDismiss,
                  }
                });
                
                let error = undefined;
                try {
                  const result = await SourcesBackend.sourceInstall(prompt.config.sourceUrl);
                }
                catch(ex) {
                  error = ex;
                }
                finally {
                  console.info("onDismiss invoke", overlayObj);
                  onDismiss.invoke(overlayObj.id ?? "");
                }

                if(error) {
                  this.overlay({
                    dialog: {
                      icon: iconError,
                      title: "Failed to install " + prompt.config.name,
                      description: String(error),
                      buttons: [{ title: "Ok", style: "primary", onClick: ()=>{} } ]
                    } as DialogDescriptor
                  });
                }
                else {
                  this.overlay({
                    dialog: {
                      icon: iconCheck,
                      title: "Plugin " + prompt.config.name + " Installed",
                      description: "You can now enable the source in the sources tab.",
                      buttons: [
                        { title: "Ok", style: "none", onClick: ()=>{} },
                        { title: "Sources", style: "primary", onClick: ()=>{ nav("/web/sources") } } 
                      ]
                    } as DialogDescriptor
                  });
                }
              }}
            />
        });
      },
      async installPluginPrompt(url: string) {
        try {
          const prompt = await SourcesBackend.sourceInstallPrompt(url);
          this.installPluginDialog(prompt);
        }
        catch(ex) {
          if(ex instanceof ExceptionModel) {
              this.overlayError((ex as ExceptionModel), { });
          }
          else
          this.overlay({
              dialog: {
                icon: iconError,
                title: "Invalid Config",
                description: url + "\n" + String(ex),
                buttons: [{ title: "Ok", style: "primary", onClick: ()=>{} } ]
              } as DialogDescriptor
            });
        }
      },

      overlaySubscriptionGroupAddCreators(ignore: string[], onResponse: (selected: string[])=>void) {
        this.overlaySubscriptionSelector("Add creators to subscription group",
          "Expand your subscription experience by inviting your favorite creators to join your group. Enjoy exclusive content and support the creators you love",
          ignore,
          onResponse);
      },
      overlayNewSubscriptionGroup(onCreated?: (group: ISubscriptionGroup)=>void) {
        //Enter name
        this.overlay({
          dialog: {
            title: "Create new subscription group",
            description: "Your personalized way to organize and enjoy content from multiple creators. Stay organized and engaged with your favorite creators.",
            input: new DialogInputText("Type in group name"),
            buttons: [{
              title: "Continue",
              style: "primary",
              onClick: (output: IDialogOutput)=>{
                console.log("Create a subscription group", output.text);
                if(output.text) {
                  //Select subscriptions
                  this.overlaySubscriptionGroupAddCreators([], (selected)=>{
                    if(selected && selected.length > 0) {
                      //Select image
                      this.overlayImageSelector("Subscription Group Cover",
                        "Select an cover for your subscription group.",
                        selected, (img)=>{
                          const group = {
                            name: output.text,
                            image: img,
                            urls: selected,
                          } as ISubscriptionGroup;
                          SubscriptionsBackend.subscriptionGroupSave(group);
                          if(onCreated)
                            onCreated(group);
                        });
                    }
                  });
                }
              }
            }]
          }
        });
      },
      overlayEditSubscriptionGroup(group: ISubscriptionGroup) {
        const copy = { ...group};
        //Enter name
        this.overlay({
          custom: () => (
            <OverlaySubscriptionGroupEditDialog subscriptionGroup={copy}  />
          )
        });
      },
      overlayOfficialPlugins() {
        //Enter name
        this.overlay({
          custom: () => (
            <OverlayOfficialPluginsDialog />
          )
        });
      },
      overlayTextPrompt(title: string, description: string, placeholder: string, buttonText: string, onText: (str: string) => void) {
        this.overlay({
          dialog: {
            title: title,
            description: description,
            input: new DialogInputText(placeholder),
            buttons: [{
              title: buttonText,
              style: "primary",
              onClick: async (output: IDialogOutput) => {
                if(output.text) {
                  onText(output.text);
                }
              }
            }]
          }
        });
      },
      overlayNewPlaylist(onCreated?: (playlist: IPlaylist) => void) {
        this.overlay({
          dialog: {
            title: "Create new playlist",
            description: "Group content together in a playlist.",
            input: new DialogInputText("Type in playlist name"),
            buttons: [{
              title: "Create",
              style: "primary",
              onClick: async (output: IDialogOutput) => {
                if(output.text) {
                  const playlist = {
                    id: crypto.randomUUID(),
                    name: output.text,
                    videos: []
                  };
                  
                  await PlaylistsBackend.createOrupdate(playlist);
                  onCreated?.(playlist);
                }
              }
            }]
          }
        });
      },
      async overlayAddToPlaylist(content: IPlatformVideo, onAdded?: (playlists: IPlaylist[]) => void) {
        const playlists = await PlaylistsBackend.getAll();
        const addContentToPlaylists = (playlists: IPlaylist[]) => {

        };
        
        this.overlay({
          dialog: {
            title: "Add to playlist",
            description: "Add content to a playlist.",
            input: new DialogInputCheckboxList({
              values: playlists.map(p => {
                return {
                  text: p.name,
                  value: p
                };
              }), 
              addLabel: "Create new playlist",
              onAddClicked: () => {
                this.overlayNewPlaylist(() => {
                  this.overlayAddToPlaylist(content, onAdded);
                  this.dismiss();
                });
                this.dismiss();
              }
            }),
            buttons: [{
              title: "Add",
              style: "primary",
              onClick: async (output: IDialogOutput) => {
                console.info("Add to selected playlists", output.selected);
                const playlists = output.selected as IPlaylist[];
                await PlaylistsBackend.addContentToPlaylists(content, playlists.map(p => p.id));
                onAdded?.(playlists);
              }
            }]
          }
        });
      },
      dismiss(id?: string | undefined) {
        this.onDismiss.invoke(id ?? this.currentOverlay?.id ?? "");
      },
      async catchDialogExceptions<T>(action: ()=>T, back: (()=>void) | null | undefined, retry: ()=>void, _beforeDialog: ()=>void | null | undefined = undefined): Promise<T> {
        try {
          return await action();
        }
        catch (error: any) {
            if (error && error) {
              if (error instanceof ExceptionModel) {
                if(_beforeDialog)
                  _beforeDialog();
                  if(error.typeName == "ScriptUnavailableException") {
                      this.overlayError((error as ExceptionModel), {
                          back: back ?? undefined,
                          retry: () => {
                            retry();
                          }
                      });
                  }
                  else if(error.typeName == "ScriptLoginRequiredException") {
                    this.dialog({
                      icon: iconError,
                      title: "[" + error.pluginName + "] Login required",
                      description: error.message,
                      buttons: [
                        {
                          title: "Back",
                          onClick: back,
                        } as DialogButton,
                        {
                          title: "Login",
                          onClick: ()=>{
                            SourcesBackend.login(error.pluginID!);
                            if(back)
                              back();
                            //TODO: Navigate
                          },
                          style: "primary"
                        } as DialogButton
                      ]
                    } as DialogDescriptor)
                  }
                  else
                    this.overlayError((error as ExceptionModel), {
                          back: back ?? undefined,
                          retry: () => {
                            retry();
                          }
                      });
              }
              else if(error.message && error.message.indexOf("due to: [500]")) {
                if(_beforeDialog)
                  _beforeDialog();
                this.overlayError({
                    title: ((error.pluginName) ? "[" + error.pluginName + "]" : "") + "Uncaught Exception",
                    message: error.message,
                    type: "Unknown",
                    canRetry: !!retry,
                    code: "",
                    name: "Uncaught Exception"
                } as ExceptionModel, {
                  back: back ?? undefined,
                  retry: () => {
                    retry();
                  }
                })
              }
            }
            throw error;
        }
      }
    };
  }
  export default createRoot(createUIOverlay);