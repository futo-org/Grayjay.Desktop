import { Accessor, Component, createEffect, createSignal, For, JSX, onCleanup, onMount, Show, batch, untrack, getOwner, runWithOwner } from "solid-js";
import { Event1 } from "../../../utility/Event";

export interface FlexibleArrayListProps {
    items?: any[];
    builder: (index: Accessor<number | undefined>, item: Accessor<any>) => JSX.Element;
    notifyEndOnLast?: number;
    onEnd?: () => void;
    outerContainerRef: HTMLDivElement | undefined;
    addedItems?: Event1<{ startIndex: number; endIndex: number }>;
    modifiedItems?: Event1<{ startIndex: number; endIndex: number }>;
    removedItems?: Event1<{ startIndex: number; endIndex: number }>;
    style?: JSX.CSSProperties;
}

const FlexibleArrayList: Component<FlexibleArrayListProps> = (props) => {
    const owner = getOwner();
    let containerRef: HTMLDivElement | undefined;

    const [pool, setPool] = createSignal(
        Array.from({ length: props.items?.length ?? 0 }, (_, i) => {
            const [index$, setIndex] = createSignal<number | undefined>(i);
            const [item$, setItem] = createSignal(props.items?.[i]);
            const element = props.builder(index$, item$);

            return {
                index: index$,
                setIndex,
                item: item$,
                setItem,
                element,
            };
        })
    );

    const handleScroll = () => {
        if (!props.outerContainerRef || !containerRef || !props.items || !props.onEnd) return;

        const itemsCount = props.items.length;
        if (itemsCount === 0) {
            props.onEnd?.();
            return;
        }

        const notifyIndex = Math.max(0, itemsCount - (props.notifyEndOnLast ?? 1));
        const lastChild = containerRef.children[notifyIndex] as HTMLElement;
        if (!lastChild) {
            console.warn("FlexibleArrayList not updated, element does not exist for index", {
                notifyIndex,
                items: props.items,
                children: containerRef.children
            });
            return;
        }

        const outerRect = props.outerContainerRef.getBoundingClientRect();
        const visibleHeight = outerRect.height + outerRect.top;
        const containerRect = lastChild.getBoundingClientRect();
        if (containerRect.top < visibleHeight) {
            props.onEnd?.();
        }
    };

    createEffect(() => {
        const items = props.items ?? [];
        const currentPool = untrack(pool);
        const newPoolSize = items.length;

        const updatedPool = [...currentPool];

        if (newPoolSize > currentPool.length) {
            for (let i = currentPool.length; i < newPoolSize; i++) {
                const [index$, setIndex] = createSignal<number | undefined>(i);
                const [item$, setItem] = createSignal(items[i]);
                updatedPool.push({
                    index: index$,
                    setIndex,
                    item: item$,
                    setItem,
                    element: props.builder(index$, item$),
                });
            }
        } else if (newPoolSize < currentPool.length) {
            updatedPool.splice(newPoolSize, currentPool.length - newPoolSize);
        }

        batch(() => {
            updatedPool.forEach((poolItem, index) => {
                poolItem.setIndex(index);
                poolItem.setItem(items[index]);
            });
            setPool(updatedPool);
        });
    });

    let lastAddedItems: Event1<{ startIndex: number; endIndex: number }> | undefined;
    const attachAddedItems = (addedItems: Event1<{ startIndex: number; endIndex: number }> | undefined) => {
        lastAddedItems?.unregister(this);
        addedItems?.registerOne(this, (range) => {
            runWithOwner(owner!, () => {
                const currentPool = pool();
                const updatedPool = [...currentPool];

                for (let i = range.startIndex; i <= range.endIndex; i++) {
                    const [index$, setIndex] = createSignal<number | undefined>(i);
                    const [item$, setItem] = createSignal(props.items?.[i]);
                    updatedPool.splice(i, 0, {
                        index: index$,
                        setIndex,
                        item: item$,
                        setItem,
                        element: props.builder(index$, item$),
                    });
                }

                setPool(updatedPool);
            });
        });
        lastAddedItems = addedItems;
    };

    let lastRemovedItems: Event1<{ startIndex: number; endIndex: number }> | undefined;
    const attachRemovedItems = (removedItems: Event1<{ startIndex: number; endIndex: number }> | undefined) => {
        lastRemovedItems?.unregister(this);
        removedItems?.registerOne(this, (range) => {
            const currentPool = pool();
            const updatedPool = [...currentPool];

            updatedPool.splice(range.startIndex, range.endIndex - range.startIndex + 1);
            setPool(updatedPool);
        });
        lastRemovedItems = removedItems;
    };

    let lastModifiedItems: Event1<{ startIndex: number; endIndex: number }> | undefined;
    const attachModifiedItems = (modifiedItems: Event1<{ startIndex: number; endIndex: number }> | undefined) => {
        lastModifiedItems?.unregister(this);
        modifiedItems?.registerOne(this, (range) => {
            const currentPool = pool();

            batch(() => {
                for (let i = range.startIndex; i <= range.endIndex; i++) {
                    const poolItem = currentPool[i];
                    if (poolItem) {
                        poolItem.setIndex(i);
                        poolItem.setItem(props.items?.[i]);
                    }
                }
            });
        });
        lastModifiedItems = modifiedItems;
    };

    createEffect(() => attachAddedItems(props.addedItems));
    createEffect(() => attachRemovedItems(props.removedItems));
    createEffect(() => attachModifiedItems(props.modifiedItems));

    onMount(() => {
        props.outerContainerRef?.addEventListener("scroll", handleScroll);
    });

    onCleanup(() => {
        props.addedItems?.unregister(this);
        props.modifiedItems?.unregister(this);
        props.removedItems?.unregister(this);
        lastModifiedItems?.unregister(this);
        lastRemovedItems?.unregister(this);
        lastAddedItems?.unregister(this);
        props.outerContainerRef?.removeEventListener("scroll", handleScroll);
    });

    return (
        <div
            ref={containerRef}
            style={{
                position: "relative",
                overflow: "hidden",
                width: "100%",
                ... props.style
            }}
        >
            <For each={pool()}>
                {(poolItem) => (
                    <Show when={poolItem.index() !== undefined}>
                        <div
                            style={{
                                width: `100%`,
                            }}
                        >
                            {poolItem.element}
                        </div>
                    </Show>
                )}
            </For>
        </div>
    );
};

export default FlexibleArrayList;
