import { Backend } from "./Backend";
import { ContentType } from "./models/ContentType";
import { IPlatformContent } from "./models/content/IPlatformContent";
import { RefreshPager } from "./models/pagers/RefreshPager";


export abstract class SearchBackend {

    static async searchLoadLazy(query: string, type: ContentType, order?: string, filters?: Record<string, string[]>, excludeClientIds?: string[]): Promise<PagerResult<IPlatformContent>> {
        return await Backend.POST("/search/SearchLoadLazy", JSON.stringify({
            type: type,
            query: query,
            filters: filters,
            excludePlugins: excludeClientIds
        }), "application/json") as PagerResult<IPlatformContent>;
            
        //    ?query=" + encodeURIComponent(query) + "&type=" + encodeURIComponent(type) + (order ? "&order=" + encodeURIComponent(order) : "") + (filters ? "&filters=" + encodeURIComponent(JSON.stringify(filters)) : "") + (clientIds ? "&clientIds=" + encodeURIComponent(JSON.stringify(clientIds)) : "")) as PagerResult<IPlatformContent>;
    }

    static async searchNextPage(): Promise<PagerResult<IPlatformContent>> {
        return await Backend.GET("/search/SearchNextPage") as PagerResult<IPlatformContent>;
    }
    static async searchPagerLazy(query: string, type: ContentType, order?: string, filters?: Record<string, string[]>, excludeClientIds?: string[]): Promise<RefreshPager<IPlatformContent>> {
        const result = RefreshPager.fromMethodsRefresh<IPlatformContent>("search", async () => this.searchLoadLazy(query, type, order, filters, excludeClientIds), this.searchNextPage);
        (await result).nextPage();
        return result;
    }

    static async searchSuggestions(query: string): Promise<string[]> {
        return await Backend.GET("/search/SearchSuggestions?query=" + encodeURIComponent(query)) as string[];
    }

    static async previousSearches(): Promise<string[]> {
        return await Backend.GET("/search/PreviousSearches") as string[];
    }

    static async addPreviousSearch(query: string): Promise<void> {
        await Backend.GET("/search/AddPreviousSearch?query=" + encodeURIComponent(query));
    }

    static async removePreviousSearch(query: string): Promise<void> {
        await Backend.GET("/search/RemovePreviousSearch?query=" + encodeURIComponent(query));
    }

    static async removeAllPreviousSearches(): Promise<void> {
        await Backend.GET("/search/RemoveAllPreviousSearches");
    }
}