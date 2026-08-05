import { Backend } from "./Backend";

export interface IBlockChannelRequest {
    url: string;
    name?: string;
    thumbnail?: string;
    pluginId?: string;
}

export abstract class BlockedChannelsBackend {
    static async list(): Promise<IBlockedChannel[]> {
        return await Backend.GET("/blockedchannels/List") as IBlockedChannel[];
    }

    static async isBlocked(url: string): Promise<boolean> {
        return await Backend.GET("/blockedchannels/IsBlocked?url=" + encodeURIComponent(url)) as boolean;
    }

    static async add(request: IBlockChannelRequest): Promise<IBlockedChannel> {
        return await Backend.POST("/blockedchannels/Add", JSON.stringify(request), "application/json") as IBlockedChannel;
    }

    static async remove(url: string): Promise<boolean> {
        return await Backend.GET("/blockedchannels/Remove?url=" + encodeURIComponent(url)) as boolean;
    }
}
