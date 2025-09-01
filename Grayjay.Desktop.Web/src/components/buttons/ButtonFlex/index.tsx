import { Component, JSX, Show } from 'solid-js'

import styles from './index.module.css';
import Button from '../Button';
import { FocusableOptions } from '../../../nav';

interface ButtonProps {
    icon?: string;
    text: string;
    color?: string;
    onClick?: (event: MouseEvent) => void;
    small?: boolean;
    style?: JSX.CSSProperties;
    focusableOpts?: FocusableOptions;
}

const ButtonFlex: Component<ButtonProps> = (props) => {
    const style = props.style ?? {};
    style.display = "flex";
    style["align-items"] = "center";
    style["flex-direction"] = "row";
    style["justify-content"] = "center";

    return (
        <Button icon={props.icon} text={props.text} color={props.color} onClick={props.onClick} small={props.small} style={style} focusableOpts={props.focusableOpts}></Button>
    );
};

export default ButtonFlex;