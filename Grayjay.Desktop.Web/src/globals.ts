import { Navigator, useNavigate } from "@solidjs/router";
import { HandlingBackend } from "./backend/HandlingBackend";
import { uuidv4 } from "./utility";
import { VideoContextState, VideoContextValue, useVideo } from "./contexts/VideoProvider";
import { IPlatformVideo } from "./backend/models/content/IPlatformVideo";
import { Duration } from "luxon";



export default class Globals {
  public static WindowID = uuidv4();


  public static async handleUrl(url: string, video: VideoContextValue, navigate: Navigator, positionSec: number) {
    const executionPlan = await HandlingBackend.handlePlan(url);
    switch(executionPlan.type) {
      case "content":
        video.actions.openVideo({
          url: executionPlan.data
        } as any, positionSec ? Duration.fromMillis(positionSec * 1000) : Duration.fromMillis(0));
        break;
      case "channel":
        navigate("/web/channel?url=" + encodeURIComponent(url));
        video?.actions?.minimizeVideo();
        break;
      case "playlist":
        navigate("/web/remotePlaylist?url=" + encodeURIComponent(url));
        video?.actions?.minimizeVideo();
        break;
    }
  }
}