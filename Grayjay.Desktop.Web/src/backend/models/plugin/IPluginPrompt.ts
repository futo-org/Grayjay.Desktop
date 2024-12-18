

export default interface IPluginPrompt {
    config: ISourceConfig,
    warnings: IPluginWarning[],
    alreadyInstalled: boolean
}

export interface IPluginWarning {
    title: string,
    description: string
}