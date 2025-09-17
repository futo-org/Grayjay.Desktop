import { Component, JSX, Show, createEffect, createSignal, mergeProps } from 'solid-js'

import close from '../../../../assets/icons/close_FILL0_wght400_GRAD0_opsz24.svg';
import styles from './index.module.css';
import { focusable } from "../../../../focusable";import { FocusableOptions, InputSource } from '../../../../nav';
 void focusable;

interface InputTextProps {
    placeholder?: string;
    value?: string;
    onTextChanged?: (newVal: string) => void;
    onSubmit?: (value: string) => void;
    onFocusChanged?: (focus: boolean) => void;
    onClick?: () => void;
    style?: JSX.CSSProperties;
    inputContainerStyle?: JSX.CSSProperties;
    inputStyle?: JSX.CSSProperties;
    small?: boolean;
    icon?: string;
    alt?: string;
    disabled?: boolean;
    label?: string;
    showClearButton?: boolean;
    error?: string | null | undefined;
    focusable?: boolean;
    onBack?: (el: HTMLElement, inputSource: InputSource) => boolean;
}

const InputText: Component<InputTextProps> = (props) => {
    let rootElement: HTMLInputElement | undefined;
    let inputElement: HTMLInputElement | undefined;

    const merged = mergeProps({ small: true }, props);
    const [text, setText] = createSignal(merged.value ?? "");
    createEffect(() => {
        setText(merged.value ?? "");
    });

    const [hasFocus, setHasFocus] = createSignal(false);
    const [touched, setTouched] = createSignal(false);
    const [isComposing, setIsComposing] = createSignal(false);
    let wasEnterDown = false;

    createEffect(() => {
        merged.onTextChanged?.(text());
    });

    const handleKeyDown = (e: KeyboardEvent) => {
        if (
            e.key === "Enter" &&
            !isComposing() &&
            !(e as any).isComposing &&
            !e.repeat &&
            !e.altKey && !e.ctrlKey && !e.metaKey && !e.shiftKey
        ) {
            e.preventDefault();
            props.onSubmit?.(text());
        }

        if (e.key === "Escape" && !isComposing() && props.showClearButton) {
            e.preventDefault();
            clear();
        }
    };

    const clear = () => {
        if ((text()?.length ?? 0) > 0) {
            setText("");
            props.onSubmit?.("");
        }
    };

    return (
        <div ref={rootElement} class={styles.rootElement} style={{
            "width": "100%",
            "display": "flex",
            "flex-direction": "column",
            ... merged.style
        }} use:focusable={props.focusable ? {
            onPress: () => {
                if (document.activeElement === inputElement) {
                    merged.onSubmit?.(text());
                } else {
                    inputElement?.focus();
                }
            },
            onBack: (el: HTMLElement, inputSource: InputSource) => {
                if (document.activeElement == inputElement) {
                    rootElement?.focus();
                    return true;
                }
                return props.onBack?.(el, inputSource);
            },
            onAction: () => {
                clear();
            },
            onActionLabel: "Clear"
        } : undefined}>
            <div class={styles.containerInputText} classList={{[styles.focus]: hasFocus(), [styles.disabled]: merged.disabled, [styles.hasLabel]: props.label ? true : false, [styles.error]: touched() && props.error ? true : false}} onClick={() => inputElement?.focus()} style={{
                "box-sizing": "border-box",
                "overflow": "hidden",
                ... props.inputContainerStyle
            }}>
                <Show when={merged.icon}>
                    <img src={merged.icon} class={styles.icon} alt={merged.alt} />
                </Show>
                <div style={{"display": "flex", "flex-direction": "column", "flex-grow": "1", "overflow": "hidden"}}>
                    <Show when={props.label}>
                        <div class={styles.labelText} classList={{[styles.hasContentOrFocus]: hasFocus() || text().length > 0}}>
                            {props.label}
                        </div>
                    </Show>
                    <input type="text"
                        ref={inputElement} 
                        disabled={merged.disabled}
                        class={styles.searchInput} 
                        placeholder={merged.placeholder} 
                        value={text()}
                        onClick={props.onClick}
                        onInput={e => {
                            setTouched(true);
                            setText(e.target.value);
                        }} 
                        onKeyDown={handleKeyDown}
                        onFocus={() => { 
                            if (!hasFocus()) {
                                setHasFocus(true);
                                merged.onFocusChanged?.(true);
                            }
                        }}
                        onBlur={() => { 
                            wasEnterDown = false;
                            if (hasFocus()) {
                                setHasFocus(false);
                                merged.onFocusChanged?.(false);
                            }
                        }}
                        onCompositionStart={() => setIsComposing(true)}
                        onCompositionEnd={() => setIsComposing(false)}
                        style={props.inputStyle} />
                </div>
                <Show when={props.showClearButton && text().length > 0}>
                    <img onClick={clear} src={close} class={styles.iconClear} alt="clear" />
                </Show>
            </div>
            <Show when={touched() && props.error}>
                <div class={styles.containerInputTextError}>{props.error}</div>
            </Show>
        </div>
    );

    //<input type="text" style={merged.style} class={styles.input} classList={{[styles.small]: merged.small}} oninput={inputHandler} placeholder={merged.placeholder} />
};

export default InputText;