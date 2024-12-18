import { Backend } from "./Backend";
import { Pager } from "./models/pagers/Pager";


export abstract class PlatformBackend {

    static async channel(url: string): Promise<ISerializedChannel> {
        return await Backend.GET("/platform/Home?url=" + url) as ISerializedChannel;
    }

}