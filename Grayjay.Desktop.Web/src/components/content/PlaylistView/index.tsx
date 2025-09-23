import { Component, Show, createMemo, createSignal, onCleanup, onMount } from 'solid-js'

import styles from './index.module.css';
import IconButton from '../../buttons/IconButton';
import more from '../../../assets/icons/more_horiz_FILL0_wght400_GRAD0_opsz24.svg';
import { FocusableOptions, InputSource } from '../../../nav';
import { focusable } from '../../../focusable';import { useFocus } from '../../../FocusProvider';
 void focusable;

interface PlaylistViewProps {
  name?: string;
  itemCount?: number;
  thumbnail?: string;
  platformIconUrl?: string;
  onClick: () => void;
  onSettings?: (element: HTMLDivElement, inputSource: InputSource) => void;
  focusableOpts?: FocusableOptions;
}

const PlaylistView: Component<PlaylistViewProps> = (props) => {
  const focus = useFocus();
  const [containerWidth$, setContainerWidth] = createSignal<number>(260);

  const totalThumbnailsHeight$ = createMemo(() => {
    return containerWidth$() / 16.0 * 9.0 + 16.0;
  });

  let refContainer: HTMLDivElement | undefined;
  const resizeObserver = new ResizeObserver(entries => {
      setContainerWidth(refContainer?.clientWidth ?? 260.0);
  });

  onMount(() => {
    setContainerWidth(refContainer?.clientWidth ?? 260.0);
    resizeObserver.observe(refContainer!);
  });

  onCleanup(() => {
    resizeObserver.unobserve(refContainer!);
    resizeObserver.disconnect();
  });
  
  let refMoreButton: HTMLDivElement | undefined;
  return (
    <div class={styles.container} ref={refContainer} onClick={() => props.onClick()} use:focusable={props.focusableOpts}>
        <div class={styles.containerThumb} style={{
          height: `${totalThumbnailsHeight$()}px`
        }}>
          <div class={styles.thumb2}></div>
          <div class={styles.thumb1}></div>
          <Show when={props.thumbnail} fallback={
            <div class={styles.thumb}>No thumbnail available</div>
          }>
            <div class={styles.thumb} style={{
              "background-image": `url(${props.thumbnail})`,
              "background-size": "cover"
            }}></div>
            <Show when={props.platformIconUrl}>
              <img src={props.platformIconUrl} class={styles.sourceIcon} />
            </Show>
          </Show>
        </div>
        <div style="display: flex; flex-direction: row; align-items: center; margin-top: 16px;">
          <div style="display: flex; flex-direction: column; flex-grow: 1; overflow: hidden;">
            <div class={styles.title}>{props.name ?? 0}</div>
            <div class={styles.metadata}>{props.itemCount ?? 0} items</div>
          </div>

          <Show when={props.onSettings && focus?.isControllerMode() !== true} fallback={<div class="menu-anchor"></div>}>
            <IconButton icon={more} 
              ref={refMoreButton} 
              onClick={(e) => {
                props.onSettings?.(refMoreButton!, "pointer");
                e.preventDefault();
                e.stopPropagation();
              }}
              style={{
                "flex-shrink": "0",
                "margin-left": "4px"
              }} />
          </Show>
        </div>
    </div>
  );
};

export default PlaylistView;