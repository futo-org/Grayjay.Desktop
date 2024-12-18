import { Backend } from "./Backend";
import { SyncDevice } from "./models/sync/SyncDevice";


export abstract class SyncBackend {
    static async getDevices(): Promise<SyncDevice[]> {
        return await Backend.GET("/sync/GetDevices");
    }
    static async getOnlineDevices(): Promise<SyncDevice[]> {
        return await Backend.GET("/sync/GetOnlineDevices");
    }

    static async hasAtLeastOneDevice(): Promise<boolean> {
        return await Backend.GET("/sync/HasAtLeastOneDevice");
    }


    static async getPairingUrl(): Promise<string | undefined> {
        return await Backend.GET_text("/sync/GetPairingUrl");
    }

    static async validateSyncDeviceInfoFormat(syncDeviceInfo: any): Promise<void> {
        await Backend.POST("/sync/ValidateSyncDeviceInfoFormat", JSON.stringify(syncDeviceInfo), "application/json");
    }

    static async addDevice(syncDeviceInfo: any): Promise<void> {
        await Backend.POST("/sync/AddDevice", JSON.stringify(syncDeviceInfo), "application/json");
    }

    static async removeDevice(publicKey: string): Promise<void> {
        await Backend.GET("/sync/RemoveDevice?publicKey=" + encodeURIComponent(publicKey));
    }



    static async sendToDevice(publicKey: string, url: string, position: number = 0): Promise<void> {
        await Backend.GET("/sync/SendToDevice?device=" + encodeURIComponent(publicKey) + "&url=" + encodeURIComponent(url) + "&position=" + Math.floor(position));
    }
}