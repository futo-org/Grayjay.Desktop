import { Component, JSX, Show, createEffect, createSignal, mergeProps } from 'solid-js'

import close from '../../../../assets/icons/close_FILL0_wght400_GRAD0_opsz24.svg';
import styles from './index.module.css';

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
}

const InputText: Component<InputTextProps> = (props) => {
    let inputElement: HTMLInputElement | undefined;

    const merged = mergeProps({ small: true }, props);
    const [text, setText] = createSignal(merged.value ?? "");
    createEffect(() => {
        setText(merged.value ?? "");
    });

    const [hasFocus, setHasFocus] = createSignal(false);
    const [touched, setTouched] = createSignal(false);


    createEffect(() => {
        merged.onTextChanged?.(text());
    });

    const onKey = (ev: KeyboardEvent) => {
        if(ev.key === 'Enter') {
            merged.onSubmit?.(text());
        }
    };

    return (
        <div style={{
            "width": "100%",
            "display": "flex",
            "flex-direction": "column",
            ... merged.style
        }}>
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
                        onKeyDown={e => onKey(e)}
                        onKeyUp={e => onKey(e)}
                        onFocus={() => { 
                            if (!hasFocus()) {
                                setHasFocus(true);
                                merged.onFocusChanged?.(true);
                            }
                        }}
                        onBlur={() => { 
                            if (hasFocus()) {
                                setHasFocus(false);
                                merged.onFocusChanged?.(false);
                            }
                        }}
                        style={props.inputStyle} />
                </div>
                <Show when={props.showClearButton && text().length > 0}>
                    <img onClick={() => {
                        setText("");
                        props.onSubmit?.("");
                    }} src={close} class={styles.iconClear} alt="clear" />
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