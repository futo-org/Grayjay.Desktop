import { Backend } from "./Backend";
import { IPlatformVideo } from "./models/content/IPlatformVideo";
import { Pager } from "./models/pagers/Pager";
import { RefreshPager } from "./models/pagers/RefreshPager";

export abstract class DeveloperBackend {

    static async isDeveloper(): Promise<boolean> {
        return await Backend.GET("/Developer/IsDeveloper") as boolean;
    }
}