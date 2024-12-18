import { ISettingsField } from "../SettingsObject";

export interface ISettingsFieldGroup extends ISettingsField {
    fields: ISettingsField[]
}