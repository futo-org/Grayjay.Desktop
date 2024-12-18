import { Backend } from "./Backend";
import { IPlatformVideo } from "./models/content/IPlatformVideo";
import { Pager } from "./models/pagers/Pager";
import { RefreshPager } from "./models/pagers/RefreshPager";

export abstract class BuyBackend {

    static async openBuy() {
        Backend.GET("/Buy/OpenBuy");
    }
    static async didPurchase(): Promise<boolean> {
        return await Backend.GET("/Buy/DidPurchase") as boolean;
    }
    static async getPrice(): Promise<string> {
        return await Backend.GET("/Buy/GetPrice") as string;
    }
    static async setActivation(licenseKey: string, activationKey: string) : Promise<boolean> {
        return await Backend.POST("/Buy/SetActivation", JSON.stringify([licenseKey, activationKey]), "application/json");
    }
    static async setLicenseUrl(licenseUrl: string) : Promise<boolean> {
        return await Backend.POST("/Buy/SetLicenseUrl", JSON.stringify(licenseUrl), "application/json");
    }
    static async setLicense(licenseKey: string) : Promise<boolean> {
        return await Backend.POST("/Buy/SetLicense", JSON.stringify(licenseKey), "application/json");
    }
    static async clearLicense() : Promise<boolean> {
        return await Backend.POST("/Buy/ClearLicense", JSON.stringify("[]"), "application/json");
    }
}