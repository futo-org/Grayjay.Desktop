import { IPlatformContent } from "./IPlatformContent";


export interface IPlatformNestedMedia extends IPlatformContent {
    contentType: 11,
    contentUrl: string,
    contentName: string,
    contentDescription: string,
    contentProvider: string,
    contentThumbnails: IThumbnails,

    pluginId?: string,
    pluginName?: string,
    pluginThumbnail?: string
}