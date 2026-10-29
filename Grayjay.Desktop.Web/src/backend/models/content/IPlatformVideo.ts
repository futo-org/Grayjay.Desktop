import { IPlatformContent } from "./IPlatformContent";


export interface IPlatformVideo extends IPlatformContent {
    contentType: 1,
    thumbnails: IThumbnails;
    duration: number;
    viewCount: number;

    isLive: boolean;
    metadata?: {
        position?: number;
        watched?: boolean;
        [key: string]: any;
    };
}