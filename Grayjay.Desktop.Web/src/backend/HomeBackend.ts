import { Backend } from "./Backend";
import { IPlatformVideo } from "./models/content/IPlatformVideo";
import { Pager } from "./models/pagers/Pager";
import { RefreshPager } from "./models/pagers/RefreshPager";

export abstract class HomeBackend {

    static async homeLoad(): Promise<PagerResult<IPlatformVideo>> {
        return await Backend.GET("/home/HomeLoad") as PagerResult<IPlatformVideo>;
    }
    static async homeLoadLazy(): Promise<PagerResult<IPlatformVideo>> {
        return await Backend.GET("/home/HomeLoadLazy") as PagerResult<IPlatformVideo>;
    }

    static async homeNextPage(): Promise<PagerResult<IPlatformVideo>> {
        return await Backend.GET("/home/HomeNextPage") as PagerResult<IPlatformVideo>;
    }

    static async homePager(): Promise<Pager<IPlatformVideo>> {
        const result = Pager.fromMethods<IPlatformVideo>(this.homeLoad, this.homeNextPage);
        //Temporary 2 pages
        (await result).nextPage();
        return result;
    }
    static async homePagerLazy(): Promise<RefreshPager<IPlatformVideo>> {
        const result = RefreshPager.fromMethodsRefresh<IPlatformVideo>("home", this.homeLoadLazy, this.homeNextPage);
        (await result).nextPage();
        return result;
    }

}