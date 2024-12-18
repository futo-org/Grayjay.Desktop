import { Duration } from "luxon";
import { CastingDeviceInfo } from "../contexts/Casting";
import { Backend } from "./Backend";
import { SourceSelected } from "../components/contentDetails/VideoDetailView";


export abstract class ImagesBackend {

    static async images(): Promise<string[]> {
        return await Backend.GET("/Images/Images") as string[]
    }

    static async imageUpload(file: File): Promise<string> {
        const formData = new FormData();
        formData.append("file", file, file.name);
        return await Backend.POSTFormData("/Images/ImageUpload", formData);
    }
}