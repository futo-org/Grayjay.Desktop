import { Component, JSX, Show } from 'solid-js'

import styles from './index.module.css';
import Button from '../Button';

interface ButtonProps {
    icon?: string;
    text: string;
    color?: string;
    onClick?: (event: MouseEvent) => void;
    small?: boolean;
    style?: JSX.CSSProperties;
}

const ButtonFlex: Component<ButtonProps> = (props) => {
    const style = props.style ?? {};
    style.display = "flex";
    style["align-items"] = "center";
    style["flex-direction"] = "row";
    style["justify-content"] = "center";

    return (
        <Button icon={props.icon} text={props.text} color={props.color} onClick={props.onClick} small={props.small} style={style}></Button>
    );
};

export default ButtonFlex;