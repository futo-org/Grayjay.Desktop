import { type Component, createSignal, onCleanup, Show, JSX, batch } from 'solid-js';
import styles from './index.module.css';
import chevDown from "../../../../assets/icons/icon_chrevron_down.svg"
import { Portal } from 'solid-js/web';
import Anchor, { AnchorStyle } from '../../../../utility/Anchor';
import SettingsMenu, { Menu } from '../../../menus/Overlays/SettingsMenu';
import { FocusableOptions, OpenIntent } from '../../../../nav';
import { focusScope } from '../../../../focusScope'; void focusScope;
import { focusable } from '../../../../focusable'; void focusable;

export interface SettingsDropdownProps {
    menu: Menu;
    anchorStyle?: AnchorStyle;
    label?: string;
    style?: JSX.CSSProperties;
    valueString?: string;
    focusable?: boolean;
};

const SettingsDropdown: Component<SettingsDropdownProps> = (props) => { 
  let selectElement: HTMLDivElement | undefined;

  const [showMenu$, setShowMenu] = createSignal(false);
  const [menuOpenIntent$, setMenuOpenIntent] = createSignal<OpenIntent>();
  const anchor = new Anchor(null, showMenu$, props.anchorStyle ?? AnchorStyle.BottomRight);
  const toggleMenu = async (el: HTMLElement, openIntent: OpenIntent) => {
    const newValue = !showMenu$();
    if (newValue)
      anchor.setElement(el);
    batch(() => {
      setShowMenu(newValue);
      setMenuOpenIntent(openIntent);
    });
  };

  const hideMenu = () => {
    setShowMenu(false);
  };

  onCleanup(() => {
    anchor.dispose();
  });

  const onPress = (openIntent: OpenIntent) => {
    if (selectElement) toggleMenu(selectElement, openIntent);
  };

  return (
    <>
      <div class={styles.selectContainer} ref={selectElement} onClick={() => onPress(OpenIntent.Pointer)} style={props.style} use:focusable={props.focusable === true ? { onPress: () => onPress(OpenIntent.Gamepad) } : undefined}>
          <div class={styles.select}>
              <div class={styles.selectText}>
                  <div style={{"display": "flex", "flex-direction": "column", "white-space": "nowrap", "text-overflow": "ellipsis"}}>
                      <Show when={props.label}>
                          <div class={styles.labelText}>{props.label}</div>
                      </Show>
                      {props.valueString ?? ""}
                  </div>
              </div>
              <div style={{"flex-grow": 1}}></div>
              <div class={styles.selectArrow}>
                  <img src={chevDown} style={{ transform: (showMenu$()) ? "rotate(-180deg)" : undefined }} />
              </div>
          </div>
      </div>
      <Portal>
        <SettingsMenu menu={props.menu} anchor={anchor} show={showMenu$()} onHide={hideMenu} ignoreGlobal={[selectElement]} openIntent={menuOpenIntent$()} />
      </Portal>
    </>
  );
};

export default SettingsDropdown;