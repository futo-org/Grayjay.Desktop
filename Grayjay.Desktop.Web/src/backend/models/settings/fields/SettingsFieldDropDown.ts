import { ISettingsField } from "../SettingsObject";


export interface ISettingsFieldDropDown extends ISettingsField {
    options: string[]
    value: number
}