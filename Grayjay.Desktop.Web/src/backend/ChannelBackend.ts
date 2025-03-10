import { Backend } from "./Backend";
import { IPlatformContent } from "./models/content/IPlatformContent";
import { Pager } from "./models/pagers/Pager";

export abstract class ChannelBackend {
    static async channelLoad(url: string): Promise<ISerializedChannel> {
        return await Backend.GET("/channel/Channel?url=" + encodeURIComponent(url)) as ISerializedChannel;
    }
    static async CanSearchChannel(url: string): Promise<boolean> {
        return await Backend.GET("/channel/CanSearchChannel?url=" + encodeURIComponent(url)) as boolean;
    }
    static async channelContentLoad(url?: string): Promise<PagerResult<IPlatformContent>> {
        return await Backend.GET("/channel/ChannelContentLoad?url=" + encodeURIComponent(url ?? "")) as PagerResult<IPlatformContent>;
    }
    static async channelContentLoadSearch(url: string, query: string): Promise<PagerResult<IPlatformContent>> {
        return await Backend.GET("/channel/ChannelContentLoadSearch?query=" + encodeURIComponent(query) + "&url=" + encodeURIComponent(url)) as PagerResult<IPlatformContent>;
    }
    static async channelContentSearchPager(url:string, query: string): Promise<Pager<IPlatformContent>> {
        return Pager.fromMethods<IPlatformContent>(() => this.channelContentLoadSearch(url, query), this.channelContentNextPage);
    }
    static async channelContentNextPage(): Promise<PagerResult<IPlatformContent>> {
        return await Backend.GET("/channel/ChannelContentNextPage") as PagerResult<IPlatformContent>;
    }
    static async channelContentPager(url: string): Promise<Pager<IPlatformContent>> {
        const result = Pager.fromMethods<IPlatformContent>(()=>this.channelContentLoad(url), this.channelContentNextPage);
        //TODO: Temporary 2 pages
        (await result).nextPage();
        return result;
    }


}