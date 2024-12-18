import { Component, Index, Show } from 'solid-js'

import styles from './index.module.css';
import { ISettingsField } from '../../../../../backend/models/settings/SettingsObject';
import { ISettingsFieldGroup } from '../../../../../backend/models/settings/fields/SettingsFieldGroup';
import { ISettingsFieldReadOnly } from '../../../../../backend/models/settings/fields/SettingsFieldReadOnly';
import { ISettingsFieldDropDown } from '../../../../../backend/models/settings/fields/SettingsFieldDropDown';
import FieldKey from '../FieldKey';
import Dropdown from '../../../../basics/inputs/Dropdown';
import { AnchorStyle } from '../../../../../utility/Anchor';

interface FieldDropDownProps {
    field: ISettingsFieldDropDown
    onFieldChanged?: (field: ISettingsField, newVal: number)=>void,
    value: number,
    isSubField?: boolean
}

const FieldDropDown: Component<FieldDropDownProps> = (props) => {

    function selectedChanged(newVal: any) {
        console.log(newVal);
        if(props.onFieldChanged && !isNaN(parseInt(newVal)))
            props.onFieldChanged(props.field, parseInt(newVal));
    }

    return (
        <div class={styles.container}>
            <FieldKey field={props.field} isSubField={!!props.isSubField} />
            <div class={styles.value}>
                <Dropdown options={props.field.options} value={props.value} onSelectedChanged={selectedChanged} anchorStyle={AnchorStyle.BottomRight} />
            </div>
        </div>
    );
};

export default FieldDropDown;