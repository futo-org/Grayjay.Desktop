import { IPlatformContent } from "./IPlatformContent";


export interface IPlatformPost extends IPlatformContent {
    contentType: 2,
    thumbnails: IThumbnails[],
    images: string[],
    description: string
}