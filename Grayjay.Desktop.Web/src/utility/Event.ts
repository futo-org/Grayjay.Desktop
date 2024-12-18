import { generateUUID } from "../utility";



interface Handler1<T> {
    tag?: any
    handler: (val: T)=>void;
}

export class Event1<T> {
    listeners: Handler1<T>[] = [];

    constructor() {

    }

    register(handler: (val: T)=>void, tag: any) {
        this.listeners.push({
            handler,
            tag
        });
    }
    registerOne(id: any, handler: (val: T)=>void) {
        this.unregister(id);
        this.listeners.push({
            handler,
            tag: id
        });
    }
    registerOnce(id: any, handler: (val: T)=>void) {
        if(!id)
            id = generateUUID();
        this.register((val)=>{
            this.unregister(id);
            handler(val);
        }, id);
    }
    unregister(tag: any) {
        this.listeners = this.listeners.filter(x=>x.tag != tag);
    }
    invoke(obj: T) {
        for(let listener of this.listeners)
            listener?.handler(obj);
    }
}

interface Handler0 {
    tag?: any
    handler: () => void;
}

export class Event0 {
    listeners: Handler0[] = [];

    constructor() {

    }

    register(handler: () => void, tag: any) {
        this.listeners.push({
            handler,
            tag
        });
    }
    registerOne(id: any, handler: () => void) {
        this.unregister(id);
        this.listeners.push({
            handler,
            tag: id
        });
    }
    registerOnce(id: any, handler: () => void) {
        if(!id)
            id = generateUUID();
        this.register(() => {
            this.unregister(id);
            handler();
        }, id);
    }
    unregister(tag: any) {
        this.listeners = this.listeners.filter(x=>x.tag != tag);
    }
    invoke() {
        for(let listener of this.listeners)
            listener?.handler();
    }
}