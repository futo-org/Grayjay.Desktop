import { Component, For, JSX, Show, createMemo } from 'solid-js'

import styles from './index.module.css';
import iconSearch from '../../assets/icons/icon24_search.svg'
import Button from '../buttons/Button';

interface EmptyContentAction {
  icon?: string,
  title: string,
  action: ()=>void,
  color?: string
}
interface EmptyContentViewProps {
  style?: JSX.CSSProperties;
  icon: string,
  title: string,
  description: string,
  actions: EmptyContentAction[]
}

const EmptyContentView: Component<EmptyContentViewProps> = (props) => {

  return (
    <div class={styles.container} style={props.style}>
      
      <div class={styles.noSubs}>
              <div class="icon">
                <img src={props.icon} style={{width: "96px", height: "auto", opacity: 0.4}} />
              </div>
              <div class={styles.title}>
                {props.title}
              </div>
              <div class={styles.description}>
                {props.description}
              </div>
              <div class={styles.buttons}>
                <For each={props.actions}>{ (btn: EmptyContentAction) =>
                  <Button text={btn.title} color={btn.color} icon={btn.icon} onClick={()=>{btn.action()}} style={{margin: "8px", width: "280px", height: "58px"}} />
                }</For>
              </div>
            </div>
    </div>
  );
};

export default EmptyContentView;