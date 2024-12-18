import { Backend } from "./Backend";
import { IPlatformVideo } from "./models/content/IPlatformVideo";

export interface IOrderedPlatformVideo extends IPlatformVideo {
    index: number;
}

export abstract class WatchLaterBackend {
    static async getAll(): Promise<IOrderedPlatformVideo[]> {
        return await Backend.GET("/watchlater/GetAll") as IOrderedPlatformVideo[];
    }
    
    //Obsolete
    static async setAll(videos: IOrderedPlatformVideo[]): Promise<void> {
        await Backend.POST("/watchlater/SetAll", JSON.stringify(videos), "application/json");
    }

    static async changeOrder(videosOrder: String[]): Promise<void> {
        await Backend.POST("/watchlater/ChangeOrder", JSON.stringify(videosOrder), "application/json");
    }

    static async add(video: IPlatformVideo): Promise<void> {
        await Backend.POST("/watchlater/Add", JSON.stringify(video), "application/json");
    }

    static async remove(url: string): Promise<void> {
        await Backend.GET("/watchlater/Remove?url=" + encodeURIComponent(url));
    }
}