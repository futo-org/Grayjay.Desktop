import { Component, createSignal, onCleanup, onMount, For, JSX, createMemo, createEffect, ErrorBoundary, Signal, untrack, batch, Accessor, on, Show } from "solid-js";
import { swap } from "../../../utility";
import { Event1 } from "../../../utility/Event";

export interface VirtualDragDropListProps {
    itemHeight: number;
    items?: any[];
    builder: (index: Accessor<number | undefined>, item: Accessor<any>, containerRef: HTMLDivElement, startDrag: (startPageY: number, top: number, el: HTMLElement) => void)=> JSX.Element;
    outerContainerRef: HTMLDivElement | undefined;
    overscan?: number;
    notifyEndOnLast?: number;
    onEnd?: () => void;
    onDragEnd?: () => void;
    onSwap: (index1: number, index2: number) => void;
    addedItems?: Event1<{startIndex: number, endIndex: number}>;
    modifiedItems?: Event1<{startIndex: number, endIndex: number}>;
    removedItems?: Event1<{startIndex: number, endIndex: number}>;
};

interface VisibleRange {
    startIndex: number;
    endIndex: number;
}

const VirtualDragDropList: Component<VirtualDragDropListProps> = (props) => {
    let containerRef: HTMLDivElement | undefined;

    const [totalHeight, setTotalHeight] = createSignal<number>(0);
    const [poolSize, setPoolSize] = createSignal(0);
    const pool = createMemo(on(poolSize, (size) => {
        return Array.from({ length: size }, (_, i) => {
            const [top$, setTop] = createSignal<number>(-1);
            const [index$, setIndex] = createSignal<number | undefined>(undefined);
            const [item$, setItem] = createSignal<any>();
            const dragOffsetY = createSignal(0);
            const isDragging = createSignal(false);
            const element = props.builder(index$, item$, containerRef!, (startPageY, containerTopY, el) => startDrag(index$, startPageY, containerTopY, el, isDragging, dragOffsetY));
            return {
                top: top$,
                setTop,
                index: index$,
                setIndex,
                item: item$,
                setItem,
                dragOffsetY: dragOffsetY[0],
                isDragging: isDragging[0],
                element
            };
        });
    }));
    
    const [visibleRange, setVisibleRange] = createSignal<VisibleRange>();
    
    const getNestedOffsetTop = (element: HTMLElement, ancestor: HTMLElement): number => {
        let offsetTop = 0;
        let currentElement: HTMLElement | null = element;
        
        while (currentElement && currentElement !== ancestor) {
            offsetTop += currentElement.offsetTop;
            currentElement = currentElement.offsetParent as HTMLElement;
        }
        
        return offsetTop;
    };

    const updatePoolSize = () => {
        const boundingRect = props.outerContainerRef?.getBoundingClientRect();
        if (boundingRect) {
            const elementsInView = Math.ceil(boundingRect.height / props.itemHeight);
            const desiredPoolSize = Math.floor(2 * (elementsInView + (props.overscan ?? 1) * 2));
            if (desiredPoolSize > poolSize()) {
                console.log("desiredPoolSize larger than pool size, change pool", desiredPoolSize);
                setPoolSize(desiredPoolSize);
            }
        }
    };

    const calculateVisibleRange = () => {
        if (!props.outerContainerRef || !containerRef) return;

        if (!props.items || !props.items.length) {
            batch(() => {
                setVisibleRange({ startIndex: 0, endIndex: 0 });
                setTotalHeight(0);
                updatePoolSize();
            });
            return;
        }

        const overscan = props.overscan ?? 1;
        const boundingRect = props.outerContainerRef.getBoundingClientRect();
        const elementsInView = Math.ceil(boundingRect.height / props.itemHeight);
        const scrollOffset = props.outerContainerRef.scrollTop - getNestedOffsetTop(containerRef, props.outerContainerRef);

        const startRowIndex = Math.floor(scrollOffset / props.itemHeight);
        const startIndex = Math.max(0, Math.min(startRowIndex - overscan, props.items!.length - 1));
        const endIndex = Math.max(0, Math.min(startRowIndex + elementsInView + overscan, props.items!.length - 1));

        if (props.onEnd) {
            if (elementsInView - endIndex <= (props.notifyEndOnLast ?? 1)) {
                props.onEnd();
            }
        }

        batch(() => {
            setVisibleRange({ startIndex, endIndex });
            updatePoolSize();
            setTotalHeight((props.items?.length ?? 0) * props.itemHeight);
        });
    };

    const onUIEvent = () => {
        calculateVisibleRange();
    };

    const resizeObserver = new ResizeObserver(entries => {
        onUIEvent();
    });

    const getFreePoolItem = () => {
        const range = visibleRange();
        const p = pool();
        if (range) {
            return p.find(item => {
                const index = item.index();
                return index === undefined || index < range.startIndex || index > range.endIndex;
            });
        } else {
            return p.find(item => item.index() === undefined);
        }
    };


    let lastAddedItems: Event1<{ startIndex: number, endIndex: number }> | undefined;
    const attachAddedItems = (addedItems: Event1<{ startIndex: number, endIndex: number }> | undefined) => {
        console.log("attachAddedItems", {lastAddedItems, addedItems});

        lastAddedItems?.unregister(this);
        addedItems?.registerOne(this, (range) => {
            console.log("added event triggred", range);
            const items = props.items;
    
            if (items) {
                batch(() => {
                    for (let i = range.startIndex; i <= range.endIndex; i++) {
                        const unassignedPoolItem = getFreePoolItem();
                        if (unassignedPoolItem) {
                            unassignedPoolItem.setIndex(i);
                            unassignedPoolItem.setItem(items[i]);
                            unassignedPoolItem.setTop(i * props.itemHeight);
                        }
                    }
                });
            }

            calculateVisibleRange();
        });
        lastAddedItems = addedItems;
    };
    
    let lastRemovedItems: Event1<{ startIndex: number, endIndex: number }> | undefined;
    const attachRemovedItems = (removedItems: Event1<{ startIndex: number, endIndex: number }> | undefined) => {
        console.log("attachRemovedItems", {lastRemovedItems, removedItems});

        lastRemovedItems?.unregister(this);
        removedItems?.registerOne(this, (range) => {
            console.log("removed event triggered", range);
            const poolItems = pool();
            const items = props.items;

            if (items) {
                batch(() => {
                    for (const poolItem of poolItems) {
                        const i = poolItem.index();
                        if (i !== undefined && ((i >= range.startIndex && i <= range.endIndex) || i >= items.length)) {
                            poolItem.setIndex(undefined);
                            poolItem.setItem(undefined);
                            poolItem.setTop(-1);
                        }
                    }
                });
            }

            calculateVisibleRange();
            console.log("removed event finished", range);
        });
        lastRemovedItems = removedItems;
    };
    
    let lastModifiedItems: Event1<{ startIndex: number, endIndex: number }> | undefined;
    const attachModifiedItems = (modifiedItems: Event1<{ startIndex: number, endIndex: number }> | undefined) => {
        console.log("attachModifiedItems", {lastModifiedItems, modifiedItems});

        lastModifiedItems?.unregister(this);
        modifiedItems?.registerOne(this, (range) => {
            console.log("modified event triggered", range);
            const poolItems = pool();
            const items = props.items;
            if (items) {    
                batch(() => {
                    for (const poolItem of poolItems) {
                        const i = poolItem.index();
                        if (i !== undefined && (i >= range.startIndex && i <= range.endIndex)) {
                            poolItem.setItem(items[i]);
                        }
                    }
                });
            }

            calculateVisibleRange();
        });
        lastModifiedItems = modifiedItems;
    };

    createEffect(() => attachAddedItems(props.addedItems));
    createEffect(() => attachModifiedItems(props.modifiedItems));
    createEffect(() => attachRemovedItems(props.removedItems));
    createEffect(() => {
        console.log("items changed", props.items);

        const poolItems = untrack(pool);
        const range = untrack(visibleRange);

        if (range) {
            batch(() => {
                for (const poolItem of poolItems) {
                    const i = untrack(poolItem.index);
                    if (i !== undefined) {
                        const item = props.items?.[i];
                        if (i >= range.startIndex && i <= range.endIndex && item) {
                            poolItem.setItem(item);
                        } else {
                            poolItem.setIndex(undefined);
                            poolItem.setItem(undefined);
                            poolItem.setTop(-1);
                        }
                    }
                }
            });    
        }

        calculateVisibleRange();
    });

    onMount(() => {
        calculateVisibleRange();
        requestAnimationFrame(() => {
            calculateVisibleRange();
        });

        //TODO debounce?
        resizeObserver.observe(props.outerContainerRef!);
        //window.addEventListener('resize', onUIEvent);
        props.outerContainerRef?.addEventListener('scroll', onUIEvent);
    });

    onCleanup(() => {
        props.addedItems?.unregister(this);
        props.modifiedItems?.unregister(this);
        props.removedItems?.unregister(this);
        lastAddedItems?.unregister(this);
        lastModifiedItems?.unregister(this);
        lastRemovedItems?.unregister(this);
        resizeObserver.unobserve(props.outerContainerRef!);
        //window.removeEventListener('resize', onUIEvent);
        props.outerContainerRef?.removeEventListener('scroll', onUIEvent);
        resizeObserver.disconnect();
    });

    createEffect(() => {
        const range = visibleRange();
        const items = props.items;
        const poolItems = pool();
    
        if (!items || !range) {
            return;
        }

        batch(() => {
            const startIdx = range.startIndex;
            const endIdx = range.endIndex;

            let previousStartIndex = Infinity;
            let previousEndIndex = -Infinity;
            for (const poolItem of poolItems) {
                const index = untrack(poolItem.index);
                if (index === undefined) {
                    continue;
                }
        
                if (index < previousStartIndex)
                    previousStartIndex = index;
                if (index > previousEndIndex)
                    previousEndIndex = index;
        
                if (index < startIdx || index > endIdx) {
                    poolItem.setIndex(undefined);
                    poolItem.setItem(undefined);
                    poolItem.setTop(-1);
                }
            }
    
            if (previousStartIndex === Infinity && previousEndIndex === -Infinity) {
                const itemCount = Math.min(endIdx - startIdx + 1, props.items?.length ?? 0);
                if (poolItems.length < itemCount) {
                    console.error("pool size is not big enough to set all at once", { poolItems, poolItemsLength: poolItems.length, range, itemCount });
                    return;
                }
    
                console.log("set all", {itemCount});
                for (let i = 0; i < itemCount; i++) {
                    const index = i + startIdx;
                    poolItems[i].setIndex(index);
                    poolItems[i].setItem(items[index]);
                    poolItems[i].setTop(index * props.itemHeight);
                }
            } else {
                for (let i = startIdx; i < previousStartIndex; i++) {
                    console.log("set start", {i});
                    const unassignedPoolItem = getFreePoolItem();
                    if (unassignedPoolItem) {
                        unassignedPoolItem.setIndex(i);
                        unassignedPoolItem.setItem(items[i]);
                        unassignedPoolItem.setTop(i * props.itemHeight);
                    } else {
                        console.error("pool size is not big enough, no unused items");
                    }
                }
    
                for (let i = previousEndIndex + 1; i <= endIdx; i++) {
                    console.log("set end", {i});
                    const unassignedPoolItem = getFreePoolItem();
                    if (unassignedPoolItem) {
                        unassignedPoolItem.setIndex(i);
                        unassignedPoolItem.setItem(items[i]);
                        unassignedPoolItem.setTop(i * props.itemHeight);
                    } else {
                        console.error("pool size is not big enough, no unused items");
                    }
                }
            }
        });
    });

    const startDrag = (i: Accessor<number | undefined>, startPageY: number, containerTopY: number, el: HTMLElement, isDragging: Signal<boolean>, dragOffsetY: Signal<number>) => {
        let spY = startPageY - containerTopY;
        const currentIndex = i();
        if (currentIndex === undefined) {
            return;
        }

        let si = currentIndex;
        let index = si;

        const node = document.createElement("div");
        node.style.width = "100vw";
        node.style.height = "100vh";
        node.style.position = "absolute";
        node.style.top = "0px";
        node.style.left = "0px";
        node.style.zIndex = "2";
        node.onmousemove = (e) => {
            const range = visibleRange();
            if (!range) {
                return;
            }

            batch(() => {
                const indexOffset = Math.round((e.pageY - startPageY) / props.itemHeight);
                const swapIndex = Math.max(0, Math.min((props.items?.length ?? 0) - 1, index + indexOffset));
                if (si != swapIndex) {
                    props.onSwap?.(si, swapIndex);
                    const item1 = pool().find(v => v.index() === si);
                    const item2 = pool().find(v => v.index() === swapIndex);

                    if (item1 && item2) {
                        const tempIndex = item2.index();
                        item2.setIndex(item1.index());
                        item1.setIndex(tempIndex);

                        const tempTop = item2.top();
                        item2.setTop(item1.top());
                        item1.setTop(tempTop);
                    }

                    si = swapIndex;
                }

                dragOffsetY[1](e.pageY - containerTopY - spY + (index - si) * props.itemHeight);
                isDragging[1](true);
            });
        };
        node.onmouseup = () => {
            batch(() => {
                dragOffsetY[1](0);
                isDragging[1](false);
            });
            node.remove();
            props.onDragEnd?.();
        };
        document.body.appendChild(node);
    };

    return (
        <div ref={containerRef}
            style={{
                height: totalHeight() + "px",
                position: 'relative'
            }}>

            <For each={pool()}>
                {(poolItem) => {
                    return (
                        <Show when={poolItem.index() !== undefined}>
                            <div style={{
                                    position: 'absolute',
                                    top: `${poolItem.top() + poolItem.dragOffsetY()}px`,
                                    left: `0px`,
                                    height: `${props.itemHeight}px`,
                                    width: `100%`,
                                    "z-index": poolItem.isDragging() ? "1" : undefined
                                }}
                            >
                                <ErrorBoundary fallback={(err, reset) => <div></div>}>
                                    {poolItem.element}
                                </ErrorBoundary>
                            </div>
                        </Show>
                    );
                }}
            </For>
        </div>
    );
};

export default VirtualDragDropList;
