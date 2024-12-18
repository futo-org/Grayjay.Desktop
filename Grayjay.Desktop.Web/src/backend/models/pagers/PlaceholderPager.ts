import { Pager } from "./Pager";


export class PlaceholderPager extends Pager<IPlatformContentPlaceholder> {
    items: IPlatformContentPlaceholder[] = [];

    constructor(count: number = 10) {
        super();
        for(let i = 0; i < count; i++) {
            this.items.push({
                contentType: 90,
                placeholderIcon: "",
                error: null
            } as IPlatformContentPlaceholder)
        }
    }


    fetchLoad(): Promise<PagerResult<IPlatformContentPlaceholder>> {
        return new Promise((resolve)=>resolve({
            results: this.items,
            hasMore: false
        } as PagerResult<IPlatformContentPlaceholder>));
    }
    protected fetchNextPage(): Promise<PagerResult<IPlatformContentPlaceholder>> {
        throw new Error("Method not implemented.");
    }
    
}