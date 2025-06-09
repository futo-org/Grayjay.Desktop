import { updateDataArray } from "../../../utility";
import { Event0, Event1 } from "../../../utility/Event";

//TODO: Split out filtered pager to avoid data duplication
export abstract class Pager<T> {
    id: string = "";
    data: T[] = Array<T>();
    dataFiltered: T[] = Array<T>();
    filter?: (item: T)=>boolean;
    modifiedItemsEvent = new Event1<{startIndex: number, endIndex: number}>();
    modifiedFilteredItemsEvent = new Event1<{startIndex: number, endIndex: number}>();
    removedItemsEvent = new Event1<{startIndex: number, endIndex: number}>();
    removedFilteredItemsEvent = new Event1<{startIndex: number, endIndex: number}>();
    addedItemsEvent = new Event1<{startIndex: number, endIndex: number}>();
    addedFilteredItemsEvent = new Event1<{startIndex: number, endIndex: number}>();
    noFilteredItemsEvent = new Event0();
    filterChangedEvent = new Event0();

    error: any;

    hasMore: boolean = false;

    setFilter(filter: (arg0: T)=>boolean) {
        this.filter = filter;
        updateDataArray<T>(this.dataFiltered, this.data.filter(filter), (a, b) => this.modifiedFiltered(a, b), (a, b) => this.addedFiltered(a, b), (a, b) => this.removedFiltered(a, b));
        this.filterChangedEvent.invoke();
    }

    abstract fetchLoad(): Promise<PagerResult<T>>;
    protected abstract fetchNextPage(): Promise<PagerResult<T>>;

    async load(): Promise<PagerResult<T>> {
        this.beforeLoad();
        let result;
        this.error = undefined;
        try {
            result = await this.fetchLoad();
            if(result.exception)
                this.error = result.exception;
        }
        catch(ex) {
            this.error = ex;
            this.data.length = 0;
            this.dataFiltered.length = 0;
            return {
                pagerID: undefined,
                results: [],
                hasMore: false,
                exception: ex
            } as PagerResult<T>;
        }
        this.hasMore = result!.hasMore;
        if (!result!.hasMore)
            console.log("End of page found");

        this.id = result!.pagerID!;
        this.data.length = 0;
        this.dataFiltered.length = 0;
        if (result?.results) {
            this.data.push(...result.results);
            this.dataFiltered.push(...result.results.filter((this.filter) ? this.filter : (item)=>true));
            if (this.dataFiltered.length > 0) {
                this.addedFiltered(0, this.dataFiltered.length - 1);
                console.info(`addedFiltered(0, ${this.dataFiltered.length - 1})`);
            }
            if (this.data.length > 0) {
                this.added(0, this.data.length - 1);
                console.info(`added(0, ${this.data.length - 1})`);
            }
        }
        this.afterLoad();
        return result;
    }

    beforeLoad() {

    }

    afterLoad(){
        
    }

    protected modified(startIndex: number, endIndex: number){
        this.modifiedItemsEvent.invoke({ startIndex, endIndex });
        console.log("modified triggered", {startIndex, endIndex});
    }

    protected modifiedFiltered(startIndex: number, endIndex: number){
        this.modifiedFilteredItemsEvent.invoke({ startIndex, endIndex });
        console.log("modified filtered triggered", {startIndex, endIndex});
    }

    protected removed(startIndex: number, endIndex: number){
        this.removedItemsEvent.invoke({ startIndex, endIndex });
        console.log("removed triggered", {startIndex, endIndex});
    }

    protected removedFiltered(startIndex: number, endIndex: number){
        this.removedFilteredItemsEvent.invoke({ startIndex, endIndex });
        console.log("removed filtered triggered", {startIndex, endIndex});
    }

    protected added(startIndex: number, endIndex: number){
        this.addedItemsEvent.invoke({ startIndex, endIndex });
        console.log("added triggered", {startIndex, endIndex});
    }

    protected addedFiltered(startIndex: number, endIndex: number){
        this.addedFilteredItemsEvent.invoke({ startIndex, endIndex });
        console.log("added filtered triggered", {startIndex, endIndex});
    }

    protected noFiltered(){
        this.noFilteredItemsEvent.invoke();
        console.log("no filtered items triggered");
    }

    async nextPage(): Promise<PagerResult<T>> {
        if(!this.hasMore)
            throw Error("Pager has no more pages");

        try {
            const result = await this.fetchNextPage();
            if (!result.hasMore) {
                this.hasMore = false;
                console.log("End of page found");
            }

            if (result?.results) {
                const dataLengthBefore = this.data.length;
                const dataFilteredLengthBefore = this.dataFiltered.length;
                this.data.push(...result.results);
                this.dataFiltered.push(...result.results.filter((this.filter) ? this.filter : (item)=>true));
                console.log("next page loaded", {length: this.data.length});
                
                if (this.data.length > dataLengthBefore) {
                    this.added(dataLengthBefore, this.data.length - 1);
                    console.info(`nextPage added(${dataLengthBefore}, ${this.data.length - 1})`);
                }
                if (this.dataFiltered.length > dataFilteredLengthBefore) {
                    this.addedFiltered(dataFilteredLengthBefore, this.dataFiltered.length - 1);
                    console.info(`nextPage addedFiltered(${dataFilteredLengthBefore}, ${this.dataFiltered.length - 1})`);
                } else {
                    this.noFiltered();
                    console.info(`nextPage noFilteredItems`);
                }
            }

            return result;
        }
        catch(ex) {
            console.error("NextPage failed: ", ex);
            this.hasMore = false;
            throw ex;
        }
    }

    static async fromMethods<T>(loadMethod: ()=>Promise<PagerResult<T>>, nextMethod: ()=>Promise<PagerResult<T>>) {
        const pager = new class extends Pager<T> {
            fetchLoad = loadMethod;
            fetchNextPage = nextMethod;
        };
        await pager.load();
        return pager;
    }
}