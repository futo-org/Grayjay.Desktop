import { Pager } from "../pagers/Pager";
import { IPlatformContent } from "./IPlatformContent";
import { IPlatformVideo } from "./IPlatformVideo";


export interface IPlatformPlaylist extends IPlatformContent {
    thumbnail?: string;
    videoCount: number;
}