import { IPlatformVideo } from "../content/IPlatformVideo";

export interface IPlatformVideoDetails extends IPlatformVideo {
    rating: any;
    description: string;
    video: any;
    subtitles: any[];
    //preview
    isLive: boolean;
    isVOD: boolean;
}