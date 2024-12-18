import { IRating } from "../IRating";
import { IPlatformContent } from "./IPlatformContent";
import { IPlatformPost } from "./IPlatformPost";


export interface IPlatformPostDetails extends IPlatformPost {
    rating: IRating,
    textType: number,
    content: string
}
export enum TextType {
    RAW = 0,
    HTML = 1,
    MARKUP = 2
};