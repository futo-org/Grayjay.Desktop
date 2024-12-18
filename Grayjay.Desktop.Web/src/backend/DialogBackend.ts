import { IDialogOutput } from "../overlays/OverlayDialog";
import { Backend } from "./Backend";
import { ISettingsObject } from "./models/settings/SettingsObject";



export abstract class DialogBackend {

    
    static async dialogRespond(id: string, dialog: IDialogOutput): Promise<boolean> {
        return await Backend.POST("/dialog/DialogRespond?id=" + id, JSON.stringify(dialog), "application/json") as boolean;
    }
    static async dialogRespondCustom(id: string, action: string, obj: any): Promise<boolean> {
        return await Backend.POST("/dialog/DialogRespondCustom?id=" + id + "&actionName=" + action, JSON.stringify(obj), "application/json") as boolean;
    }
}