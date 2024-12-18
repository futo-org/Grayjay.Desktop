import { Component, onMount } from "solid-js";
import styles from './index.module.css';
import ContentGrid from "../../../containers/ContentGrid";
import { PlaceholderPager } from "../../../../backend/models/pagers/PlaceholderPager";
import ScrollContainer from "../../../containers/ScrollContainer";

export interface SkeletonProps {
    itemCount?: number
};

const LoaderGrid: Component<SkeletonProps> = (props) => {
    const pager = new PlaceholderPager(props.itemCount ?? 12);
    
    onMount(async ()=>{
        await pager.load();
    });

    let scrollContainerRef: HTMLDivElement | undefined;

    return (
        <div class={styles.loaderGrid}>
            <ScrollContainer ref={scrollContainerRef}>
                <ContentGrid pager={pager} outerContainerRef={scrollContainerRef} />
            </ScrollContainer>
        </div>
    );
};

export default LoaderGrid;
