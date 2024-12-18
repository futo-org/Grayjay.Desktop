import { DateTime } from "luxon";
import { Backend } from "./Backend";
import { IPlatformContent } from "./models/content/IPlatformContent";
import { IPlatformVideo } from "./models/content/IPlatformVideo";
import { Pager } from "./models/pagers/Pager";
import { dateFromAny } from "../utility";


export abstract class SubscriptionsBackend {

    static async subscriptions(): Promise<ISubscription[]> {
        return await Backend.GET("/subscriptions/Subscriptions") as ISubscription[]
    }
    static async lastSubscriptionTime(): Promise<DateTime | undefined> {
        const result = await Backend.GET("/subscriptions/LastSubscriptionTime") as string;
        return dateFromAny(result);
    }
    static async subscription(url: string): Promise<ISubscription> {
        return await Backend.GET("/subscriptions/Subscription?url=" + encodeURIComponent(url)) as ISubscription
    }
    static async subscriptionGroups(): Promise<ISubscriptionGroup[]> {
        return await Backend.GET("/subscriptions/SubscriptionGroups") as ISubscriptionGroup[]
    }

    static async subscriptionsLoad(updated: boolean = false): Promise<PagerResult<IPlatformVideo>> {
        return await Backend.GET("/subscriptions/SubscriptionsLoad?updated=" + updated) as PagerResult<IPlatformVideo>;
    }
    static async subscriptionsLoadNew(): Promise<PagerResult<IPlatformVideo>> {
        return await Backend.GET("/subscriptions/SubscriptionsLoadNew") as PagerResult<IPlatformVideo>;
    }
    static async subscriptionsNextPage(): Promise<PagerResult<IPlatformVideo>> {
        return await Backend.GET("/subscriptions/SubscriptionsNextPage") as PagerResult<IPlatformVideo>;
    }
    
    static async subscriptionsCacheLoad(): Promise<PagerResult<IPlatformVideo>> {
        return await Backend.GET("/subscriptions/SubscriptionsCacheLoad") as PagerResult<IPlatformVideo>;
    }
    static async subscriptionsCacheNextPage(): Promise<PagerResult<IPlatformVideo>> {
        return await Backend.GET("/subscriptions/SubscriptionsCacheNextPage") as PagerResult<IPlatformVideo>;
    }
    
    static async subscriptionsFilterChannelLoad(id: string): Promise<PagerResult<IPlatformVideo>> {
        return await Backend.GET("/subscriptions/SubscriptionsFilterChannelLoad?url=" + encodeURIComponent(id)) as PagerResult<IPlatformVideo>;
    }
    static async subscriptionsFilterSubscriptionLoad(id: string): Promise<PagerResult<IPlatformVideo>> {
        return await Backend.GET("/subscriptions/SubscriptionsFilterSubscriptionLoad?id=" + id) as PagerResult<IPlatformVideo>;
    }
    static async subscriptionsFilterNextPage(): Promise<PagerResult<IPlatformVideo>> {
        return await Backend.GET("/subscriptions/SubscriptionsFilterNextPage") as PagerResult<IPlatformVideo>;
    }


    static async subscriptionGroupLoad(id: string, updated: boolean = false): Promise<PagerResult<IPlatformVideo>> {
        return await Backend.GET("/subscriptions/SubscriptionGroupLoad?id=" + id + "&updated=" + updated) as PagerResult<IPlatformVideo>;
    }
    static async subscriptionGroupNextPage(id: string): Promise<PagerResult<IPlatformVideo>> {
        return await Backend.GET("/subscriptions/SubscriptionGroupNextPage?id=" + id) as PagerResult<IPlatformVideo>;
    }

    static async subscriptionGroup(id: string): Promise<ISubscriptionGroup> {
        return await Backend.GET("/subscriptions/SubscriptionGroup?id=" + id) as ISubscriptionGroup;
    }
    static async subscriptionGroupSave(group: ISubscriptionGroup): Promise<boolean> {
        return await Backend.POST("/subscriptions/SubscriptionGroupSave", JSON.stringify(group), "application/json") as boolean;
    }
    static async subscriptionGroupDelete(id: string): Promise<ISubscriptionGroup> {
        return await Backend.GET("/subscriptions/SubscriptionGroupDelete?id=" + id) as ISubscriptionGroup;
    }



    static async subscriptionSettings(channelUrl: string): Promise<ISubscriptionSettings> {
        return await Backend.GET(`/subscriptions/SubscriptionSettings?channelUrl=${encodeURIComponent(channelUrl)}`);
    }

    static async updateSubscriptionSettings(channelUrl: string, subscriptionSettings: ISubscriptionSettings): Promise<any> {
        return await Backend.POST(`/subscriptions/UpdateSubscriptionSettings?channelUrl=${encodeURIComponent(channelUrl)}`, JSON.stringify(subscriptionSettings), "application/json");
    }

    static async subscriptionPager(updated: boolean = false): Promise<Pager<IPlatformContent>> {
        return Pager.fromMethods<IPlatformContent>(()=>this.subscriptionsLoad(updated), this.subscriptionsNextPage);
    }

    static async subscriptionGroupPager(id: string, updated: boolean = false): Promise<Pager<IPlatformContent>> {
        return Pager.fromMethods<IPlatformContent>(()=>this.subscriptionGroupLoad(id, updated), ()=>this.subscriptionGroupNextPage(id));
    }

    static async subscriptionFilterSubscriptionPager(id: string): Promise<Pager<IPlatformContent>> {
        return Pager.fromMethods<IPlatformContent>(()=>this.subscriptionsFilterSubscriptionLoad(id), ()=>this.subscriptionsFilterNextPage());
    }
    static async subscriptionFilterChannelPager(url: string): Promise<Pager<IPlatformContent>> {
        return Pager.fromMethods<IPlatformContent>(()=>this.subscriptionsFilterChannelLoad(url), ()=>this.subscriptionsFilterNextPage());
    }

    static async subscriptionCachePager(): Promise<Pager<IPlatformContent>> {
        return Pager.fromMethods<IPlatformContent>(()=>this.subscriptionsCacheLoad(), this.subscriptionsCacheNextPage);
    }

    static async isSubscribed(url: string): Promise<boolean> {
        return await Backend.GET("/subscriptions/IsSubscribed?url=" + encodeURIComponent(url));
    }
    static async subscribe(url: string): Promise<boolean> {
        return await Backend.GET("/subscriptions/Subscribe?url=" + encodeURIComponent(url));
    }
    static async unsubscribe(url: string): Promise<boolean> {
        return await Backend.GET("/subscriptions/Unsubscribe?url=" + encodeURIComponent(url));
    }

}