interface IBlockedChannel {
    url: string;
    name: string;
    thumbnail?: string;
    pluginId?: string;
    blockedTime: number;
    urlAlternatives: string[];
}
