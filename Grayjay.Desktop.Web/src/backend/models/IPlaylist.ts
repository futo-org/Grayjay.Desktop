import { IPlatformVideo } from "./content/IPlatformVideo";

export interface IPlaylist {
    id: string;
    name: string;
    videos: IPlatformVideo[];
    videoAdds?: Record<string, number>;
    dateCreated?: string;
    dateUpdate?: string;
    datePlayed?: string;
}  