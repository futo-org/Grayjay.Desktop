import { Component, createSignal, onCleanup, onMount, JSX, createMemo, children, untrack, For, createEffect } from "solid-js";
import { getNestedOffsetTop } from "../../../utility";

export interface VirtualFlexibleListProps {
    children: JSX.Element[];
    outerContainerRef: HTMLDivElement | undefined;
    overscan?: number;
    minimumItemHeight: number;
    notifyEndOnLast?: number;
    onEnd?: () => void;
};

//TODO: Add itemCount binding similar to VirtualGrid
const VirtualFlexibleList: Component<VirtualFlexibleListProps> = (props) => {
    const childElements = children(() => props.children);
    const allChildren = createMemo(() => {
        const allChildren = childElements();
        return Array.isArray(allChildren) ? allChildren : [ allChildren ];
    })

    let containerRef: HTMLDivElement | undefined;
    const [visibleRange, setVisibleRange] = createSignal({ startIndex: 0, endIndex: 0 });
    let cumulativeHeights: number[] = [];
    const [totalHeight, setTotalHeight] = createSignal<number>(0);

    const findStartIndex = (scrollTop: number, cumulativeHeights: number[]): number => {
        if (cumulativeHeights.length == 0) {
            return 0;
        }

        let low = 0;
        let high = cumulativeHeights.length - 1;
    
        while (low <= high) {
            let mid = Math.floor((low + high) / 2);
            if (cumulativeHeights[mid] < scrollTop) {
                low = mid + 1;
            } else if (mid > 0 && cumulativeHeights[mid - 1] >= scrollTop) {
                high = mid - 1;
            } else {
                return mid;
            }
        }
    
        return low > high ? cumulativeHeights.length - 1 : 0;
    };

    const updateVisibleRange = () => {
        if (!props.outerContainerRef || !containerRef) 
            return;

        const overscan = props.overscan ?? 1;
        
        const scrollTop = props.outerContainerRef.scrollTop - getNestedOffsetTop(containerRef, props.outerContainerRef);
        const boundingRect = props.outerContainerRef.getBoundingClientRect();
        
        let startIndex = findStartIndex(scrollTop, cumulativeHeights);
        let endIndex = startIndex;
        let totalHeight = 0;
        for (let i = startIndex; totalHeight < scrollTop + boundingRect.height; i++) {
            if (i < cumulativeHeights.length)
                totalHeight = cumulativeHeights[i];
            else
                totalHeight += props.minimumItemHeight;
            
            endIndex = i;
        }

        const childCount = allChildren().length;
        const maxIndex = childCount - 1;
        startIndex = Math.max(0, Math.min(startIndex - overscan, maxIndex));
        endIndex = Math.max(0, Math.min(endIndex + overscan, maxIndex));

        if (props.onEnd) {
            if (childCount - endIndex <= (props.notifyEndOnLast ?? 1)) {
                props.onEnd();
            }
        }

        setVisibleRange({ startIndex, endIndex });

        if (endIndex >= cumulativeHeights.length) {
            requestAnimationFrame(() => {
                updateItemHeights();
            });
        }
    };

    const updateItemHeights = () => {
        if (!containerRef) 
            return;

        const vr = untrack(visibleRange);
        const c = allChildren();
        for (let i = cumulativeHeights.length; i <= vr.endIndex; i++) {
            const child = c[i];
            if (!(child instanceof Element)) {
                throw Error("Child is not an Element!");
            }

            let height = child.getBoundingClientRect().height;
            if (height == 0) {
                break;
            }

            const top = (i > 0 ? cumulativeHeights[i - 1] : 0);
            cumulativeHeights[i] = top + height;

            const vis = visibleItems();
            if (i < vis.length) {
                vis[i].setTop(top);
            }
        }

        setTotalHeight(cumulativeHeights[cumulativeHeights.length - 1]);
    };

    const handleResize = () => {
        //TODO: Can this be more efficient?
        cumulativeHeights = [];
        updateVisibleRange();
    };

    const resizeObserver = new ResizeObserver(entries => {
        handleResize();
    });

    onMount(() => {
        //TODO: Debounce?
        updateVisibleRange();
        resizeObserver.observe(props.outerContainerRef!);
        //window.addEventListener('resize', handleResize);
        props.outerContainerRef?.addEventListener('scroll', updateVisibleRange);
    });

    onCleanup(() => {
        //TODO: Debounce?
        resizeObserver.unobserve(props.outerContainerRef!);
        //window.removeEventListener('resize', handleResize);
        props.outerContainerRef?.removeEventListener('scroll', updateVisibleRange);
        resizeObserver.disconnect();
    });

    const visibleItems = createMemo(() => {
        const range = visibleRange();
        const visibleSet = allChildren().slice(range.startIndex, range.endIndex + 1);

        return visibleSet.map((v, i) => { 
            const index = range.startIndex + i;
            const [top, setTop] = createSignal(index > 0 ? cumulativeHeights[index - 1] : 0); 
            return { v, index, top, setTop };
        });
    });

    return (
        <div ref={containerRef}
            style={{
                height: totalHeight() + "px", 
                position: 'relative'
            }}>

            <For each={visibleItems()}>
                {(item) => { 
                    //TODO: Hide elements at 0?
                    return (
                        <div
                            style={{
                                position: 'absolute',
                                top: `${(item.top())}px`,
                                left: item.top() !== undefined ? `0px` : '100vw',
                                width: `100%`
                            }}
                        >
                            {item.v}
                        </div>
                    )
                }}
            </For>
        </div>
    );
};

export default VirtualFlexibleList;