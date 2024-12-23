
import { Component, For, Match, Show, Switch, batch, createEffect, createMemo, createResource, createSignal, onCleanup, onMount } from 'solid-js';
import styles from './index.module.css';
import UIOverlay from '../../state/UIOverlay';
import { Event0 } from '../../utility/Event'
import InputText from '../../components/basics/inputs/InputText';
import Dropdown from '../../components/basics/inputs/Dropdown';
import Checkbox from '../../components/basics/inputs/Checkbox';
import icon_add from '../../assets/icons/icon24_add.svg';
import icon_close from '../../assets/icons/icon24_close.svg';

export interface DialogDescriptor {
  icon?: string,
  title: string,
  description?: string,
  code?: string,
  buttons: DialogButton[],
  defaultAction?: number,
  input?: IDialogInput,
  output?: IDialogOutput
}
export interface DialogButton {
  title: string,
  style?: string,
  onClick: (output: IDialogOutput) => void
}

export interface IDialogOutput {
  text: string,
  index: number,
  button: number,
  selected: any
}

interface IDialogInput {
  type: string
}
export class DialogInputCheckboxList implements IDialogInput {
  type = "checkboxList";
  values: { text: string, value: any }[];
  addLabel?: string;
  onAddClicked?: () => void;

  constructor(obj: { values: { text: string, value: any }[], addLabel?: string, onAddClicked?: () => void}) {
    this.values = obj.values;
    this.addLabel = obj.addLabel;
    this.onAddClicked = obj.onAddClicked;
  }
}
export class DialogInputText implements IDialogInput {
  type = "inputText";
  placeholder: string;
  value?: string;

  constructor(placeholder: string) {
    this.placeholder = placeholder;
  }
}
export class DialogDropdown implements IDialogInput {
  type = "dropdown";
  placeholder: string;
  options: string[];
  value?: number;

  constructor(options: string[], placeholder: string, value: number) {
    this.options = options;
    this.placeholder = placeholder;
  }
}

export interface OverlayDialogProps {
  dialog: DialogDescriptor | undefined,
  onGlobalDismiss?: Event0
};
const OverlayDialog: Component<OverlayDialogProps> = (props: OverlayDialogProps) => {

  let output = props.dialog?.output ?? {
    selected: [],
    text: ((props.dialog?.input?.type == "inputText") ? ((props.dialog?.input as DialogInputText).value ?? "") : ""),
    index: ((props.dialog?.input?.type == "dropdown") ? ((props.dialog?.input as DialogDropdown).value ?? -1) : -1)
  } as IDialogOutput;
  if (props.dialog)
    props.dialog.output = output;
  output.button = -1;

  props.onGlobalDismiss?.registerOne("dialog", () => {
    if (props.dialog) {
      const action = props.dialog.defaultAction ?? 0;
      if (props.dialog?.buttons && props.dialog!.buttons.length > action) {
        props.dialog?.buttons[action].onClick(output);
      }
      UIOverlay.dismiss();
    }
  });

  const renderInputCheckboxList = (input: DialogInputCheckboxList, output: IDialogOutput) => {
    return (
      <div style="display: flex; flex-direction: column; justify-content: center; align-items: flex-start;">
        <For each={input.values}>
          {(item, i) => {
            return (
              <Checkbox value={false}
                onChecked={(value) => { 
                  if (value) {
                    output.selected = [ ... (output?.selected ?? []), item.value ];
                  } else {
                    output.selected = output.selected?.filter((v: any) => v !== item.value) ?? [];
                  }
                }}
                label={item.text}
                style={{
                  "padding-top": "10px",
                  "padding-bottom": "10px",
                  "padding-left": "8px",
                  width: "100%"
                }} />
            );
          }}
        </For>
        <Show when={input.addLabel}>
          <div class={styles.addButton} onClick={() => {
            input.onAddClicked?.();
          }}>
            <img src={icon_add} style="height: 24px; width: 24px;" />
            <div class={styles.addLabel}>{input.addLabel}</div>  
          </div>
        </Show>
      </div>
    );
  };

  return (
    <Show when={props.dialog}>
      <div class={styles.dialog} onClick={(ev) => ev.stopPropagation()}>
        <Show when={props.dialog?.icon}>
          <img src={props.dialog?.icon} class={styles.icon} />
        </Show>
        <img src={icon_close} class={styles.iconClose} onClick={() => UIOverlay.dismiss()} />
        <div class={styles.title} style="padding-right: 25px;">
          {props.dialog!.title}
        </div>
        <div class={styles.description}>
          {props.dialog!.description}
        </div>
        <Show when={props.dialog?.code}>
          <div class={styles.code}>
            {props.dialog!.code}
          </div>
        </Show>
        <Show when={props.dialog?.input}>
          <div class={styles.input}>
            <Switch>
              <Match when={props.dialog?.input?.type == "inputText"}>
                <InputText
                  placeholder={(props.dialog?.input as DialogInputText).placeholder}
                  value={""}
                  onTextChanged={(newVal) => { output.text = newVal }} />
              </Match>
              <Match when={props.dialog?.input?.type == "dropdown"}>
                <Dropdown
                  options={(props.dialog?.input as DialogDropdown).options}
                  value={output.index}
                  onSelectedChanged={(newVal) => output.index = newVal} />
              </Match>
              <Match when={props.dialog?.input?.type == "checkboxList"}>
                {renderInputCheckboxList(props.dialog?.input as DialogInputCheckboxList, output)}
              </Match>
            </Switch>
          </div>
        </Show>
        <div class={styles.buttons}>
          <For each={props.dialog!.buttons}>{button =>
            <div class={styles.button} classList={{
              [styles.primary]: button.style == "primary",
              [styles.accent]: button.style == "accent",
              [styles.none]: button.style == "none" || !button.style
            }} onClick={(e) => { output.button = (props.dialog?.buttons.indexOf(button) ?? -1); UIOverlay.dismiss(); button.onClick(output); e.preventDefault(); e.stopPropagation() }}>
              {button.title}
            </div>
          }</For>
        </div>
      </div>
    </Show>
  );
};

export default OverlayDialog;