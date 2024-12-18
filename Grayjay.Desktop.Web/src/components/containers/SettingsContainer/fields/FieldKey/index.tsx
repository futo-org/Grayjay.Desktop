import { Component, Index, Show } from 'solid-js'

import styles from './index.module.css';
import { ISettingsField } from '../../../../../backend/models/settings/SettingsObject';
import { ISettingsFieldGroup } from '../../../../../backend/models/settings/fields/SettingsFieldGroup';
import { ISettingsFieldReadOnly } from '../../../../../backend/models/settings/fields/SettingsFieldReadOnly';
import { ISettingsFieldDropDown } from '../../../../../backend/models/settings/fields/SettingsFieldDropDown';

interface FieldKeyProps {
    field: ISettingsField,
    isSubField?: boolean
}

const FieldKey: Component<FieldKeyProps> = (props) => {
    return (
            <div classList={{[styles.descriptor]: true, [styles.isSub]: !!props.isSubField}}>
                <div class={styles.title}>
                    {props.field.title}
                </div>
                <div class={styles.description}>
                    {props.field.description}
                </div>
            </div>
    );
};

export default FieldKey;