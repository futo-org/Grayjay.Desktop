import { createWSState, makeReconnectingWS } from "@solid-primitives/websocket";
import { WebSocketPacket } from "../backend/models/socket/WebSocketPacket";
import { createEffect, createRoot } from "solid-js";

export interface WebSocketState {
    socket: WebSocket;
    handlers: any,

    registerHandler: (type:string, handler: (packet: WebSocketPacket)=>void, tag: any) => void,
    registerHandlerNew: (type:string, handler: (packet: WebSocketPacket)=>void, tag: any) => void,
    unregisterHandler: (type:string, tag: any) => void
};

function createState() {
    console.log("Initializing WS");
    const socket = makeReconnectingWS(`ws://127.0.0.1:${window.location.port}/ws`, undefined);
    const state = createWSState(socket);

    createEffect(() => {
        const s = state();
        let stateString = s.toString();
        switch (s) {
            case 0:
                stateString = "CONNECTING";
                break;
            case 1:
                stateString = "OPEN";
                break;
            case 2:
                stateString = "CLOSING";
                break;
            case 3:
                stateString = "CLOSED";
                break;
        }

        console.info("websocket state changed", stateString);
    });

    const value: WebSocketState = {
        socket,
        handlers: {},
        
        registerHandlerNew(type, handler, tag) {
            this.unregisterHandler(type, tag);
            this.registerHandler(type, handler, tag);
        },
        registerHandler(type, handler, tag){
            if(!value.handlers[type])
                value.handlers[type] = [];
            value.handlers[type].push({
                handler,
                tag
            });
            console.info("handler registered", {handlers: value.handlers});
        },
        unregisterHandler(type, tag) {
            if(value.handlers[type]) {
                const handlers = value.handlers[type];
                value.handlers[type] = handlers.filter((x: { tag: any; })=>x.tag != tag);
                console.info("handler unregistered", {handlers: value.handlers});
            }
        }
    };

    socket.addEventListener("message", (ev)=>{
        const packet = JSON.parse(ev.data) as WebSocketPacket;
        const handlers = value.handlers[packet.type];
        console.log("Websocket Message [" + packet.type + "]", {packet, handlers});
        if((handlers?.length ?? 0) > 0) {
            for(let handler of handlers) {
                handler.handler(packet);
            }
        }
    });
    return value;
}

export default createRoot(createState);