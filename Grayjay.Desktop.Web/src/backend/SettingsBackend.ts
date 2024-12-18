import { Backend } from "./Backend";
import { ISettingsObject } from "./models/settings/SettingsObject";

export abstract class SettingsBackend {

    static async persistGet(key: string, def?: any): Promise<any> {
        const obj = JSON.parse(await Backend.GET("/settings/PersistGet?key=" + key)) ?? def;
        console.info("persistGet", {key, obj});
        return obj;
    }
    static async persistSet(key: string, obj: any) {
        console.info("persistSet", {key, obj});
        await Backend.POST("/settings/PersistSet?key=" + key, JSON.stringify(obj), "application/json") as any;
    }
    
    static async settings(): Promise<ISettingsObject> {
        const settings = await Backend.GET("/settings/Settings") as ISettingsObject;
        settings.onSave = async () => {
            return await this.settingsSave(settings.object);
        };
        return settings;
    }
    static async settingsSave(obj: any): Promise<boolean> {
        return await Backend.POST("/settings/SettingsSave", JSON.stringify(obj), "application/json");
    }

    static async dismissSubscriptionGroups() {
        await Backend.GET("/settings/SubscriptionGroupsDismiss") as any;
    }
    
}