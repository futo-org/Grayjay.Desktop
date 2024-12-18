import { Component, For, Show } from 'solid-js'

import styles from './index.module.css';
import { ISettingsField } from '../../../../../backend/models/settings/SettingsObject';
import { ISettingsFieldGroup } from '../../../../../backend/models/settings/fields/SettingsFieldGroup';
import SettingsContainer, { SettingsContainerParent } from '../..';
import Field from '../Field';

interface FieldGroupProps {
    container?: SettingsContainerParent,
    field: ISettingsFieldGroup,
    onFieldChanged?: (field: ISettingsField, newVal: any)=>void,
    value: any
}

const FieldGroup: Component<FieldGroupProps> = (props) => {

    return (
        <div class={styles.container}>
            <div class={styles.header}>
                {props.field.title}
            </div>
            <Show when={props.field.description}>
                <div class={styles.description}>
                    {props.field.description}
                </div>
            </Show>
            <div>
                <For each={props.field.fields}>{ field =>
                    <Field field={field}
                        container={props.container}
                        parentObject={props.value} 
                        onFieldChanged={(field: ISettingsField, newVal: any)=>props.onFieldChanged && props.onFieldChanged(field, newVal)}
                        isSubField={true} />
                }</For>
            </div>
        </div>
    );
};

export default FieldGroup;