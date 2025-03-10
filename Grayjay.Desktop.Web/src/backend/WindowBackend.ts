import { Backend } from "./Backend";
import { IPlatformVideo } from "./models/content/IPlatformVideo";

export interface IOrderedPlatformVideo extends IPlatformVideo {
    index: number;
}

export abstract class WindowBackend {
    static async startWindow(): Promise<Boolean> {
        return await Backend.GET("/window/startWindow")
    }

    static async ready(): Promise<boolean> {
        return await Backend.GET("/window/Ready");
    }

    static async delay(ms: number): Promise<boolean> {
        return await Backend.GET("/window/Delay?ms=" + ms);
    }

    static async echo(str: string): Promise<boolean> {
        await Backend.GET("/window/Echo?str=" + str);
        return true;
    }
}