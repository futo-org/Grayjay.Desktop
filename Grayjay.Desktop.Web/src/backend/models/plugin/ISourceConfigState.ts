
export interface ISourceCapabilities {
    hasChannelSearch: boolean,
    hasGetChannelCapabilities: boolean,
    hasGetChannelTemplateByClaimMap: boolean,
    hasGetChannelUrlByClaim: boolean,
    hasGetComments: boolean,
    hasGetContentChapters: boolean,
    hasGetLiveChatWindow: boolean,
    hasGetLiveEvents: boolean,
    hasGetPlaybackTracker: boolean,
    hasGetPlaylist: boolean,
    hasGetSearchCapabilities: boolean,
    hasGetUserPlaylists: boolean,
    hasGetUserSubscriptions: boolean,
    hasSaveState: boolean,
    hasSearchChannelContents: boolean,
    hasSearchPlaylists: boolean
}

export interface IFilterCapability {
    name: string;
    value: string;
    id: string;
}

export interface IFilter {
    id: string;
    name: string;
    isMultiSelect: boolean;
    filters: IFilterCapability[];
}

export interface IResultCapabilities {
    types: string[];
    sorts: string[];
    filters: IFilter[];
}

export interface ISourceSetting {
    name: string;
    description: string;
    type: string;
    default: string;
    variable: string;
    dependency: string;
    warningDialog: string;
    options: string[];
}

export interface ISourceCaptchaConfig {
    captchaUrl: string;
    completionUrl: string;
    cookiesToFind: string[];
    userAgent: string;
    cookiesExclOthers: boolean;
}

export interface ISourceAuthConfig {
    loginUrl: string;
    completionUrl: string;
    allowedDomains: string[];
    headersToFind: string[];
    cookiesToFind: string[];
    cookiesExclOthers: boolean;
    userAgent: string;
    loginButton: string;
    domainHeadersToFind: { [key: string]: string[] };
}

export interface ISourceConfig {
    id: string;
    name: string;
    description: string;
    version: number;
    author: string;
    authorUrl: string;
    iconUrl: string;
    sourceUrl: string;
    scriptUrl: string;
    allowUrls: string[];
    packages: string[];
    scriptSignature: string;
    scriptPublicKey: string;
    captcha: ISourceCaptchaConfig;
    authentication: ISourceAuthConfig;
    constants: { [key: string]: string };
    subscriptionRateLimit: number;
    enableInSearch: boolean;
    enableInHome: boolean;
    supportedClaimTypes: number[];
    primaryClaimFieldType: number;
    settings: ISourceSetting[];
    absoluteIconUrl?: string;
    absoluteScriptUrl?: string;
}

export interface ISourceConfigState {
    config: ISourceConfig,
    capabilities: ISourceCapabilities,
    capabilitiesChannel: IResultCapabilities,
    capabilitiesSearch: IResultCapabilities
}