import { IPlatformContent } from "./IPlatformContent"

export interface IPlatformContentPlaceholder extends IPlatformContent {
    error: string,
    errorPluginID: string,
    placeholderIcon: string
}