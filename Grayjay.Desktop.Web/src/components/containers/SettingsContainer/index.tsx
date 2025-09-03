import { Component, For, createMemo, Show, createEffect, JSX } from "solid-js";
import styles from './index.module.css';

import { Event1 } from "../../../utility/Event";
import { ISettingsField, ISettingsObject } from "../../../backend/models/settings/SettingsObject";
import Field from "./fields/Field";

export interface SettingsContainerProps {
    settings: ISettingsObject | undefined,
    filterGroup?: string,
    filterName?: string,
    onFieldChanged?: (arg0: ISettingsField, arg1: any) => void;
    style?: JSX.CSSProperties;
};

export class SettingsContainerParent {
    settings: ISettingsObject;

    onFieldChange = new Event1<IFieldChangedEvent>()

    constructor(settingsObject: ISettingsObject) {
        this.settings = settingsObject;
        this.settings.onFieldChanged = (field, newVal) =>{
            this.onFieldChange.invoke({ field: field, newValue: newVal });
        }
    }
}
export interface IFieldChangedEvent {
    field: ISettingsField,
    newValue: any
}

const SettingsContainer: Component<SettingsContainerProps> = (props) => {
    let object = createMemo(()=>(props.settings) ? new SettingsContainerParent(props.settings) : null);
    let existing: ISettingsObject | undefined = undefined;
    let didChange = false;

    createEffect(()=>{
        if(existing != props.settings) {
            if(didChange) {
                didChange = false;
                //save(existing!);
            }
        }
        existing = props.settings;
    });

    function onFieldChanged(field: ISettingsField, newVal: any) {
        console.log("Field [" + field.property + "] changed", newVal);
        console.log("Settings [" + props.settings?.id + "] changed", props.settings?.object);
        didChange = true;
        if(props.settings?.onFieldChanged)
            props.settings.onFieldChanged(field, newVal);
        if(props.onFieldChanged)
            props.onFieldChanged(field, newVal);
    }

    function save(settings: ISettingsObject) {
        if(settings?.onSave)
            settings.onSave();
    }

    return (
        <div class={styles.container} style={props.style}>
            <Show when={props.settings}>
                <div style="margin: 24px">
                    <For each={props.settings!!.fields}>{ field =>
                        <Show when={(!props.filterGroup || (field.type == 'group' && field.property == props.filterGroup)) && (!props.filterName || field.title.indexOf(props.filterName!) >= 0)}>
                            <Field container={object()} field={field} parentObject={props.settings?.object} onFieldChanged={onFieldChanged} />
                        </Show>
                    }</For>
                </div>
            </Show>
        </div>
    );
};

export default SettingsContainer;
