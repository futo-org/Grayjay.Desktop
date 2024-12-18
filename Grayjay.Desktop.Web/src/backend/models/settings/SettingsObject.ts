


export interface ISettingsField {
    type: string,
    title: string,
    property: string,
    description: string,
    dependency?: string,
    warningDialog?: string
}

export interface ISettingsObject {
    fields: ISettingsField[],
    id?: string,
    object: any,
    onFieldChanged?: (field: ISettingsField, newVal: any)=>void;
    onSave?: ()=>Promise<boolean>
}