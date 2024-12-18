export enum RatingTypes {
    Likes = 1,
    LikesDislikes = 2,
    Scaler = 3
}

export interface IRating {
    type: RatingTypes;
}

export interface IRatingLikes extends IRating {
    type: RatingTypes.Likes;
    likes: number;
}

export interface IRatingLikesDislikes extends IRating {
    type: RatingTypes.LikesDislikes;
    likes: number;
    dislikes: number;
}

export interface IRatingScaler extends IRating {
    type: RatingTypes.Scaler;
    value: number;
}