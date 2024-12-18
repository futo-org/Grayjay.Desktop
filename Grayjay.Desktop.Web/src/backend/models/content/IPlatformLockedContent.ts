import { IPlatformContent } from "./IPlatformContent";


export interface IPlatformLockedContent extends IPlatformContent {
    contentType: 70,
    contentName: string,
    contentThumbnails: IThumbnails,
    unlockUrl: string,
    lockDescription: string
}