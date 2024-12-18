


export interface IVideoDownload {
    state: number,
    video: IPlatformVideo,
    videoDetails: IPlatformVideoDetails,

    progress: number,

    downloadSpeedVideo: number,
    downloadSpeedAudio: number,
    downloadSpeed: number,

    error: string,

    groupType?: string,
    groupID?: string,


    videoSource: any,
    audioSource: any,

    videoFileSize: number,
    audioFileSize: number


}