

interface PagerResult<T> {
    pagerID?: string;
    results: T[];
    hasMore: boolean,
    error?: any
}