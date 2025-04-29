import { Accessor, createSignal, Setter } from "solid-js";
import { Pager } from "./Pager";
import StateWebsocket from "../../../state/StateWebsocket";
import { updateDataArray } from "../../../utility";


export abstract class RefreshPager<T> extends Pager<T> {

    abstract uniqueId: string;
    
    hadInitialUpdate$: Accessor<boolean>
    private setHadInitialUpdate: Setter<boolean>;

    constructor() {
        super();
        const [initialUpdateSignal$, setInitialUpdateSignal] = createSignal(false);
        this.hadInitialUpdate$ = initialUpdateSignal$;
        this.setHadInitialUpdate = setInitialUpdateSignal;
    }

    override beforeLoad() {
        StateWebsocket?.registerHandlerNew("PagerUpdated", (packet)=>{
            if(packet.id == this.id) {
                console.log("Pager updated");
                const result = packet.payload as PagerResult<T>;

                const newDataFiltered = result.results.filter(this.filter ? this.filter : (item) => true);
                updateDataArray(this.data, result.results, (a, b) => this.modified(a, b), (a, b) => this.added(a, b), (a, b) => this.removed(a, b));
                updateDataArray(this.dataFiltered, newDataFiltered, (a, b) => this.modifiedFiltered(a, b), (a, b) => this.addedFiltered(a, b), (a, b) => this.removedFiltered(a, b));

                if(!this.hadInitialUpdate$())
                    this.setHadInitialUpdate(true);
            }
        }, this.uniqueId);
    }
    override afterLoad(){
    }

    static async fromMethodsRefresh<T>(uid: string, loadMethod: ()=>Promise<PagerResult<T>>, nextMethod: ()=>Promise<PagerResult<T>>) {
        const pager = new class extends RefreshPager<T> {
            fetchLoad = loadMethod;
            fetchNextPage = nextMethod;
            uniqueId = uid;
        };
        await pager.load();
        return pager;
    }
}