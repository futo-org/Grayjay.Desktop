

export interface IExceptionModel {
    type: string,
    typeName?: string;
    title: string,
    message: string,
    code: string
    canRetry: boolean
    pluginID?: string;
    pluginName?: string;
    stacktrace?: string;

    model?: IExceptionModel;
}

export default class ExceptionModel extends Error implements IExceptionModel {
    type: string;
    typeName?: string;
    title: string;
    message: string;
    code: string;
    canRetry: boolean;

    pluginID?: string;
    pluginName?: string;

    constructor(model: IExceptionModel) {
        super();
        this.type = model.type ?? model?.model?.type;
        this.typeName = model.typeName ?? model?.model?.typeName;
        this.title =  model.title ?? model?.model?.title;
        this.message = model.message ?? model?.model?.message;
        this.code = model.code ?? model?.model?.code;
        this.canRetry = model.canRetry ?? model?.model?.canRetry;
        this.pluginID = model.pluginID ?? model?.model?.pluginID;
        this.pluginName = model.pluginName ?? model?.model?.pluginName;
        if(!this.code && model.stacktrace)
            this.code = model.stacktrace;
    }

    replaceTitle = (newTitle: string) => {
        if(!this.message)
            this.message = this.title;
        this.title = newTitle;
        return this;
    }
}