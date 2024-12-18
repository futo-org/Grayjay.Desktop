import { IPlatformContent } from "../content/IPlatformContent";
import { IPlatformVideoDetails } from "../contentDetails/IPlatformVideoDetails";

export interface IVideoLocal extends IPlatformContent  {
    videoDetails: IPlatformVideoDetails,

    videoSources: any[],
    audioSources: any[],
    subtitleSources: any[],

    groupID: string,
    groupType: string
}