import { RefItem } from "../RefItem";
import { IPlatformPlaylist } from "./IPlatformPlaylist";
import { IPlatformVideo } from "./IPlatformVideo";


export interface IPlatformPlaylistDetails extends IPlatformPlaylist {
    contents?: PagerResult<RefItem<IPlatformVideo>>;
}