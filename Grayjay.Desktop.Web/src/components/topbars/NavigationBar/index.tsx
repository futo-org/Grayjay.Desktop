import type { Component, JSX } from 'solid-js';
import { createMemo, Show } from 'solid-js';

import styles from './index.module.css';
import { useNavigate } from '@solidjs/router';
import back from '../../../assets/icons/icon24_back.svg';
import cast from '../../../assets/icons/icon_32_cast.svg';
import SearchBar from '../SearchBar';
import { useCasting } from '../../../contexts/Casting';
import { ContentType } from '../../../backend/models/ContentType';
import { focusable } from '../../../focusable';import { useFocus } from '../../../FocusProvider';
import { Direction } from '../../../nav';
import IconButton from '../../buttons/IconButton';
 void focusable;

interface NavigationBarProps {
  initialText?: string;
  isRoot?: boolean;
  style?: JSX.CSSProperties;
  defaultSearchType?: ContentType;
  children?: JSX.Element | undefined;
  childrenAfter?: JSX.Element | undefined;
  suggestionsVisible?: boolean;
  groupEscapeTo?: Partial<Record<Direction, string[]>>;
  castGroupIndex?: number;
}

const NavigationBar: Component<NavigationBarProps> = (props) => {
  const focus = useFocus();
  const navigate = useNavigate();
  const casting = useCasting();
  const canGoBack$ = createMemo(() => !props.isRoot && history.length > 0 && focus?.isControllerMode() !== true);

  return (
    <div class={styles.containerTopBar} style={{ "margin-left": "24px", "margin-right": "24px", width: "calc(100% - 48px)", ... props.style}}>
      <Show when={canGoBack$()}>
        <IconButton
          icon={back}
          variant="ghost"
          shape="rounded"
          width="48px"
          height="48px"
          iconInset="12px"
          style={{ "flex-shrink": 0 }}
          onClick={() => navigate(-1)}
        />      
      </Show>
      <SearchBar id="main-search" style={{ "flex-grow": 1, "max-width": "700px" }} initialText={props.initialText} inputStyle={{ "margin-left": !canGoBack$() ? "0px" : "24px" }} overlayStyle={{ "margin-left": !canGoBack$() ? "0px" : "24px" }} defaultSearchType={props.defaultSearchType} suggestionsVisible={props.suggestionsVisible} focusableGroupOpts={{
        groupId: 'nav-bar',
        groupIndices: [0],
        groupType: 'horizontal',
        groupEscapeTo: props.groupEscapeTo
      }} />
      <Show when={props.children}>
        {props.children}
      </Show>

      <div style={{"flex-grow": 1}}></div>
      <Show when={props.childrenAfter}>
        {props.childrenAfter}
      </Show>

      <IconButton
        icon={cast}
        variant="none"
        style={{ "margin-left": "24px" }}
        onClick={() => casting?.actions.open()}
        focusableOpts={{
          groupId: 'nav-bar',
          groupIndices: [props.castGroupIndex ?? (props.childrenAfter ? 2 : 1)],
          groupType: 'horizontal',
          groupEscapeTo: props.groupEscapeTo,
          onPress: () => casting?.actions.open(),
        }}
      />
    </div>
  );
};

export default NavigationBar;