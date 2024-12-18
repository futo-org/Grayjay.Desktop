import { IPlatformAuthorLink } from "../IPlatformAuthorLink";

export interface IPlatformContent {
    contentType: number;
    id: IPlatformID;
    name: string;
    author: IPlatformAuthorLink;
    dateTime: string;
    url: string;
    shareUrl: string;
    backendUrl?: string;
}