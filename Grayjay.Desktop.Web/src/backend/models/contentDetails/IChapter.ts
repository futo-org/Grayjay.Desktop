

export interface IChapter {
    name: string,
    type: ChapterType,
    timeStart: number,
    timeEnd: number
}
export enum ChapterType {
    NORMAL = 0,

    SKIPPABLE = 5,
    SKIP = 6,
    SKIPONCE = 7
};