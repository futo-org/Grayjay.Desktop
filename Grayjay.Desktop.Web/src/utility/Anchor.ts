import { Accessor, JSX, Setter, createEffect, createMemo, createSignal } from "solid-js";
import { observePosition } from "../utility";


export enum AnchorStyle {
    BottomLeft = 1,
    BottomRight = 2,
    TopLeft = 3,
    TopRight = 4

    //TODO:
    //BottomLeftSide = 5,
    //BottomRightSide = 6,
    //TopLeftSide = 7,
    //TopRightSide = 8,
    //TopLeftCorner = 9,
    //TopRightCorner = 10,
    //BottomLeftCorner = 11,
    //BottomRightCorner = 12
}

export enum AnchorFlags {
    AnchorWidth = 1,
    AnchorHeight = 2,
    AnchorMinWidth = 3,
    AnchorMinHeight = 4
}

export default class Anchor {
    element: HTMLElement | null;
    anchorType: AnchorStyle;

    bounding$: Accessor<DOMRect>;
    style$: Accessor<JSX.CSSProperties>;
    styleFlipped$: Accessor<JSX.CSSProperties>;

    condition$?: Accessor<Boolean>;

    private setBounding: Setter<DOMRect>;
    private destroyListener: (()=>void) | undefined = undefined;

    constructor(element: HTMLElement | null, condition: Accessor<boolean> | undefined = undefined, anchorStyle: AnchorStyle = AnchorStyle.BottomLeft, anchorFlags: AnchorFlags[] = []) {
        this.element = element;
        this.anchorType = anchorStyle;
        const [bounding$, setBounding] = createSignal<DOMRect>(element?.getBoundingClientRect() ?? new DOMRect());
        this.bounding$ = bounding$;
        this.styleFlipped$ = createMemo<JSX.CSSProperties>(()=>{
            const bounds = this.bounding$();
            let style: JSX.CSSProperties;

            switch(anchorStyle) {
                case AnchorStyle.TopLeft: style = {
                    top: bounds.top + bounds.height + "px",
                    left: bounds.left + "px"
                };
                break;
                case AnchorStyle.TopRight: style = {
                    top: bounds.top + bounds.height + "px",
                    right: (window.innerWidth - bounds.right) + "px"
                }
                break;
                case AnchorStyle.BottomLeft: style = {
                    bottom: (window.innerHeight - bounds.top) + "px",
                    left:  bounds.left + "px",
                }
                break;
                case AnchorStyle.BottomRight: style = {
                    bottom: (window.innerHeight - bounds.top) + "px",
                    right:  (window.innerWidth - bounds.right) + "px",
                }
                break;
            }
            for(let flag of anchorFlags) {
                switch(flag) {
                    case AnchorFlags.AnchorWidth:
                        style.width = bounds.width + "px";
                        break;
                    case AnchorFlags.AnchorMinWidth:
                        style["min-width"] = bounds.width + "px";
                        break;
                    case AnchorFlags.AnchorHeight:
                        style.height = bounds.height + "px";
                        break;
                    case AnchorFlags.AnchorMinHeight:
                        style["min-height"] = bounds.height + "px";
                        break;
                }
            }

            return style;
        });
        this.style$ = createMemo<JSX.CSSProperties>(()=>{
            const bounds = this.bounding$();
            let style: JSX.CSSProperties;

            switch(anchorStyle) {
                case AnchorStyle.TopLeft: style = {
                    bottom: (window.innerHeight - bounds.top) + "px",
                    left:  bounds.left + "px",
                };
                break;
                case AnchorStyle.TopRight: style = {
                    bottom: (window.innerHeight - bounds.top) + "px",
                    right:  (window.innerWidth - bounds.right) + "px",
                }
                break;
                case AnchorStyle.BottomLeft: style = {
                    top: bounds.top + bounds.height + "px",
                    left: bounds.left + "px"
                }
                break;
                case AnchorStyle.BottomRight: style = {
                    top: bounds.top + bounds.height + "px",
                    right: (window.innerWidth - bounds.right) + "px"
                }
                break;
            }

            for(let flag of anchorFlags) {
                switch(flag) {
                    case AnchorFlags.AnchorWidth:
                        style.width = bounds.width + "px";
                        break;
                    case AnchorFlags.AnchorMinWidth:
                        style["min-width"] = bounds.width + "px";
                        break;
                    case AnchorFlags.AnchorHeight:
                        style.height = bounds.height + "px";
                        break;
                    case AnchorFlags.AnchorMinHeight:
                        style["min-height"] = bounds.height + "px";
                        break;
                }
            }

            return style;
        }); 
        this.setBounding = setBounding;
        if(condition) {
            this.condition$ = condition;
            createEffect(()=>{
                console.log("Anchor condition changed: " + condition());
                if(condition())
                    this.start();
                else
                    this.stop();
            });
            if(condition())
                this.start();
        }
    }
    
    setElement = (el: HTMLElement) => {
        console.log("Anchor element changed");
        this.element = el;
        this.setBounding(el.getBoundingClientRect());
    }

    start = () => {
        if(!this.destroyListener && this.element) {
            this.destroyListener = observePosition(this.element, this.handleChange);
            this.handleChange(this.element);
        }
    }
    stop = () => {
        if(this.destroyListener) {
            this.destroyListener();
            this.destroyListener = undefined;
        }
    }

    handleChange = (element: HTMLElement) => {
        const oldBox = this.bounding$();
        const newBox = element.getBoundingClientRect();
        if(newBox.x != oldBox.x || newBox.y != oldBox.y) {
            console.log("New position:", newBox);
            this.setBounding(newBox);
        }
    }

    dispose() {
        this.stop();
    }
}