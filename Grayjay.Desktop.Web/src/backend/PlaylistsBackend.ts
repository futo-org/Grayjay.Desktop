import { Backend } from "./Backend";
import { IPlaylist } from "./models/IPlaylist";
import { IPlatformPlaylistDetails } from "./models/content/IPlatformPlaylistDetails";
import { IPlatformVideo } from "./models/content/IPlatformVideo";

export abstract class PlaylistsBackend {
    static async getAll(): Promise<IPlaylist[]> {
        return await Backend.GET("/playlists/GetAll") as IPlaylist[];
    }

    static async createOrupdate(playlist: IPlaylist): Promise<void> {
        const sanitizedPlaylist: IPlaylist = {
            ... playlist,
            videos: playlist.videos.filter(v => v !== undefined && v !== null)
        };
        await Backend.POST("/playlists/CreateOrUpdate", JSON.stringify(sanitizedPlaylist), "application/json");
    }

    static async addContentToPlaylists(content: IPlatformVideo, playlistIds: string[]): Promise<void> {
        await Backend.POST("/playlists/AddContentToPlaylists", JSON.stringify({
            content,
            playlistIds
        }), "application/json");
    }

    static async removeContentFromPlaylists(id: string, index: number): Promise<void> {
        await Backend.GET("/playlists/RemoveContentFromPlaylist?id=" + id + "&index=" + index);
    }

    static async get(id: string): Promise<IPlaylist> {
        return await Backend.GET("/playlists/Get?id=" + id) as IPlaylist;
    }

    static async delete(id: string): Promise<void> {
        await Backend.DELETE("/playlists/Delete?id=" + id);
    }

    static async renamePlaylist(id: string, newName: string): Promise<void> {
        await Backend.POST("/playlists/RenamePlaylist?id=" + id, JSON.stringify(newName), "application/json");
    }
}