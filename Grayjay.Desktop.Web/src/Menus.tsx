import { MenuItemButton, MenuItemCheckbox, MenuItemToggle, MenuSeperator } from "./components/menus/Overlays/SettingsMenu";

import ic_notifications from './assets/icons/notifications.svg';
import ic_streams from './assets/icons/streams.svg';
import ic_videos from './assets/icons/videos.svg';
import ic_addToPlaylist from './assets/icons/icon_add_to_playlist.svg';
import ic_download from './assets/icons/icon24_download.svg';
import ic_trash from './assets/icons/icon_trash.svg';

import { ISourceConfigState } from "./backend/models/plugin/ISourceConfigState";
import UIOverlay from "./state/UIOverlay";
import { PlaylistsBackend } from "./backend/PlaylistsBackend";
import { IPlaylist } from "./backend/models/IPlaylist";

export class Menus {
    static getSubscriptionMenu(subscription: ISubscription, subscriptionSettings: ISubscriptionSettings, sourceState?: ISourceConfigState) {
        const hasStreams = (sourceState?.capabilitiesChannel?.types?.indexOf("STREAMS") ?? -1) !== -1;
        const hasVideos = (sourceState?.capabilitiesChannel?.types?.indexOf("VIDEOS") ?? -1) !== -1 
          || (sourceState?.capabilitiesChannel?.types?.indexOf("MIXED") ?? -1) !== -1
          || (sourceState?.capabilitiesChannel?.types?.length ?? 0) === 0;

        return {
            subscription,
            subscriptionSettings,
            menu: {
                title: "",
                items: [
                    new MenuItemToggle({
                        icon: ic_notifications,
                        name: "Enable notifications",
                        description: "Get notified about the latest videos Get notified about the latest videos",
                        isSelected: subscriptionSettings.doNotifications,
                        onToggle: (v) => {
                            subscriptionSettings.doNotifications = v;
                        }
                    }),
                    ...hasVideos || hasStreams ? [new MenuSeperator()] : [],
                    ...hasVideos ? [new MenuItemCheckbox({
                        icon: ic_videos,
                        name: "Check videos",
                        isSelected: subscriptionSettings.doFetchVideos,
                        onToggle: (v) => {
                            subscriptionSettings.doFetchVideos = v;
                        }
                    })] : [],
                    ...hasStreams ? [new MenuItemCheckbox({
                        icon: ic_streams,
                        name: "Check streams",
                        isSelected: subscriptionSettings.doFetchStreams,
                        onToggle: (v) => {
                            subscriptionSettings.doFetchStreams = v;
                        }
                    })] : []
                ]
            }
        };
    }

    static getPlaylistItems(id: string, afterRemove?: () => void) {
        return [
          new MenuItemButton("Rename", ic_addToPlaylist, undefined, () => {
    
          }),
          new MenuItemButton("Download", ic_download, undefined, async () => {
              const playlist = await PlaylistsBackend.get(id);
              UIOverlay.overlayDownloadPlaylist(playlist.id, (px, bitrate)=>{

              });
          }),
          new MenuSeperator(),
          new MenuItemButton("Remove", ic_trash, undefined, async () => {
            UIOverlay.overlayConfirm({
              yes: async () => {
                await PlaylistsBackend.delete(id)
                afterRemove?.();
              }
            });
          })
        ];
      };
}