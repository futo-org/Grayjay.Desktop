import { IDialogOutput } from "../overlays/OverlayDialog";
import { Backend } from "./Backend";
import { ISettingsObject } from "./models/settings/SettingsObject";


export interface IHandlePlan {
    type: string,
    data: string
}

export abstract class HandlingBackend {

    static async handlePlan(url: string): Promise<IHandlePlan> {
        return await Backend.GET("/handle/GetHandlePlan?url=" + url);
    }
}