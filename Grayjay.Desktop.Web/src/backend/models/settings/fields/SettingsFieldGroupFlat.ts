import { ISettingsField } from "../SettingsObject";

export interface ISettingsFieldGroupFlat extends ISettingsField {
    fields: ISettingsField[]
}