import { Backend } from "./Backend";
import { IPlatformContent } from "./models/content/IPlatformContent";
import { IPlatformPlaylistDetails } from "./models/content/IPlatformPlaylistDetails";
import { Pager } from "./models/pagers/Pager";

export abstract class PlaylistBackend {
    static async playlistLoad(url: string): Promise<IPlatformPlaylistDetails> {
        return await Backend.GET("/playlist/PlaylistLoad?url=" + encodeURIComponent(url)) as IPlatformPlaylistDetails;
    }

    static async contentsLoad(): Promise<PagerResult<IPlatformContent>> {
        return await Backend.GET("/playlist/ContentsLoad");
    }

    static async contentsNextPage(): Promise<PagerResult<IPlatformContent>> {
        return await Backend.GET("/playlist/ContentsNextPage");
    }

    static async contentsPager(): Promise<Pager<IPlatformContent>> {
        return Pager.fromMethods<IPlatformContent>(this.contentsLoad, this.contentsNextPage);
    }

    static async convertToLocalPlaylist(): Promise<string> {
        return await Backend.GET("/playlist/ConvertToLocalPlaylist") as string;
    }
}