import { ISourceConfig, ISourceConfigState } from "./ISourceConfigState";

export interface ISourceDetails {
    config: ISourceConfig,
    state: ISourceConfigState,
    hasLoggedIn: boolean,
    hasCaptcha: boolean,
    hasUpdate: boolean
}