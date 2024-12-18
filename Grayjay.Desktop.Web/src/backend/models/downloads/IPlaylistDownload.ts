import { IPlaylist } from "../IPlaylist";



export interface IVideoDownload {
    playlistId: string,
    targetPixelCount: number,
    targetBitrate: number,
    playlist?: IPlaylist
}