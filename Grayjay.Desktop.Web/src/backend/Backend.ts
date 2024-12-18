import Globals from "../globals";
import { uuidv4 } from "../utility";
import ExceptionModel from "./exceptions/ExceptionModel";

export abstract class Backend {
 
    static async GET(url: string) {
        const resp = await fetch(url, {
            headers: {
                "WindowID": Globals.WindowID
            }
        });
        if (resp.status != 200)
            await this.handleException(resp);

        try {
            return await resp.json();
        } catch (e) {
            return undefined;
        }
    }

    static async GET_text(url: string) {
        const resp = await fetch(url, {
            headers: {
                "WindowID": Globals.WindowID
            }
        });
        if (resp.status != 200)
            await this.handleException(resp);

        try {
            return await resp.text();
        } catch (e) {
            return undefined;
        }
    }

    static async DELETE(url: string) {
        const resp = await fetch(url, {
            method: "DELETE",
            headers: {
                "WindowID": Globals.WindowID
            }
        });
        
        if (resp.status != 200)
            await this.handleException(resp);

        try {
            return await resp.json();
        } catch (e) {
            return undefined;
        }
    }
 
    static async POST(url: string, body: string, contentType: string): Promise<any> {
        const resp = await fetch(url, {
            method: "POST",
            headers: {
                "Content-Type": contentType,
                "WindowID": Globals.WindowID
            },
            body: body
        });
        if (resp.status != 200)
            await this.handleException(resp);
        try {
            return await resp.json();
        } catch (e) {
            return undefined;
        }
    }
    static async POSTFormData(url: string, body: FormData): Promise<any> {
        const resp = await fetch(url, {
            method: "POST",
            headers: {
                WindowID: Globals.WindowID
            },
            body: body
        });
        if (resp.status != 200)
            await this.handleException(resp);
        try {
            return await resp.json();
        } catch (e) {
            return undefined;
        }
    }


    static async handleException(response: Response) {
        if(response.status == 550) {
            throw new ExceptionModel(await response.json());
        }
        throw new Error("failed due to: [" + response.status  + "]\n" + await response.text());
    }
}