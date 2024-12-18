import { Component, Match, Show, Switch, createEffect, createSignal } from 'solid-js'

import styles from './index.module.css';
import { ISettingsField, ISettingsObject } from '../../../../../backend/models/settings/SettingsObject';
import { ISettingsFieldGroup } from '../../../../../backend/models/settings/fields/SettingsFieldGroup';
import { ISettingsFieldReadOnly } from '../../../../../backend/models/settings/fields/SettingsFieldReadOnly';
import FieldGroup from '../FieldGroup';
import FieldToggle from '../FieldToggle';
import FieldReadOnly from '../FieldReadOnly';
import FieldDropDown from '../FieldDropDown';
import { ISettingsFieldToggle } from '../../../../../backend/models/settings/fields/SettingsFieldToggle';
import { ISettingsFieldDropDown } from '../../../../../backend/models/settings/fields/SettingsFieldDropDown';
import { parseBool } from '../../../../../utility';
import { SettingsContainerParent } from '../..';
import UIOverlay from '../../../../../state/UIOverlay';
import warning from '../../../../../assets/icons/icon_warning.svg';

interface FieldProps {
    container?: SettingsContainerParent,
    field: ISettingsField,
    onFieldChanged?: (field: ISettingsField, newVal: any)=>void,
    parentObject: any,
    isSubField?: boolean
}

const Field: Component<FieldProps> = (props) => {

    function onChanged(field: ISettingsField, newVal: any){
        if(props.onFieldChanged)
            props.onFieldChanged(field, newVal);
    }
    function onChangedDirect(field: ISettingsField, newVal: any){
        const oldVal = props.parentObject[props.field.property];
        const oldValBool = parseBool(oldVal);
        const newValBool = parseBool(newVal);
        if(field.warningDialog && !oldValBool && newValBool) {
            UIOverlay.overlay({
                dialog: {
                    icon: warning,
                    title: field.warningDialog,
                    description: "",
                    buttons: [
                        {
                            title: "Cancel",
                            style: "none",
                            onClick: ()=>{

                            }
                        },
                        {
                            title: "Ok",
                            style: "primary",
                            onClick: ()=>{
                                props.parentObject[props.field.property] = newVal;
                                onChanged(field, newVal);
                            }
                        }
                    ]
                }
            });
        }
        else {
            props.parentObject[props.field.property] = newVal;
            onChanged(field, newVal);
        }
    }

    const [isVisible$, setIsVisible] = createSignal(!props.field.dependency || parseBool(props.parentObject[props.field.dependency]));

    createEffect(()=>{
        if(props.field.dependency && props.container) {
            props.container?.onFieldChange.registerOne(props.field, (changeEv)=>{
                const isVisible = isVisible$();
                const newIsVisible = !props.field.dependency || parseBool(props.parentObject[props.field.dependency]);
                if(isVisible != newIsVisible) {
                    console.log("Dependency change detected for [" + props.field.title + "] with dependency [" + props.field.dependency + "]")
                    setIsVisible(newIsVisible);
                }
            });
        }
    });

    return (
        <div class={styles.container}>
            <Show when={isVisible$()}>
                <Switch>
                    <Match when={props.field.type == "group"}>
                        <FieldGroup field={props.field as ISettingsFieldGroup} value={props.parentObject[props.field.property]}
                            container={props.container}
                            onFieldChanged={onChanged} />
                    </Match>
                    <Match when={props.field.type == "group_flat"}>
                        <FieldGroup field={props.field as ISettingsFieldGroup} value={props.parentObject}
                            container={props.container}
                            onFieldChanged={onChanged} />
                    </Match>
                    <Match when={props.field.type == "toggle"}>
                        <FieldToggle field={props.field as ISettingsFieldToggle} value={parseBool(props.parentObject[props.field.property])} 
                            onFieldChanged={onChangedDirect} isSubField={props.isSubField} />
                    </Match>
                    <Match when={props.field.type == "readonly"}>
                        <FieldReadOnly field={props.field as ISettingsFieldReadOnly} value={props.parentObject[props.field.property]}
                             isSubField={props.isSubField} />
                    </Match>
                    <Match when={props.field.type == "dropdown"}>
                        <FieldDropDown field={props.field as ISettingsFieldDropDown} value={props.parentObject[props.field.property]}
                            onFieldChanged={onChangedDirect} isSubField={props.isSubField} />
                    </Match>
                </Switch>
            </Show>
        </div>
    );
};

export default Field;