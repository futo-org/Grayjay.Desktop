import { IPlatformVideo } from "./content/IPlatformVideo";

export interface IPlaylist {
    id: string;
    name: string;
    videos: IPlatformVideo[];
    dateCreated?: string;
    dateUpdate?: string;
    datePlayed?: string;
}  