import { Component, Show } from 'solid-js'

import styles from './index.module.css';
import { ISettingsField } from '../../../../../backend/models/settings/SettingsObject';
import { ISettingsFieldGroup } from '../../../../../backend/models/settings/fields/SettingsFieldGroup';
import { ISettingsFieldReadOnly } from '../../../../../backend/models/settings/fields/SettingsFieldReadOnly';
import FieldKey from '../FieldKey';

interface FieldReadOnlyProps {
    field: ISettingsFieldReadOnly,
    value: string,
    isSubField?: boolean
}

const FieldReadOnly: Component<FieldReadOnlyProps> = (props) => {

    return (
        <div class={styles.container}>
            <FieldKey field={props.field} isSubField={!!props.isSubField} />
            <div class={styles.value}>
                {props.value}
            </div>
        </div>
    );
};

export default FieldReadOnly;