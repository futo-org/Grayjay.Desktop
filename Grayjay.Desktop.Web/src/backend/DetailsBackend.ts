import { Backend } from "./Backend";
import { RefItem } from "./models/RefItem";
import { ILiveChatWindowDescriptor } from "./models/comments/ILiveChatWindowDescriptor";
import { ISerializedComment } from "./models/comments/ISerializedComment";
import { IPlatformContent } from "./models/content/IPlatformContent";
import { IPlatformPostDetails } from "./models/content/IPlatformPostDetails";
import { IChapter } from "./models/contentDetails/IChapter";
import { IPlatformVideoDetails } from "./models/contentDetails/IPlatformVideoDetails";
import { IVideoDownload } from "./models/downloads/IVideoDownload";
import { IVideoLocal } from "./models/downloads/IVideoLocal";
import { Pager } from "./models/pagers/Pager";


export abstract class DetailsBackend {

    static async postLoad(url: string): Promise<IPostLoadResult> {
        return await Backend.GET("/details/PostLoad?url=" + encodeURIComponent(url));
    }
    static async postCurrent(): Promise<IPostLoadResult> {
        return await Backend.GET("/details/PostCurrent");
    }

    static async videoLoad(url: string): Promise<IVideoLoadResult> {
        return await Backend.GET("/details/VideoLoad?url=" + encodeURIComponent(url));
    }
    static async videoCurrent(): Promise<PagerResult<IPlatformVideoDetails>> {
        return await Backend.GET("/details/VideoCurrent");
    }

    static async commentsLoad(): Promise<PagerResult<RefItem<ISerializedComment>>> {
        return await Backend.GET("/details/CommentsLoad");
    }
    static async commentsNextPage(): Promise<PagerResult<RefItem<ISerializedComment>>> {
        return await Backend.GET("/details/CommentsNextPage");
    }
    static async commentsPager(): Promise<Pager<RefItem<ISerializedComment>>> {
        return Pager.fromMethods<RefItem<ISerializedComment>>(this.commentsLoad, this.commentsNextPage);
    }
    static async liveChatWindow(): Promise<ILiveChatWindowDescriptor> {
        return await Backend.GET("/details/GetLiveChatWindow");
    }
    static async loadLiveChat(): Promise<void> {
        return await Backend.GET("/details/LoadLiveChat");
    }
    static async getVideoChapters(url: string): Promise<IChapter[]> {
        return await Backend.GET("/details/getVideoChapters?url=" + encodeURIComponent(url));
    }

    static async repliesLoad(commentId: string, replyId?: string): Promise<PagerResult<RefItem<ISerializedComment>>> {
        return await Backend.GET("/details/RepliesLoad?commentId=" + commentId + "&replyId=" + replyId);
    }
    static async repliesNextPage(commentId: string): Promise<PagerResult<RefItem<ISerializedComment>>> {
        return await Backend.GET("/details/RepliesNextPage?replyId=" + commentId);
    }
    static async repliesPager(commentId: string, replyId?: string): Promise<Pager<RefItem<ISerializedComment>>> {
        return Pager.fromMethods<RefItem<ISerializedComment>>(()=>this.repliesLoad(commentId, replyId), ()=>this.repliesNextPage(commentId));
    }

    static async recommendationsPager(url: string): Promise<Pager<IPlatformContent>> {
        return Pager.fromMethods<IPlatformContent>(()=>this.recommendationsLoad(url), ()=>this.recommendationsNextPage(url));
    }
    static async recommendationsLoad(url: string): Promise<PagerResult<IPlatformContent>> {
        return await Backend.GET("/details/RecommendationsLoad?url=" + encodeURIComponent(url));
    }
    static async recommendationsNextPage(url: string): Promise<PagerResult<IPlatformContent>> {
        return await Backend.GET("/details/RecommendationsNextPage?url=" + encodeURIComponent(url));
    }

    static async download(url: string, videoIndex: number, audioIndex: number): Promise<IVideoDownload> {
        return await Backend.GET("/details/Download?url=" + encodeURIComponent(url) + "&videoIndex=" + videoIndex + "&audioIndex=" + audioIndex);
    }

    static async sourceAuto(url: string): Promise<ISourceDirectDescriptor> {
        return await Backend.GET(`/details/SourceAuto?url=${encodeURIComponent(url)}`) as ISourceDirectDescriptor;
    }
    static async sourceProxy(url: string, videoIndex: number, videoIsLocal: boolean, audioIndex: number, audioIsLocal: boolean, subtitleIndex: number, subtitleIsLocal: boolean, tag: string): Promise<ISourceDirectDescriptor> {
        return await Backend.GET(`/details/SourceProxy?url=${encodeURIComponent(url)}&videoIndex=${videoIndex}&audioIndex=${audioIndex}&subtitleIndex=${subtitleIndex}&videoIsLocal=${videoIsLocal}&audioIsLocal=${audioIsLocal}&subtitleIsLocal=${subtitleIsLocal}&tag=${encodeURIComponent(tag)}`) as ISourceDirectDescriptor;
    }
    static async sourceVideoQualities(videoIndex: number): Promise<any> {
        return await Backend.GET(`/details/VideoQualities?videoIndex=` + videoIndex) as any;
    }

    static async watchProgress(url: string, progress: number) {
        return await Backend.GET(`/details/WatchProgress?url=${encodeURIComponent(url)}&position=${Math.floor(progress)}`);
    }
}

export interface IVideoLoadResult {
    video: IPlatformVideoDetails,
    local: IVideoLocal
}
export interface IPostLoadResult {
    post: IPlatformPostDetails
}

export interface ISourceDirectDescriptor {
    url: string;
    type: string;
    videoIndex?: number;
    audioIndex?: number;
    subtitleIndex?: number;
    videoIsLocal?: boolean;
    audioIsLocal?: boolean;
    subtitleIsLocal?: boolean;
}
