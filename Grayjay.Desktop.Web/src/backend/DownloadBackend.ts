import { Backend } from "./Backend";
import { IPlatformVideo } from "./models/content/IPlatformVideo";
import { IStorageInfo } from "./models/downloads/IStorageInfo";
import { IVideoDownload } from "./models/downloads/IVideoDownload";
import { IVideoLocal } from "./models/downloads/IVideoLocal";
import { Pager } from "./models/pagers/Pager";


export abstract class DownloadBackend {


    static async loadDownloadSources(url: string) {
        return await Backend.GET("/download/LoadDownloadSources?url=" + encodeURIComponent(url)) as IDownloadSources;
    }
    static async download(id: string, videoIndex: number, audioIndex: number, subtitleIndex: number, manifestIndex: number): Promise<IVideoDownload> {
        return await Backend.GET(`/download/Download?id=${id}&videoIndex=${videoIndex}&audioIndex=${audioIndex}&subtitleIndex=${subtitleIndex}&manifestIndex=${manifestIndex}`) as IVideoDownload;
    }
    static async downloadPlaylist(playlistId: string, pixelCount?: number, bitrate?: number){
        return await Backend.GET(`/download/DownloadPlaylist?playlistId=${playlistId}&pixelCount=${pixelCount ?? -1}&bitrate=${bitrate ?? -1}`);
    }
    static async downloadMultiple(videos: IPlatformVideo[], pixelCount?: number, bitrate?: number) {
        return await Backend.POST(`/download/DownloadMultiple?pixelCount=${pixelCount ?? -1}&bitrate=${bitrate ?? -1}`,
            JSON.stringify(videos), "application/json"
        );
    }

    static async getStorageInfo(): Promise<IStorageInfo> {
        return await Backend.GET("/download/GetStorageInfo") as IStorageInfo;
    }
    static async getDownloading(): Promise<IVideoDownload[]> {
        return await Backend.GET("/download/GetDownloading") as IVideoDownload[];
    }
    static async getDownloadingPlaylists(): Promise<IPlaylistDownload> {
        return await Backend.GET("/download/GetDownloadingPlaylists") as IPlaylistDownload[];
    }
    static async getDownloaded(): Promise<IVideoLocal[]> {
        return await Backend.GET("/download/GetDownloaded") as IVideoLocal[];
    }

    static async deleteDownload(id: IPlatformID): Promise<boolean> {
        return await Backend.POST("/download/DeleteDownload", JSON.stringify(id), "application/json");
    }
    static async deleteDownloadPlaylist(id: string): Promise<boolean> {
        return await Backend.GET("/download/DeleteDownloadPlaylist?id=" + id);
    }

    static async exportDownload(id: IPlatformID): Promise<boolean> {
        return await Backend.POST("/download/ExportDownload", JSON.stringify(id), "application/json");
    }

    static async exportDownloads(ids: IPlatformID[]): Promise<boolean> {
        return await Backend.POST("/download/ExportDownloads", JSON.stringify(ids), "application/json");
    }


    static async downloadCycle(): Promise<boolean> {
        return await Backend.GET("/download/DownloadCycle");
    }

    static async changeDownloadDirectory() {
        Backend.GET("/download/ChangeDownloadDirectory");
    }
}