import { Show, type Component } from 'solid-js';

import styles from './index.module.css';

interface SideBarButtonProps {
    icon?: string;
    name: string;
    selected?: boolean;
    collapsed?: boolean;
    onClick?: (event: MouseEvent) => void;
    onRightClick?: (event: MouseEvent) => void;
}

const SideBarButton: Component<SideBarButtonProps> = (props) => {
  const handleClick = (event: MouseEvent) => {
    if (props.onClick) {
      props.onClick(event);
    }
  };
  const handleRightClick = (event: MouseEvent) => {
    if (props.onRightClick) {
      props.onRightClick(event);
    }
  };

  return (
    <div onClick={handleClick} onContextMenu={handleRightClick} class={styles.sideBarButton} classList={{[styles.selected]: props.selected, [styles.collapsed]: props.collapsed}}>
      <Show when={props.icon}>
        <img src={props.icon} class={styles.icon} alt="logo" />
      </Show>
      <div class={styles.text}>{props.name}</div>
    </div>
  );
};

export default SideBarButton;
