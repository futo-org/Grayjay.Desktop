import { Component, Show, createSignal } from 'solid-js'

import styles from './index.module.css';
import { ISettingsField } from '../../../../../backend/models/settings/SettingsObject';
import { ISettingsFieldToggle } from '../../../../../backend/models/settings/fields/SettingsFieldToggle';
import Toggle from '../../../../basics/inputs/Toggle';
import FieldKey from '../FieldKey';
import { focusable } from '../../../../../focusable'; void focusable;

interface FieldToggleProps {
    field: ISettingsFieldToggle,
    onFieldChanged?: (field: ISettingsField, newVal: boolean)=>void,
    value: boolean,
    isSubField?: boolean
}

const FieldToggle: Component<FieldToggleProps> = (props) => {

    const [value$, setValue] = createSignal(props.value);

    function toggle(newVal: boolean) {
        setValue(newVal);
        if(props.onFieldChanged)
            props.onFieldChanged(props.field, newVal);
    }

    return (
        <div class={styles.container} use:focusable={{
            onPress: () => toggle(!value$())
        }}>
            <FieldKey field={props.field} isSubField={props.isSubField} />
            <div class={styles.value}>
                <Toggle value={value$()} onToggle={(newVal)=>toggle(newVal)} />
            </div>
        </div>
    );
};

export default FieldToggle;