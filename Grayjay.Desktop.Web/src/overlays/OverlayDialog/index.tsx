import { Component, For, Match, Show, Switch, createEffect, createMemo, createSignal } from 'solid-js';
import styles from './index.module.css';
import UIOverlay from '../../state/UIOverlay';
import { Event0 } from '../../utility/Event'
import InputText from '../../components/basics/inputs/InputText';
import Dropdown from '../../components/basics/inputs/Dropdown';
import Checkbox from '../../components/basics/inputs/Checkbox';
import icon_add from '../../assets/icons/icon24_add.svg';
import icon_close from '../../assets/icons/icon24_close.svg';
import icon_copy from '../../assets/icons/copy.svg';
import Tooltip from '../../components/tooltip';
import ScrollContainer from '../../components/containers/ScrollContainer';
import { focusScope } from '../../focusScope'; void focusScope;
import { focusable } from '../../focusable'; void focusable;
import { createMutable } from 'solid-js/store';
import { FocusableOptions } from '../../nav';

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
  onClick: (output: IDialogOutput) => void,
  focusableOpts: FocusableOptions
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
    this.value = value;
  }
}

export interface OverlayDialogProps {
  dialog: DialogDescriptor | undefined,
  onGlobalDismiss?: Event0
};

const OverlayDialog: Component<OverlayDialogProps> = (props: OverlayDialogProps) => {
  let containerRef: HTMLDivElement | undefined;
  const titleId = `dialog-title-${Math.random().toString(36).slice(2)}`;
  const output = createMutable<IDialogOutput>({
    text: '',
    index: -1,
    button: -1,
    selected: []
  });

  createEffect(() => {
    const d = props.dialog;
    if (!d) return;

    output.text = d.input?.type === 'inputText' ? ((d.input as DialogInputText).value ?? '') : '';
    output.index = d.input?.type === 'dropdown' ? ((d.input as DialogDropdown).value ?? -1) : -1;
    output.selected = Array.isArray(d.output?.selected) ? [...(d.output!.selected as any[])] : [];
    output.button = -1;

    d.output = output;
  });

  props.onGlobalDismiss?.registerOne("dialog", () => {
    triggerDefaultAction();
  });

  const hasAnyInput$ = createMemo(() => !!props.dialog?.input);
  const primaryIndex$ = createMemo(() => props.dialog?.buttons?.findIndex(b => b.style === "primary") ?? -1);

  const triggerDefaultAction = () => {
    if (props.dialog) {
      const action = props.dialog.defaultAction ?? 0;
      if (props.dialog.buttons && props.dialog.buttons.length > action) {
        output.button = action;
        props.dialog.buttons[action].onClick(output);
      }
    }
    UIOverlay.dismiss();
  };

  const dialogBack = () => {
    triggerDefaultAction();
    return true;
  };

  const clickClose = () => {
    UIOverlay.dismiss();
  };

  const pressButton = (btn: DialogButton) => {
    output.button = (props.dialog?.buttons.indexOf(btn) ?? -1);
    UIOverlay.dismiss();
    btn.onClick(output);
  };

  const renderInputCheckboxList = (input: DialogInputCheckboxList, output: IDialogOutput) => {
    const list = () => Array.isArray(output.selected) ? output.selected as any[] : [];
    const isChecked = (val: any) => list().includes(val);
    const setSelectedFor = (val: any, next: boolean) => {
      const curr = list();
      const has = curr.includes(val);
      if (next && !has) output.selected = [...curr, val];
      if (!next && has)  output.selected = curr.filter(v => v !== val);
    };

    const toggle = (val: any) => setSelectedFor(val, !isChecked(val));

    return (
      <div style="display: flex; flex-direction: column; justify-content: center; align-items: flex-start;">
        <For each={input.values}>
          {item => {
            return (
              <div
                style={{ width: "100%" }}
                use:focusable={{
                  onPress: () => toggle(item.value),
                  onBack: dialogBack,
                }}
              >
                <Checkbox
                  value={isChecked(item.value)}
                  onChecked={(next) => {
                    setSelectedFor(item.value, next);
                  }}
                  label={item.text}
                  style={{
                    "padding-top": "10px",
                    "padding-bottom": "10px",
                    "padding-left": "8px",
                    width: "100%"
                  }}
                />
              </div>
            );
          }}
        </For>

        <Show when={input.addLabel}>
          <div
            class={styles.addButton}
            onClick={() => input.onAddClicked?.()}
            use:focusable={{
              onPress: () => input.onAddClicked?.(),
              onBack: dialogBack,
            }}
          >
            <img src={icon_add} style="height: 24px; width: 24px;" />
            <div class={styles.addLabel}>{input.addLabel}</div>
          </div>
        </Show>
      </div>
    );
  };
  
  return (
    <Show when={props.dialog}>
      <div
        class={styles.dialog}
        ref={containerRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        onClick={(ev) => ev.stopPropagation()}
        use:focusScope={{
          trap: true,
          wrap: true,
          orientation: "vertical"
        }}
      >
        <Show when={props.dialog?.icon}>
          <img src={props.dialog?.icon} class={styles.icon} alt="" />
        </Show>

        <img
          src={icon_close}
          class={styles.iconClose}
          alt="Close"
          role="button"
          tabindex={0}
          onClick={clickClose}
          use:focusable={{
            onPress: clickClose,
            onBack: dialogBack,
          }}
        />

        <div id={titleId} class={styles.title} style="padding-right: 25px;">
          {props.dialog!.title}
        </div>

        <ScrollContainer
          wrapperStyle={{
            height: "calc(100% - 70px)",
            width: "100%",
            "margin-left": "-32px",
            "margin-right": "-32px",
            "padding-left": "32px",
            "padding-right": "32px",
            "margin-bottom": "-32px"
          }}
        >
          <div class={styles.description}>
            {props.dialog!.description}
          </div>

          <Show when={props.dialog?.code}>
            <div class={styles.code}>
              <Tooltip text="Copy all">
                <img
                  src={icon_copy}
                  style="width: 16px; height: 16px; margin-right: 6px; user-select: none;"
                  onClick={async () => {
                    await navigator.clipboard.writeText(props.dialog!.code!);
                    UIOverlay.toast("Text has been copied");
                  }}
                  role="button"
                  tabindex={0}
                  use:focusable={{
                    onPress: async () => {
                      await navigator.clipboard.writeText(props.dialog!.code!);
                      UIOverlay.toast("Text has been copied");
                    },
                    onBack: dialogBack,
                  }}
                />
              </Tooltip>
              {props.dialog!.code}
            </div>
          </Show>

          <Show when={props.dialog?.input}>
            <div class={styles.input}>
              <div>
                <Show when={props.dialog?.input?.type == "inputText"}>
                  <InputText
                    placeholder={(props.dialog?.input as DialogInputText).placeholder}
                    value={output.text}
                    onTextChanged={(newVal) => { output.text = newVal }}
                    focusableOpts={{
                      onBack: dialogBack
                    }}
                  />
                </Show>

                <Show when={props.dialog?.input?.type == "dropdown"}>
                  <div
                    use:focusable={{ onBack: dialogBack }}
                  >
                    <Dropdown
                      options={(props.dialog?.input as DialogDropdown).options}
                      value={output.index}
                      onSelectedChanged={(newVal) => output.index = newVal}
                    />
                  </div>
                </Show>

                <Show when={props.dialog?.input?.type == "checkboxList"}>
                  {renderInputCheckboxList(props.dialog?.input as DialogInputCheckboxList, output)}
                </Show>
              </div>
            </div>
          </Show>

          <div class={styles.buttons}>
            <For each={props.dialog!.buttons}>
              {(button, i) => {
                const isPrimary = button.style === "primary";
                const isAutofocusButton =
                  !hasAnyInput$() && (
                    (primaryIndex$() >= 0 ? i() === primaryIndex$() : i() === 0)
                  );

                return (
                  <div
                    class={styles.button}
                    classList={{
                      [styles.primary]: isPrimary,
                      [styles.accent]: button.style == "accent",
                      [styles.none]: button.style == "none" || !button.style
                    }}
                    tabindex={0}
                    data-autofocus={isAutofocusButton ? '' : undefined}
                    onClick={(e) => {
                      pressButton(button);
                      e.preventDefault();
                      e.stopPropagation();
                    }}
                    use:focusable={{
                      onPress: () => pressButton(button),
                      onBack: dialogBack,
                    }}
                  >
                    {button.title}
                  </div>
                );
              }}
            </For>
          </div>
        </ScrollContainer>
      </div>
    </Show>
  );
};

export default OverlayDialog;