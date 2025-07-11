import { Duration } from "luxon";
import { CastingDeviceInfo } from "../contexts/Casting";
import { Backend } from "./Backend";
import { SourceSelected } from "../components/contentDetails/VideoDetailView";


export abstract class CastingBackend {

    static async discoveredDevices(): Promise<CastingDeviceInfo[]> {
        return await Backend.GET("/casting/DiscoveredDevices") as CastingDeviceInfo[]
    }

    static async pinnedDevices(): Promise<CastingDeviceInfo[]> {
        return await Backend.GET("/casting/PinnedDevices") as CastingDeviceInfo[]
    }

    static async addPinnedDevice(deviceInfo: CastingDeviceInfo): Promise<void> {
        await Backend.POST("/casting/AddPinnedDevice", JSON.stringify(deviceInfo), "application/json");
    }

    static async removePinnedDevice(deviceInfo: CastingDeviceInfo): Promise<void> {
        await Backend.POST("/casting/RemovePinnedDevice", JSON.stringify(deviceInfo), "application/json");
    }

    static async connect(id: string): Promise<void> {
        await Backend.GET("/casting/Connect?id=" + encodeURIComponent(id));
    }

    static async disconnect(): Promise<void> {
        await Backend.GET("/casting/Disconnect");
    }

    static async mediaSeek(time: Duration): Promise<void> {
        await Backend.GET("/casting/MediaSeek?time=" + encodeURIComponent(time.as('seconds')));
    }

    static async mediaStop(): Promise<void> {
        await Backend.GET("/casting/MediaStop");
    }
    
    static async mediaPause(): Promise<void> {
        await Backend.GET("/casting/MediaPause");
    }

    static async mediaResume(): Promise<void> {
        await Backend.GET("/casting/MediaResume");
    }

    static async mediaLoad(obj: { streamType: string, resumePosition: Duration, duration: Duration, sourceSelected: SourceSelected, speed?: number, tag?: string }): Promise<void> {
        await Backend.GET(`/casting/MediaLoad?streamType=${encodeURIComponent(obj.streamType)}` +
            `&resumePosition=${encodeURIComponent(obj.resumePosition.as("seconds"))}` +
            `&duration=${encodeURIComponent(obj.duration.as("seconds"))}` +
            `&url=${encodeURIComponent(obj.sourceSelected.url)}` + 
            `&videoIndex=${encodeURIComponent(obj.sourceSelected.video)}` + 
            `&audioIndex=${encodeURIComponent(obj.sourceSelected.audio)}` + 
            `&subtitleIndex=${encodeURIComponent(obj.sourceSelected.subtitle)}` + 
            `&videoIsLocal=${encodeURIComponent(obj.sourceSelected.videoIsLocal)}` + 
            `&audioIsLocal=${encodeURIComponent(obj.sourceSelected.audioIsLocal)}` + 
            `&subtitleIsLocal=${encodeURIComponent(obj.sourceSelected.subtitleIsLocal)}` + 
            (obj.speed ? `&speed=${encodeURIComponent(obj.speed!)}` : "") +
            (obj.tag ? `&tag=${encodeURIComponent(obj.tag!)}` : ""));
    }

    static async changeVolume(volume: number): Promise<void> {
        await Backend.GET("/casting/ChangeVolume?volume=" + encodeURIComponent(volume));
    }

    static async changeSpeed(speed: number): Promise<void> {
        await Backend.GET("/casting/ChangeSpeed?speed=" + encodeURIComponent(speed));
    }
}