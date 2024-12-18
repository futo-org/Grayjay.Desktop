import { Backend } from "./Backend";
import { IPlatformVideo } from "./models/content/IPlatformVideo";
import { Pager } from "./models/pagers/Pager";
import { RefreshPager } from "./models/pagers/RefreshPager";

export abstract class ImportBackend {

    static importZip() {
        Backend.GET("/import/ImportZip");
    }
    static importNewPipe() {
        Backend.GET("/import/ImportNewPipe");
    }


    static async getUserSubscriptions(id: string): Promise<string[]>{
        return await Backend.GET("/import/GetUserSubscriptions?id=" + id) as string[];
    }
    static async getUserPlaylists(id: string): Promise<string[]>{
        return await Backend.GET("/import/getUserPlaylists?id=" + id) as string[];
    }


    static async importSubscriptions(urls: string[]) {
        return await Backend.POST("/import/ImportSubscriptions", JSON.stringify(urls), "text/json") as boolean;
    }

    static async importPlaylists(urls: string[]) {
        return await Backend.POST("/import/ImportPlaylists", JSON.stringify(urls), "text/json") as boolean;
    }
}