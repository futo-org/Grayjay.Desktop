import { IPlatformAuthorLink } from "../IPlatformAuthorLink";
import { IRating } from "../IRating";

export interface ISerializedComment {
    contextUrl: string;
    author: IPlatformAuthorLink;
    message: string;
    rating: IRating;
    date: string;
    replyCount: number;
}