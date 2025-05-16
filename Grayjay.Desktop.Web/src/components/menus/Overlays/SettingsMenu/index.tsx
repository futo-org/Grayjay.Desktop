
import { Component, For, JSX, Match, Show, Signal, Switch, batch, createEffect, createMemo, createSignal, onCleanup, onMount } from 'solid-js';
import styles from './index.module.css';
import chevron_right from '../../../../assets/icons/icon_chevron_right.svg';
import StateGlobal from '../../../../state/StateGlobal';
import { Event1 } from '../../../../utility/Event';
import Toggle from '../../../basics/inputs/Toggle';
import Anchor from '../../../../utility/Anchor';
import Checkbox from '../../../basics/inputs/Checkbox';
import CheckboxFlex from '../../../basics/inputs/CheckboxFlex';

export interface MenuItem {
  type?: string
}
export interface Menu {
  title?: string,
  items: MenuItem[]
}
export interface IMenuItemGroup extends MenuItem {
  key: string,
  value?: string,
  subMenu: Menu
}
export class MenuItemGroup implements IMenuItemGroup {
  type = "group"
  key: string
  value?: string
  subMenu: Menu

  constructor(key: string, value: string, subMenu: Menu) {
    this.key = key;
    this.value = value;
    this.subMenu = subMenu;
  }
}
export interface IMenuItemOption extends MenuItem {
  name: string,
  value: any,
  onSelected: (val: any) => void,
  isSelected: boolean
}
export class MenuItemOption implements IMenuItemOption {
  type = "option"
  name: string
  value: any
  onSelected: (val: any) => void
  isSelected: boolean

  constructor(name: string, value: any, isSelected: boolean, onSelected: (val: any) => void) {
    this.name = name;
    this.value = value;
    this.isSelected = isSelected;
    this.onSelected = onSelected;
  }
}

export class MenuItemHeader implements MenuItem {
  type = "header";
  name: string;
  description?: string;

  constructor(obj: { name: string, description?: string }) {
    this.name = obj.name;
    this.description = obj.description;
  }
}

export class MenuItemToggle implements MenuItem {
  type = "toggle";
  name: string;
  description?: string;
  isSelected: boolean;
  icon?: string;
  onToggle?: (value: boolean) => void;

  constructor(obj: { name: string, isSelected: boolean, description?: string, icon?: string, onToggle?: (value: boolean) => void }) {
    this.name = obj.name;
    this.description = obj.description;
    this.icon = obj.icon;
    this.isSelected = obj.isSelected;
    this.onToggle = obj.onToggle;
  }
}

export class MenuItemCheckbox implements MenuItem {
  type = "checkbox";
  name: string;
  description?: string;
  isSelected: boolean;
  icon?: string;
  onToggle?: (value: boolean) => void;

  constructor(obj: { name: string, isSelected: boolean, description?: string, icon?: string, onToggle?: (value: boolean) => void }) {
    this.name = obj.name;
    this.description = obj.description;
    this.icon = obj.icon;
    this.isSelected = obj.isSelected;
    this.onToggle = obj.onToggle;
  }
}

export interface IMenuButton extends MenuItem {
  name: string,
  icon: string,
  description?: string,
  onClick: () => void
}
export class MenuItemButton implements IMenuButton {
  type = "button"
  name: string
  icon: string
  description?: string
  onClick: () => void

  constructor(name: string, icon: string, description: string | undefined, onClick: () => void) {
    this.name = name;
    this.icon = icon;
    this.description = description;
    this.onClick = onClick;
  }
}

export class MenuSeperator implements MenuItem {
  type = "seperator"
}

export interface ShowEvent {
  invoker: HTMLElement | null,
  alignment?: Alignment
}

export enum Alignment {
  TopLeft = 0,
  TopRight = 1,
  BottomRight = 2,
  BottomLeft = 3
}

export interface SettingsMenuProps {
  menu: Menu,
  anchor?: Anchor,
  show: boolean,
  style?: JSX.CSSProperties,
  onHide?: ()=>void,
  ignoreGlobal?: (HTMLElement|undefined)[]
};
const SettingsMenu: Component<SettingsMenuProps> = (props: SettingsMenuProps) => {
    let containerRef: HTMLDivElement | undefined;

    const [alignment$, setAlignment] = createSignal(Alignment.TopLeft);
    const [invoker$, setInvoker] = createSignal<HTMLElement>();
    const [menu$, setMenu] = createSignal<Menu>(props.menu);
    const [menuStack$, setMenuStack] = createSignal<Menu[]>([]);
    const id = Math.random() * 100000;


    createEffect(() => {
      setMenu(props.menu);
      setMenuStack([props.menu]);
    });

    let lastState = false;
    createEffect(()=>{
      if(lastState != props.show) {
        lastState = props.show;
        if(props.show) {
          
        }
      }
    });

    onMount(()=>{
        StateGlobal.onGlobalClick.register((ev)=>{
            if(ev.target && !containerRef?.contains(ev.target as Node) && (!props.ignoreGlobal || props.ignoreGlobal.filter(x=>x && x.contains(ev.target as Node)).length == 0)) {
              if(props.onHide)
                props.onHide();
              if(menuStack$() && menuStack$().length > 0){
                const stack = menuStack$()
                setMenu(stack[0]);
                setMenuStack([stack[0]]);
              }
            }
        }, id);
    });
    onCleanup(()=>{
        StateGlobal.onGlobalClick.unregister(id);
    });

    function openGroup(group: IMenuItemGroup) {
        console.log("SubMenu", group);
        if(group.subMenu) {
            setMenuStack(menuStack$().concat([group.subMenu]));
            setMenu(group.subMenu);
        }
    }

    function selectOption(option: IMenuItemOption) {
        console.log("Option selected", option);
        option?.onSelected(option.value);
        if(props.onHide)
          props.onHide();
    }
    function clickButton(option: IMenuButton) {
        console.log("Button clicked", option);
        option?.onClick();
        if(props.onHide)
          props.onHide();
    }

    const renderToggle = (item: MenuItemToggle): JSX.Element => {
      const [value$, setValue] = createSignal(item.isSelected);
      return (
        <div class={styles.menuToggle} onClick={(ev) => {
          const v = !value$();
          item.onToggle?.(v);
          setValue(!value$());
        }}>
          <Show when={item.icon}>
            <img src={item.icon} class={styles.icon} />
          </Show>
          <div class={styles.nameContainer} style={
            {
              "margin-left": item.icon ? "16px" : "0px", 
              "max-width": item.icon ? "calc(100% - 124px)" : "calc(100% - 84px)"
            }
          }>
            <div class={styles.name}>{item.name}</div>
            <Show when={item.description}>
              <div class={styles.description}>{item.description}</div>
            </Show>
          </div>
          <div class={styles.toggle}>
            <Toggle value={value$()} onToggle={v => {
              item.onToggle?.(v);
              setValue(v);
            }} />
          </div>
        </div>
      );
    };

    const renderCheckbox = (item: MenuItemCheckbox): JSX.Element => {
      const [value$, setValue] = createSignal(item.isSelected);
      return (
        <div class={styles.menuToggle} onClick={(ev) => {
          const v = !value$();
          item.onToggle?.(v);
          setValue(!value$());
        }}>
          <Show when={item.icon}>
            <img src={item.icon} class={styles.icon} />
          </Show>
          <div class={styles.nameContainer} style={
            {
              "margin-left": item.icon ? "16px" : "0px", 
              "max-width": item.icon ? "calc(100% - 104px)" : "calc(100% - 64px)"
            }
          }>
            <div class={styles.name}>{item.name}</div>
            <Show when={item.description}>
              <div class={styles.description}>{item.description}</div>
            </Show>
          </div>
          <div class={styles.toggle}>
            <CheckboxFlex value={value$()} onChecked={v => {
              item.onToggle?.(v);
              setValue(v);
            }} />
          </div>
        </div>
      );
    };

    const renderHeader = (item: MenuItemHeader): JSX.Element => {
      return (
        <div class={styles.menuHeader}>
          <div class={styles.nameContainer}>
            <div class={styles.name}>{item.name}</div>
            <Show when={item.description}>
              <div class={styles.description}>{item.description}</div>
            </Show>
          </div>
        </div>
      );
    };
  
    return (
      <Show when={props.show}>
      <div class={styles.menu}  ref={containerRef} style={{ ...(props.anchor?.style$() ?? {}), ...props.style }}>
        <Show when={menu$()?.title}>
          <div class={styles.title}>
            {menu$()?.title}
          </div>
        </Show>
        <For each={menu$()?.items?.filter(x=>x)}>{ item =>
          <Switch fallback={
            <div></div>
          }>
            <Match when={item.type == "seperator"}>
              <div style="width: calc(100%); height: 1px; background: var(--grey-color-6); margin-top: 3px; margin-bottom: 3px;"></div>
            </Match>
            <Match when={item.type == "group"}>
              <div class={styles.menuItem} onClick={()=>openGroup(item as IMenuItemGroup)} classList={{[styles.isGroup]: true}}>
                <div class={styles.key}>
                  {(item as IMenuItemGroup).key}
                </div>
                <div class={styles.value}>
                  {(item as IMenuItemGroup).value}
                </div>
                <div class={styles.expander}>
                  <img src={chevron_right} />
                </div>
              </div>
            </Match>
            <Match when={item.type == "option"}>
              <div class={styles.menuItem} classList={{[styles.option]: true}} onClick={()=>selectOption(item as IMenuItemOption)}>
                <div class={styles.name} style={{
                  "font-weight": ((item as IMenuItemOption).isSelected) ? "bold" : "regular", 
                  color: ((item as IMenuItemOption).isSelected) ? "var(--white-color)" : "var(--grey-color-1)"
                }}>
                  {(item as IMenuItemOption).name}
                </div>
              </div>
            </Match>
            <Match when={item.type == "toggle"}>
              {renderToggle(item as MenuItemToggle)}
            </Match>
            <Match when={item.type == "checkbox"}>
              {renderCheckbox(item as MenuItemCheckbox)}
            </Match>
            <Match when={item.type == "header"}>
              {renderHeader(item as MenuItemHeader)}
            </Match>
            <Match when={item.type == "button"}>
              <div class={styles.menuButton} onClick={()=>clickButton(item as IMenuButton)}>
                <Show when={(item as IMenuButton).icon}>
                  <img class={styles.icon} src={(item as IMenuButton).icon} />
                </Show>
                  <Show when={(item as IMenuButton).description} fallback={
                    <div class={styles.text}>
                      <div class={styles.name}>
                        {(item as IMenuButton).name}
                      </div>
                    </div>
                  }>
                    <div class={styles.text}>
                      <div class={styles.nameWithDescription}>
                        {(item as IMenuButton).name}
                      </div>
                      <div class={styles.description}>
                        {(item as IMenuButton).description}
                      </div>
                    </div>
                  </Show>
              </div>
            </Match>
          </Switch>

        }</For>
      </div>
      </Show>
    );
  };
  
  export default SettingsMenu;