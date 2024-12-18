import { Show, type Component } from 'solid-js';

import styles from './index.module.css';

interface SideBarCreatorProps {
    icon?: string;
    name: string;
    selected?: boolean;
    collapsed?: boolean;
    onClick?: (event: MouseEvent) => void;
}

const SideBarCreator: Component<SideBarCreatorProps> = (props) => {
  const handleClick = (event: MouseEvent) => {
    if (props.onClick) {
      props.onClick(event);
    }
  };

  return (
    <div onClick={handleClick} class={styles.sideBarButton} classList={{[styles.selected]: props.selected, [styles.collapsed]: props.collapsed}}>
      <Show when={props.icon}>
        <img src={props.icon} class={styles.icon} alt="logo" />
      </Show>
      <div class={styles.text}>{props.name}</div>
    </div>
  );
};

export default SideBarCreator;
