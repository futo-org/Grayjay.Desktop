import { Component, createSignal, onCleanup, onMount, JSX, createMemo, untrack, For, createEffect, Accessor, Show, batch, on } from "solid-js";
import { Portal } from "solid-js/web";
import { Event1 } from "../../../utility/Event";
import { getNestedOffsetTop } from "../../../utility";

export interface VirtualFlexibleArrayListProps {
    items?: any[];
    addedItems?: Event1<{startIndex: number, endIndex: number}>;
    modifiedItems?: Event1<{startIndex: number, endIndex: number}>;
    removedItems?: Event1<{startIndex: number, endIndex: number}>;
    builder: (index: Accessor<number | undefined>, item: Accessor<any>) => JSX.Element;
    outerContainerRef: HTMLDivElement | undefined;
    overscan?: number;
    notifyEndOnLast?: number;
    onEnd?: () => void;
};

const VirtualFlexibleArrayList: Component<VirtualFlexibleArrayListProps> = (props) => {
    let containerRef: HTMLDivElement | undefined;
    let measureContainerRef: HTMLDivElement | undefined;
    const [itemsToMeasure$, setItemsToMeasure] = createSignal<{ startIndex: number, endIndex: number }>();
    const [containerDimensions$, setContainerDimensions] = createSignal({ width: 0, height: 0 });
    const [visibleRange, setVisibleRange] = createSignal<{ startIndex: number, endIndex: number }>();
    const [heights$, setHeights] = createSignal<number[]>([]);
    const [cumulativeHeights$, setCumulativeHeights] = createSignal<number[]>([]);
    const [totalHeight, setTotalHeight] = createSignal<number>(0);
    const [poolSize, setPoolSize] = createSignal(0);

    const findStartIndex = (scrollTop: number, cumulativeHeights: number[]): number => {
        if (cumulativeHeights.length === 0) return 0;
    
        let low = 0;
        let high = cumulativeHeights.length - 1;
    
        while (low < high) {
            const mid = Math.floor((low + high) / 2);
            if (cumulativeHeights[mid] < scrollTop) {
                low = mid + 1;
            } else {
                high = mid;
            }
        }
    
        return cumulativeHeights[low] >= scrollTop ? low : cumulativeHeights.length;
    };

    const updateVisibleRange = () => {
        if (!props.outerContainerRef || !containerRef) 
            return;
    
        const overscan = props.overscan ?? 1;
        const scrollTop = props.outerContainerRef.scrollTop - getNestedOffsetTop(containerRef, props.outerContainerRef);        
        const cumulativeHeights = cumulativeHeights$();
        const itemCount = props.items?.length ?? 0;
        const maxIndex = itemCount - 1;
        let startIndex = findStartIndex(scrollTop, cumulativeHeights);
        startIndex = Math.max(0, Math.min(startIndex - 1 - overscan, maxIndex));
        let endIndex = startIndex;
        let totalHeight = 0;
        
        const outerBoundingRect = props.outerContainerRef.getBoundingClientRect();
        for (let i = startIndex; totalHeight < scrollTop + outerBoundingRect.height + outerBoundingRect.top; i++) {
            totalHeight = cumulativeHeights[i];            
            endIndex = i;
        }
    
        endIndex = Math.max(0, Math.min(endIndex + overscan, maxIndex));
    
        if (props.onEnd) {
            if (itemCount - endIndex <= (props.notifyEndOnLast ?? 1)) {
                props.onEnd();
            }
        }
    
        const currentVisibleRange = visibleRange();
        if (!currentVisibleRange || currentVisibleRange.startIndex !== startIndex || currentVisibleRange.endIndex !== endIndex)
            setVisibleRange({ startIndex, endIndex });
    };

    const handleScroll = () => {
        updateVisibleRange();
    };

    const handleResize = () => {
        if (containerDimensions$().width === props.outerContainerRef!.offsetWidth && containerDimensions$().height === props.outerContainerRef!.offsetHeight) {
            return;
        }

        const length = props.items?.length;
        if (length) 
            measureItems(0, length - 1, props.outerContainerRef!.offsetWidth, props.outerContainerRef!.offsetHeight);
    };

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

    const resizeObserver = new ResizeObserver(entries => {
        handleResize();
    });

    const measureItems = (startIndex: number, endIndex: number, containerWidth: number, containerHeight: number) => {
        if (!measureContainerRef || !containerWidth) {
            console.warn("measure items skipped because container ref is null or containerWidth is null or 0", {startIndex, endIndex, containerWidth, containerHeight, containerRef});
            return;
        }

        if (itemsToMeasure$()) {
            console.warn("measure items skipped because a measurement is already ongoing", {startIndex, endIndex, containerWidth, containerHeight, containerRef});
            return;
        }
        
        console.info("measure items", {startIndex, endIndex, containerWidth, containerHeight, containerRef});

        batch(() => {
            setContainerDimensions({ width: props.outerContainerRef!.offsetWidth, height: props.outerContainerRef!.offsetHeight });
            setItemsToMeasure({ startIndex, endIndex });
        });

        measureContainerRef!.style.width = `${containerWidth}px`;
        requestAnimationFrame(() => {
            requestAnimationFrame(() => {
                if (!measureContainerRef?.children) {
                    if (props.items)
                        console.error("Measure items but no children?");

                    batch(() => {
                        setItemsToMeasure(undefined);
                        setHeights([]);
                        setCumulativeHeights([]);
                        setTotalHeight(0);
                        setPoolSize(0);
                    });
                    return;
                }
                
                const hs: number[] = [ ... heights$().slice(0, startIndex) ];
                const chs: number[] = [ ... cumulativeHeights$().slice(0, startIndex) ];
                let minHeight = Infinity;
                let cumulativeHeight = chs.length > 0 ? (chs[chs.length - 1] + hs[hs.length - 1]) : 0;
                for (const child of measureContainerRef.children) {
                    const boundingRect = child.getBoundingClientRect();
                    const height = child.clientHeight;
                    const computedStyle = window.getComputedStyle(child);
                    const marginTop = parseFloat(computedStyle.marginTop);
                    const marginBottom = parseFloat(computedStyle.marginBottom);
                    const totalHeight = height + marginTop + marginBottom;
                    //console.info("child measurement", {child, height, totalHeight, marginTop, marginBottom, boundingRect});
                    hs.push(totalHeight);
                    chs.push(cumulativeHeight);
                    cumulativeHeight += totalHeight;
                    if (totalHeight < minHeight) {
                        minHeight = totalHeight;
                    }
                }

                const suffixHeights = heights$().slice(endIndex + 1);
                const suffixCumulativeHeights = cumulativeHeights$().slice(endIndex + 1).map(h => h + cumulativeHeight);
                hs.push(...suffixHeights);
                chs.push(...suffixCumulativeHeights);

                console.info("measure items result", {startIndex, endIndex, containerWidth, containerHeight, containerRef, hs, chs});

                batch(() => {
                    setItemsToMeasure(undefined);
                    setHeights(hs);
                    setCumulativeHeights(chs);
                    setTotalHeight(cumulativeHeight);

                    const desiredPoolSize = Math.floor(2 * (Math.ceil(containerDimensions$().height / minHeight) + (props.overscan ?? 1) * 2));
                    if (desiredPoolSize > poolSize()) {
                        console.log("desiredPoolSize larger than pool size, change pool", desiredPoolSize);
                        setPoolSize(desiredPoolSize);
                    }
                });

                updateVisibleRange();
            });
        });
    };
    
    let lastAddedItems: Event1<{ startIndex: number, endIndex: number }> | undefined;
    const attachAddedItems = (addedItems: Event1<{ startIndex: number, endIndex: number }> | undefined) => {
        lastAddedItems?.unregister(this);
        addedItems?.registerOne(this, (range) => {
            console.log("added event triggered", range);
            measureItems(range.startIndex, range.endIndex, untrack(containerDimensions$).width, untrack(containerDimensions$).height);
        });
        lastAddedItems = addedItems;
    };
    
    let lastRemovedItems: Event1<{ startIndex: number, endIndex: number }> | undefined;
    const attachRemovedItems = (removedItems: Event1<{ startIndex: number, endIndex: number }> | undefined) => {
        lastRemovedItems?.unregister(this);
        removedItems?.registerOne(this, (range) => {
            console.log("removed event triggered", range);
            measureItems(range.startIndex, Math.max(range.endIndex, props.items?.length ?? 0), untrack(containerDimensions$).width, untrack(containerDimensions$).height);
        });
        lastRemovedItems = removedItems;
    };

    let lastModifiedItems: Event1<{ startIndex: number, endIndex: number }> | undefined;
    const attachModifiedItems = (modifiedItems: Event1<{ startIndex: number, endIndex: number }> | undefined) => {
        lastModifiedItems?.unregister(this);
        modifiedItems?.registerOne(this, (range) => {
            console.log("modified event triggered", range);

            const poolItems = pool();
            batch(() => {
                for (const poolItem of poolItems) {
                    const i = poolItem.index();
                    if (i !== undefined && (i >= range.startIndex || i <= range.endIndex)) {
                        poolItem.setItem(props.items?.[i]);
                        break;
                    }
                }
            });

            measureItems(range.startIndex, range.endIndex, untrack(containerDimensions$).width, untrack(containerDimensions$).height);
        });
        lastModifiedItems = modifiedItems;
    };

    createEffect(() => attachAddedItems(props.addedItems));
    createEffect(() => attachRemovedItems(props.removedItems));
    createEffect(() => attachModifiedItems(props.modifiedItems));
    createEffect(() => {
        console.log("items changed", props.items);
        measureItems(0, props.items?.length ?? 0, untrack(containerDimensions$).width, untrack(containerDimensions$).height);
    });

    createEffect(() => {
        console.info("container dimensions changed", containerDimensions$());
    })

    onMount(() => {
        resizeObserver.observe(props.outerContainerRef!);
        props.outerContainerRef?.addEventListener('scroll', handleScroll);
        const length = props.items?.length;
        if (length)
            measureItems(0, length - 1, props.outerContainerRef!.offsetWidth, props.outerContainerRef!.offsetHeight);
        updateVisibleRange();
    });

    onCleanup(() => {
        props.addedItems?.unregister(this);
        props.modifiedItems?.unregister(this);
        props.removedItems?.unregister(this);
        lastAddedItems?.unregister(this);
        lastModifiedItems?.unregister(this);
        lastRemovedItems?.unregister(this);
        resizeObserver.unobserve(props.outerContainerRef!);
        props.outerContainerRef?.removeEventListener('scroll', handleScroll);
        resizeObserver.disconnect();
    });

    const pool = createMemo(on(poolSize, (size) => {
        console.log("pool cleared");
        return Array.from({ length: size }, (_, i) => {
            const [top$, setTop] = createSignal<number>(); 
            const [index$, setIndex] = createSignal<number>(); 
            const [item$, setItem] = createSignal<any>();
            const element = props.builder(index$, item$);
            
            return {
                top: top$,
                setTop,
                index: index$,
                setIndex,
                item: item$,
                setItem,
                element
            };
        });
    }));

    createEffect(() => {
        const range = visibleRange();
        const items = props.items;
        const cumulativeHeights = cumulativeHeights$();
        const poolItems = pool();
        if (!range || !items || !poolItems || !poolItems.length || itemsToMeasure$()) {
            return;
        }
        
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
        
                if (index < range.startIndex || index > range.endIndex) {
                    poolItem.setIndex(undefined);
                    poolItem.setItem(undefined);
                    poolItem.setTop(-1);
                }
            }

            if (previousStartIndex === Infinity && previousEndIndex === -Infinity) {
                const itemCount = Math.min(range.endIndex - range.startIndex + 1, props.items?.length ?? 0);
                if (poolItems.length < itemCount) {
                    console.error("pool size is not big enough to set all at once", {poolItems, poolItemsLength: poolItems.length, range, itemCount});
                    return;
                }
    
                for (let i = 0; i < itemCount; i++) {
                    const index = i + range.startIndex;
                    if (!poolItems[i]) {
                        console.error("pool size is not big enough, no item at index", {i, poolItem: poolItems[i], poolItems, poolItemsLength: poolItems.length, range, itemCount});
                    }
                    poolItems[i].setIndex(index);
                    poolItems[i].setItem(items[index]);
                    poolItems[i].setTop(cumulativeHeights[index]);
                }
            } else {
                for (let i = range.startIndex; i < previousStartIndex; i++) {
                    const unassignedPoolItem = getFreePoolItem();
                    if (unassignedPoolItem) {
                        unassignedPoolItem.setIndex(i);
                        unassignedPoolItem.setItem(items[i]);
                        unassignedPoolItem.setTop(cumulativeHeights[i]);
                    } else {
                        console.error("pool size is not big enough, no unused items");
                    }
                }
        
                for (let i = previousEndIndex + 1; i <= range.endIndex; i++) {
                    const unassignedPoolItem = getFreePoolItem();
                    if (unassignedPoolItem) {
                        unassignedPoolItem.setIndex(i);
                        unassignedPoolItem.setItem(items[i]);
                        unassignedPoolItem.setTop(cumulativeHeights[i]);
                    } else {
                        console.error("pool size is not big enough, no unused items");
                    }
                }
            }
        });
    });

    return (
        <>
            <div ref={containerRef}
                style={{
                    height: totalHeight() + "px", 
                    position: 'relative',
                    overflow: 'hidden'
                }}>

                <For each={pool()}>
                    {(poolItem) => (
                        <Show when={poolItem.index() !== undefined}>
                            <div style={{
                                    position: 'absolute',
                                    top: `${poolItem.top()}px`,
                                    width: `100%`
                                }}
                            >
                                {poolItem.element}
                            </div>
                        </Show>
                    )}
                </For>
            </div>
            <Portal>
                <div ref={measureContainerRef} style={{
                    "position": "fixed",
                    "top": "0px",
                    "left": "0px",
                    "pointer-events": "none",
                    "visibility": "hidden",
                    "display": itemsToMeasure$() ? "block" : "none"
                }}>
                    <Show when={itemsToMeasure$()}>
                        <For each={props.items?.slice(itemsToMeasure$()!.startIndex, itemsToMeasure$()!.endIndex + 1)}>
                            {(item, i) => 
                            { 
                                return (
                                    <div style={{ width: `100%` }}>
                                        {props.builder(i, () => item)}
                                    </div>
                                )
                            }}
                        </For>
                    </Show>
                </div>
            </Portal>
        </>
    );
};

export default VirtualFlexibleArrayList;