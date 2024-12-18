import { Component, createSignal, onCleanup, onMount, For, JSX, createMemo, createEffect, ErrorBoundary, Accessor, Show, batch, on, untrack } from "solid-js";
import { Event0, Event1 } from "../../../utility/Event";

export interface VirtualGridProps {
    itemHeight?: number;
    itemWidth: number;
    items?: any[];
    builder: (index: Accessor<number | undefined>, item: Accessor<any>) => JSX.Element;
    outerContainerRef: HTMLDivElement | undefined;
    overscan?: number;
    notifyEndOnLast?: number;
    onEnd?: () => void;
    autosizeWidth?: boolean;
    calculateHeight?: (width: number) => number;
    style?: JSX.CSSProperties;
    elementStyle?: JSX.CSSProperties;
    maximumRowsVisible?: number;
    addedItems?: Event1<{startIndex: number, endIndex: number}>;
    modifiedItems?: Event1<{startIndex: number, endIndex: number}>;
    removedItems?: Event1<{startIndex: number, endIndex: number}>;
};

interface RenderProperties {
    startRow: number;
    endRow: number;
    itemsPerRow: number;
    itemWidth: number;
    itemHeight: number;
    truncated: boolean;
}

const VirtualGrid: Component<VirtualGridProps> = (props) => {
    let containerRef: HTMLDivElement | undefined;

    const [poolSize, setPoolSize] = createSignal(0);
    const [renderProperties, setRenderProperties] = createSignal<RenderProperties>({ 
        startRow: 0, 
        endRow: 0, 
        itemsPerRow: 0, 
        itemWidth: props.itemWidth, 
        itemHeight: props.itemHeight ?? props.itemWidth,
        truncated: false
    });

    const getNestedOffsetTop = (element: HTMLElement, ancestor: HTMLElement): number => {
        let offsetTop = 0;
        let currentElement: HTMLElement | null = element;
        
        while (currentElement && currentElement !== ancestor) {
            offsetTop += currentElement.offsetTop;
            currentElement = currentElement.offsetParent as HTMLElement;
        }
        
        return offsetTop;
    };

    const calculateVisibleRange = () => {
        if (!props.outerContainerRef || !containerRef) return;
        
        const overscan = props.overscan ?? 1;
        const boundingRect = props.outerContainerRef.getBoundingClientRect();
        const containerWidth = boundingRect.width - 40;
        const itemsPerRow = Math.floor(containerWidth / props.itemWidth);
        const itemWidth = props.autosizeWidth === true ? containerWidth / itemsPerRow : props.itemWidth;
        const itemHeight = props.calculateHeight ? props.calculateHeight(itemWidth) : props.itemHeight ?? itemWidth;
        const rowsInView = Math.ceil(boundingRect.height / itemHeight);
        const scrollOffset = props.outerContainerRef.scrollTop - getNestedOffsetTop(containerRef, props.outerContainerRef);
        const desiredRowCount = Math.ceil((props.items?.length ?? 0) / itemsPerRow);
        const rowCount = props.maximumRowsVisible ? Math.min(props.maximumRowsVisible, desiredRowCount) : desiredRowCount

        const startRowIndex = Math.floor(scrollOffset / itemHeight);
        const startRow = Math.max(0, Math.min(startRowIndex - overscan, rowCount - 1));
        const endRow = Math.max(0, Math.min(startRowIndex + rowsInView + overscan, rowCount - 1));

        if (props.onEnd) {
            if (rowCount - endRow <= (props.notifyEndOnLast ?? 1)) {
                props.onEnd();
            }
        }

        const newVisibleRange = { startRow, endRow, itemsPerRow, itemWidth, itemHeight, truncated: rowCount < desiredRowCount };
        batch(() => {
            setRenderProperties(newVisibleRange);
                
            const desiredPoolSize = Math.floor(2 * (itemsPerRow * (rowsInView + (props.overscan ?? 1) * 2)));
            if (desiredPoolSize > poolSize()) {
                console.log("desiredPoolSize larger than pool size, change pool", desiredPoolSize);
                setPoolSize(desiredPoolSize);
            }
        });
    };

    const onUIEvent = () => {
        calculateVisibleRange();
        batch(() => {
            const p = renderProperties();
            const poolItems = pool();
            for (const poolItem of poolItems) {
                const index = poolItem.index();
                if (index === undefined) {
                    continue;
                }

                const rowIndex = Math.floor(index / p.itemsPerRow);
                const colIndex = index % p.itemsPerRow;
                poolItem.setTop(rowIndex * p.itemHeight);
                poolItem.setLeft(colIndex * p.itemWidth);
            }
        });
    };

    const getFreePoolItem = () => {
        const rp = renderProperties();
        const startIdx = props.items ? rp.startRow * rp.itemsPerRow : 0;
        const endIdx = props.items ? Math.min((rp.endRow + 1) * rp.itemsPerRow - 1, props.items.length - 1) : 0;
        return pool().find(item => {
            const index = item.index();
            return index === undefined || index < startIdx || index > endIdx;
        });
    };

    const resizeObserver = new ResizeObserver(entries => {
        onUIEvent();
    });

    let lastAddedItems: Event1<{ startIndex: number, endIndex: number }> | undefined;
    const attachAddedItems = (addedItems: Event1<{ startIndex: number, endIndex: number }> | undefined) => {
        console.log("attachAddedItems", {lastAddedItems, addedItems});

        lastAddedItems?.unregister(this);
        addedItems?.registerOne(this, (range) => {
            console.log("added event triggered", range);
            const rp = renderProperties();
            const items = props.items;
            const endIdx = props.items ? Math.min((rp.endRow + 1) * rp.itemsPerRow - 1, props.items.length - 1) : 0;
    
            if (items) {
                batch(() => {
                    for (let i = range.startIndex; i <= Math.min(range.endIndex, endIdx); i++) {
                        const rowIndex = Math.floor(i / rp.itemsPerRow);
                        const colIndex = i % rp.itemsPerRow;
                        const unassignedPoolItem = getFreePoolItem();
                        if (unassignedPoolItem) {
                            unassignedPoolItem.setIndex(i);
                            unassignedPoolItem.setItem(items[i]);
                            unassignedPoolItem.setTop(rowIndex * rp.itemHeight);
                            unassignedPoolItem.setLeft(colIndex * rp.itemWidth);
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
                            poolItem.setLeft(-1);
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
        const rp = untrack(renderProperties);
        const startIdx = props.items ? rp.startRow * rp.itemsPerRow : 0;
        const endIdx = props.items ? Math.min((rp.endRow + 1) * rp.itemsPerRow - 1, props.items.length - 1) : 0;

        batch(() => {
            for (const poolItem of poolItems) {
                const i = untrack(poolItem.index);
                if (i !== undefined) {
                    const item = props.items?.[i];
                    if (i >= startIdx && i <= endIdx && item) {
                        poolItem.setItem(item);
                    } else {
                        poolItem.setIndex(undefined);
                        poolItem.setItem(undefined);
                        poolItem.setTop(-1);
                    }
                }
            }
        });
        
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

    const pool = createMemo(on(poolSize, (size) => {
        return Array.from({ length: size }, (_, i) => {
            const [top$, setTop] = createSignal<number>(-1);
            const [left$, setLeft] = createSignal<number>(-1);
            const [index$, setIndex] = createSignal<number | undefined>(undefined);
            const [item$, setItem] = createSignal<any>();
            const element = props.builder(index$, item$);
            return {
                top: top$,
                left: left$,
                setTop,
                setLeft,
                index: index$,
                setIndex,
                item: item$,
                setItem,
                element
            };
        });
    }));

    createEffect(() => {
        const p = renderProperties();
        const items = props.items;
        const poolItems = pool();
        const startIdx = p.startRow * p.itemsPerRow;

        if (!items) {
            return;
        }

        const endIdx = Math.min((p.endRow + 1) * p.itemsPerRow - 1, items.length - 1);
        batch(() => {       
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
                    poolItem.setLeft(-1);
                }
            }
            
            if (previousStartIndex === Infinity && previousEndIndex === -Infinity) {
                const itemCount = endIdx - startIdx + 1;
                if (poolItems.length < itemCount) {
                    console.error("pool size is not big enough to set all at once", {poolItems, poolItemsLength: poolItems.length, startIdx, endIdx, itemCount});
                    return;
                }
        
                for (let i = 0; i < itemCount; i++) {
                    const index = i + startIdx;
                    const rowIndex = Math.floor(index / p.itemsPerRow);
                    const colIndex = index % p.itemsPerRow;
                    poolItems[i].setIndex(index);
                    poolItems[i].setItem(items[index]);
                    poolItems[i].setTop(rowIndex * p.itemHeight);
                    poolItems[i].setLeft(colIndex * p.itemWidth);
                }
            } else {
                
                for (let i = startIdx; i < previousStartIndex; i++) {
                    const unassignedPoolItem = getFreePoolItem();
                    if (unassignedPoolItem) {
                        const rowIndex = Math.floor(i / p.itemsPerRow);
                        const colIndex = i % p.itemsPerRow;
                        unassignedPoolItem.setIndex(i);
                        unassignedPoolItem.setItem(items[i]);
                        unassignedPoolItem.setTop(rowIndex * p.itemHeight);
                        unassignedPoolItem.setLeft(colIndex * p.itemWidth);
                    } else {
                        console.error("pool size is not big enough, no unused items");
                    }
                }

                
                for (let i = previousEndIndex + 1; i <= endIdx; i++) {
                    const unassignedPoolItem = getFreePoolItem();
                    if (unassignedPoolItem) {
                        const rowIndex = Math.floor(i / p.itemsPerRow);
                        const colIndex = i % p.itemsPerRow;
                        unassignedPoolItem.setIndex(i);
                        unassignedPoolItem.setItem(items[i]);
                        unassignedPoolItem.setTop(rowIndex * p.itemHeight);
                        unassignedPoolItem.setLeft(colIndex * p.itemWidth);
                    } else {
                        console.error("pool size is not big enough, no unused items");
                    }
                }
            }
        });
    });
    
    const rowCount = createMemo(() => {
        const p = renderProperties();
        return Math.ceil((props.items?.length ?? 0) / p.itemsPerRow);
    });

    const height = createMemo(() => {
        const rc = rowCount();
        const maxRowCount = props.maximumRowsVisible;
        if (maxRowCount !== undefined && rc > maxRowCount)
            return maxRowCount * renderProperties().itemHeight;
        return rc * renderProperties().itemHeight
    });

    return (
        <div ref={containerRef}
            style={{ 
                ... props.style,
                width: renderProperties().itemsPerRow * renderProperties().itemWidth + "px", 
                height: height() + "px", 
                position: 'relative'
            }}>

            <For each={pool()}>
                {(poolItem) => (
                    <Show when={poolItem.index() !== undefined}>
                        <div
                            style={{
                                ... props.elementStyle,
                                position: 'absolute',
                                top: `${poolItem.top()}px`,
                                left: `${poolItem.left()}px`,
                                height: `${renderProperties().itemHeight}px`,
                                width: `${renderProperties().itemWidth}px`,
                                //transition: "width 0.2s ease, left 0.1s ease"
                            }}
                        >
                            <ErrorBoundary fallback={(err, reset) => <div></div>}>
                                {poolItem.element}
                            </ErrorBoundary>                        
                        </div>
                    </Show>
                )}
            </For>
        </div>
    );
};

export default VirtualGrid;
