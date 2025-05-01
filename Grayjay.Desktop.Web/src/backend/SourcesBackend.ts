import { Backend } from "./Backend";
import IPluginPrompt from "./models/plugin/IPluginPrompt";
import { ISourceDetails } from "./models/plugin/ISourceConfig";
import { ISourceConfig, ISourceConfigState } from "./models/plugin/ISourceConfigState";
import { ISettingsObject } from "./models/settings/SettingsObject";



export abstract class SourcesBackend {

    static async sources(): Promise<ISourceConfig[]> {
        return await Backend.GET("/sources/Sources") as ISourceConfig[];
    }
    static async sourceStates(): Promise<ISourceConfigState[]> {
        return await Backend.GET("/sources/SourceStates") as ISourceConfigState[];
    }
    static async source(id: string): Promise<ISourceConfig> {
        return await Backend.GET("/sources/Source?id=" + id) as ISourceConfig;
    }
    static async sourceDetails(id: string): Promise<ISourceDetails> {
        return await Backend.GET("/sources/SourceDetails?id=" + id) as ISourceDetails;
    }
    static async sourceAppSettings(id: string): Promise<ISettingsObject> {
        const settings = await Backend.GET("/settings/SourceAppSettings?id=" + id) as ISettingsObject;
        settings.onSave = async () => {
            return await this.sourceAppSettingsSave(id, settings.object);
        };
        return settings;
    }
    static async sourceAppSettingsSave(id: string, obj: any): Promise<boolean> {
        return await Backend.POST("/settings/SourceAppSettingsSave?id=" + id, JSON.stringify(obj), "application/json");
    }
    static async sourceSettings(id: string): Promise<ISettingsObject> {
        const settings = await Backend.GET("/settings/SourceSettings?id=" + id) as ISettingsObject;
        settings.onSave = async () => {
            return await this.sourceSettingsSave(id, settings.object);
        };
        return settings;
    }
    static async sourceSettingsSave(id: string, obj: any): Promise<boolean> {
        return await Backend.POST("/settings/SourceSettingsSave?id=" + id, JSON.stringify(obj), "application/json");
    }

    static async sourceInstall(url: string): Promise<IPluginPrompt> {
        return await Backend.GET("/sources/SourceInstall?url=" + encodeURIComponent(url));
    }
    static async sourceInstallPrompt(url: string): Promise<IPluginPrompt> {
        return await Backend.GET("/sources/SourceInstallPrompt?url=" + encodeURIComponent(url));
    }
    static async sourceDelete(id: string): Promise<boolean> {
        return await Backend.GET("/sources/SourceDelete?id=" + encodeURIComponent(id));
    }

    static async sourcesReorder(ids: string[]): Promise<any> {
        return await Backend.POST("/sources/SourcesReorder", JSON.stringify(ids), "application/json") as any;
    }

    static async enabledSources(): Promise<ISourceConfig[]> {
        return await Backend.GET("/sources/SourcesEnabled") as ISourceConfig[];
    }
    static async disabledSources(): Promise<ISourceConfig[]> {
        return await Backend.GET("/sources/SourcesDisabled") as ISourceConfig[];
    }

    static async enableSource(id: string) {
        await Backend.GET("/sources/SourceEnable?id=" + id);
    }
    static async disableSource(id: string) {
        await Backend.GET("/sources/SourceDisable?id=" + id);
    }

    static async officialSources(): Promise<ISourceConfig[]> {
        return await Backend.GET("/sources/OfficialPlugins") as ISourceConfig[];
    }
    static async installOfficialSources(ids: string[]): Promise<any> {
        return await Backend.POST("/sources/InstallOfficialPlugins", JSON.stringify(ids), "application/json") as any;
    }
    static async sourceInstallPeerTubePrompt(url: string): Promise<IPluginPrompt> {
        return await Backend.POST("/sources/SourceInstallPeerTubePrompt", JSON.stringify(url), "application/json") as any;
    }
    
    static login(id: string) {
        Backend.GET("/sources/SourceLogin?id=" + id);
    }
    static loginDevClone() {
        Backend.GET("/sources/SourceLoginDevClone");
    }
    
    static async logout(id: string) {
        return await Backend.GET("/sources/SourceLogout?id=" + id);
    }
}