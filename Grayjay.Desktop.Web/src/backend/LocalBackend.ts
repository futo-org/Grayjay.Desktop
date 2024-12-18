import { Backend } from "./Backend";

export abstract class LocalBackend {
    static async open(uri: string): Promise<void> {
        await Backend.GET("/local/Open?uri=" + encodeURIComponent(uri));
    }
}