import { Component, JSX, Show } from "solid-js";
import Loader from "../Loader";

export interface LoaderProps {
  size?: string;
  text?: string;
  style?: JSX.CSSProperties;
}

const CenteredLoader: Component<LoaderProps> = (props) => {
    return (
        <div style={{
            "width": "100%",
            "height": "100%",
            "flex-direction": "column",
            "display": "flex",
            "align-items": "center",
            "justify-content": "center",
            ... props.style
        }}>
            <Loader size={props.size} />
            <Show when={props.text && props.text.length}>
                <div style="margin-top: 20px;">
                    {props.text}
                </div>
            </Show>
        </div>
    );
};

export default CenteredLoader;