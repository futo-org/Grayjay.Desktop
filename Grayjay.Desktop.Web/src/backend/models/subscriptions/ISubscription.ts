interface ISubscription {
    channel: ISerializedChannel;
    lastVideo: number;
    lastVideoUpdate: number;
    uploadInterval: number;

    doNotifications: boolean;
    doFetchLive: boolean;
    doFetchStreams: boolean;
    doFetchVideos: boolean;
    doFetchPosts: boolean;
}

interface ISubscriptionSettings {
    doNotifications: boolean,
    doFetchLive: boolean,
    doFetchStreams: boolean,
    doFetchVideos: boolean,
    doFetchPosts: boolean
}

interface ISubscriptionGroup {
    id: string,
    name: string,
    image?: IImageVariable,
    urls: string[],
    priority: number
}

interface IImageVariable {
    url?: string,
    resId?: number,
    presetName?: string
}