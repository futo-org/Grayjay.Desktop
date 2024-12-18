import type { Component, JSX } from 'solid-js';
import { Show } from 'solid-js';

import styles from './index.module.css';
import { useNavigate } from '@solidjs/router';
import back from '../../../assets/icons/icon24_back.svg';
import cast from '../../../assets/icons/icon_32_cast.svg';
import TransparentIconButton from '../../buttons/TransparentIconButton';
import SearchBar from '../SearchBar';
import { useCasting } from '../../../contexts/Casting';
import { ContentType } from '../../../backend/models/ContentType';

interface NavigationBarProps {
  initialText?: string;
  isRoot?: boolean;
  style?: JSX.CSSProperties;
  defaultSearchType?: ContentType;
  children?: JSX.Element | undefined;
  childrenAfter?: JSX.Element | undefined;
}

const NavigationBar: Component<NavigationBarProps> = (props) => {
  const navigate = useNavigate();
  const casting = useCasting();
  const canGoBack = !props.isRoot && history.length > 0;

  return (
    <div class={styles.containerTopBar} style={{ "margin-left": "24px", "margin-right": "24px", width: "calc(100% - 48px)", ... props.style}}>
      <Show when={canGoBack}>
        <TransparentIconButton icon={back} onClick={() => navigate(-1)} style={{"flex-shrink":0}} />
      </Show>
      <SearchBar style={{ "flex-grow": 1, "max-width": "700px" }} initialText={props.initialText} inputStyle={{ "margin-left": !canGoBack ? "0px" : "24px" }} overlayStyle={{ "margin-left": !canGoBack ? "0px" : "24px" }} defaultSearchType={props.defaultSearchType} />
      <Show when={props.children}>
        {props.children}
      </Show>

      <div style={{"flex-grow": 1}}></div>
      <Show when={props.childrenAfter}>
        {props.childrenAfter}
      </Show>

      <img src={cast} style={{"margin-left": "24px", "cursor": "pointer" }} onClick={() => casting?.actions.open()} />
    </div>
  );
};

export default NavigationBar;