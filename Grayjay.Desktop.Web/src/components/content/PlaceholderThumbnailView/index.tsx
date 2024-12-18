import { Component, JSX, Show } from 'solid-js'

import styles from './index.module.css';
import IconButton from '../../buttons/IconButton';
import more from '../../../assets/icons/more_horiz_FILL0_wght400_GRAD0_opsz24.svg';
import { proxyImage, toHumanNowDiffString, toHumanNumber } from '../../../utility';
import { DateTime } from 'luxon';
import SkeletonDiv from '../../basics/loaders/SkeletonDiv';
import { IPlatformContentPlaceholder } from '../../../backend/models/content/IPlatformContentPlaceholder';

interface PlaceholderProps {
  placeholder?: IPlatformContentPlaceholder;
  style?: JSX.CSSProperties;
}

const PlaceholderThumbnailView: Component<PlaceholderProps> = (props) => {
  return (
    <div class={styles.container} style={props.style}>
      <Show when={!props.placeholder?.error}>
        <div class={styles.placeholderThumbnail}>
          <SkeletonDiv>
            <Show when={props.placeholder?.placeholderIcon}>
              <img src={props.placeholder?.placeholderIcon} class={styles.placeholderIcon} />
            </Show>
          </SkeletonDiv>
        </div>
        <div class={styles.title}>
          <SkeletonDiv />
        </div>
        <div class={styles.bottomRow}>
            <div  class={styles.authorThumbnail}>
              <SkeletonDiv />
            </div>
            <div class={styles.authorColumn}>
                <div class={styles.authorName}>
                  <SkeletonDiv />
                </div>
                <div class={styles.metadata}>
                  <SkeletonDiv />
                </div>
            </div>
        </div>
      </Show>
      <Show when={!!props.placeholder?.error}>
        <Show when={props.placeholder?.placeholderIcon}>
          <img src={props.placeholder?.placeholderIcon} class={styles.placeholderIcon} />
        </Show>
        <div class={styles.error}>
          {props.placeholder?.error}
        </div>
      </Show>
    </div>
  );
};

export default PlaceholderThumbnailView;