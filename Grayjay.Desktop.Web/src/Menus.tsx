import { MenuItemButton, MenuItemCheckbox, MenuItemToggle, MenuSeperator } from "./components/menus/Overlays/SettingsMenu";

import ic_notifications from './assets/icons/notifications.svg';
import ic_streams from './assets/icons/streams.svg';
import ic_videos from './assets/icons/videos.svg';
import ic_addToPlaylist from './assets/icons/icon_add_to_playlist.svg';
import ic_subscriptions from './assets/icons/icon_nav_subscriptions.svg';
import ic_download from './assets/icons/icon24_download.svg';
import ic_trash from './assets/icons/icon_trash.svg';
import ic_hide from './assets/icons/icon24_hide.svg';

import { ISourceConfigState } from "./backend/models/plugin/ISourceConfigState";
import UIOverlay from "./state/UIOverlay";
import { PlaylistsBackend } from "./backend/PlaylistsBackend";
import { IPlaylist } from "./backend/models/IPlaylist";
import { SubscriptionsBackend } from "./backend/SubscriptionsBackend";
import StateBlockedChannels from "./state/StateBlockedChannels";

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
                    new MenuItemButton("Unsubscribe", ic_subscriptions, undefined, async () => {
                        /*UIOverlay.overlayConfirm({
                            yes: async () => {
                                await SubscriptionsBackend.unsubscribe(subscription.channel.url);
                            }
                        }, "Are you sure you want to unsubscribe?");*/
                        await SubscriptionsBackend.unsubscribe(subscription.channel.url);
                    }),
                    new MenuItemButton(StateBlockedChannels.isBlocked(subscription.channel.url) ? "Unblock channel" : "Block channel", ic_hide, undefined, async () => {
                        const channel = subscription.channel;
                        if (StateBlockedChannels.isBlocked(channel.url)) {
                            await StateBlockedChannels.unblock(channel.url);
                        }
                        else {
                            UIOverlay.overlayConfirm({
                                no: () => { },
                                yes: async () => {
                                    await StateBlockedChannels.block(channel.url, channel.name, channel.thumbnail, channel.id?.pluginID);
                                }
                            }, "Block this channel? Content from this channel will no longer appear in your home feed, and videos from it will not play.");
                        }
                    }),
                    new MenuSeperator(),
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
              UIOverlay.overlayTextPrompt("Playlist rename", "Enter the new name for the playlist.", "Some name", "Rename", async (str)=>{
                if(str && str.length > 0)
                    await PlaylistsBackend.renamePlaylist(id, str);
              });
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