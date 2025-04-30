
interface ISerializedChannel {
    id: IPlatformID;
    name: string;
    thumbnail: string; //?
    banner: string; //?
    subscribers: number; //?
    url: string; //?
    links: any; //?
    urlAlternatives: string[]; //
    description?: string;
}