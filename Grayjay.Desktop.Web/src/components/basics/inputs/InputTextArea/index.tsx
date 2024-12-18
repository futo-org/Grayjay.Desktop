import { Component, JSX, Show, createEffect, createSignal, mergeProps } from 'solid-js'

import close from '../../../../assets/icons/close_FILL0_wght400_GRAD0_opsz24.svg';
import styles from './index.module.css';

interface InputTextAreaProps {
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
    error?: string | null | undefined;
}

const InputTextArea: Component<InputTextAreaProps> = (props) => {
    let inputElement: HTMLTextAreaElement | undefined;

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

    const onKey = (ev: any) => {
        if(ev.key === 'Enter') {
            merged.onSubmit?.(text());
        }
    };

    return (
        <div style={{
            "width": "100%",
            "height": "250px",
            "display": "flex",
            "flex-direction": "column",
            ... merged.style
        }}>
            <div class={styles.containerInputText} classList={{[styles.focus]: hasFocus(), [styles.disabled]: merged.disabled, [styles.error]: touched() && props.error ? true : false}} onClick={() => inputElement?.focus()} style={{
                "box-sizing": "border-box",
                "overflow": "hidden",
                ... props.inputContainerStyle
            }}>
                <Show when={merged.icon}>
                    <img src={merged.icon} class={styles.icon} alt={merged.alt} />
                </Show>
                <textarea ref={inputElement}
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
                    style={props.inputStyle}>
                </textarea>
            </div>
            <Show when={touched() && props.error}>
                <div class={styles.containerInputTextError}>{props.error}</div>
            </Show>
        </div>
    );
};

export default InputTextArea;