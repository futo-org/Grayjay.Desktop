import DOMPurify from "dompurify";
import { decode } from "html-entities";
import { DateTime, Duration } from "luxon";
import UIOverlay from "./state/UIOverlay";
import ExceptionModel from "./backend/exceptions/ExceptionModel";
import { IPlaylist } from "./backend/models/IPlaylist";
import { Accessor, createResource, InitializedResourceOptions, InitializedResourceReturn, Resource, ResourceFetcher, ResourceOptions, ResourceReturn, ResourceSource } from "solid-js";
import { IPlatformVideo } from "./backend/models/content/IPlatformVideo";
import { IPlatformAuthorLink } from "./backend/models/IPlatformAuthorLink";

const countInKilo: number = 1000;
const countInMillion: number = countInKilo * 1000;
const countInBillion: number = countInMillion * 1000;

const countInKbit = 1000;
const countInMbit = 1000 * countInKbit;
const countInGbit = 1000 * countInMbit;

export function promptFile(onFile: (file: File)=>void) {
  const input = document.createElement("input");
  input.type = "file";
  input.accept = "image/*";
  input.onchange = (ev)=>{
    if(input.files && input.files.length > 0) {
      const file = input.files[0];
      console.log("File:", file);
      onFile(file);
    }
  };
  input.click();
}


export function uuidv4() {
  return "10000000-1000-4000-8000-100000000000".replace(/[018]/g, c =>
    (+c ^ crypto.getRandomValues(new Uint8Array(1))[0] & 15 >> +c / 4).toString(16)
  );
}

export function proxyImage(url?: string): string | undefined {
  if (!url) {
    return undefined;
  }
  if(url.startsWith("/"))
    return url;

  return `/proxy/Image?url=${encodeURIComponent(url)}`;
  //return url;
}
export function proxyImageVariable(imgVar?: any): string | undefined {
  if (!imgVar) {
    return undefined;
  }
  if(imgVar.url?.startsWith("/"))
    return imgVar.url;

  if(imgVar.subscriptionUrl)
    return `/Images/ImageSubscription?subUrl=${encodeURIComponent(imgVar.subscriptionUrl)}`;
  if(imgVar.url)
    return `/Images/CachePassthrough?url=${encodeURIComponent(imgVar.url)}`;
  return undefined;
}

export function getBestThumbnail(thumbnails?: IThumbnails): IThumbnail | undefined {
  if (!thumbnails) {
    return undefined;
  }
  
  return (thumbnails.sources.length > 0) ? thumbnails.sources[Math.max(0, thumbnails.sources.length - 1)] : undefined;
}

export function getPlaylistThumbnail(playlist?: IPlaylist): string | undefined {
  if(playlist && playlist.videos && playlist.videos.length > 0) {
    return getBestThumbnail(playlist.videos[0].thumbnails)?.url;
  }
  return undefined;
}

export function generateUUID() { // Public Domain/MIT
  var d = new Date().getTime();//Timestamp
  var d2 = ((typeof performance !== 'undefined') && performance.now && (performance.now()*1000)) || 0;//Time in microseconds since page-load or 0 if unsupported
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function(c) {
      var r = Math.random() * 16;//random number between 0 and 16
      if(d > 0){//Use timestamp until depleted
          r = (d + r)%16 | 0;
          d = Math.floor(d/16);
      } else {//Use microseconds since page-load if supported
          r = (d2 + r)%16 | 0;
          d2 = Math.floor(d2/16);
      }
      return (c === 'x' ? r : (r & 0x3 | 0x8)).toString(16);
  });
}

export function parseBool(value: any): boolean {
  if(typeof value == "boolean")
    return !!value;
  if(typeof value == "string")
    return value.toLowerCase() == "true";
  return !!value;
}

export function toCamelCasing(key: string) {
  let capitalLength = 0;
  for(let i = 0; i < key.length; i++) {
    if(key[i] == key[i].toUpperCase())
      capitalLength++;
    else break;
  }
  return key.substring(0, capitalLength).toLowerCase() + key.substring(capitalLength);
}

export function toHumanNumber(value?: number): string | undefined {
  if (value === undefined) {
    return undefined;
  }

  const v: number = Math.abs(value);
  if (v >= countInBillion)
    return `${Math.floor(value / countInBillion)}B`;
  if (v >= countInMillion)
    return `${(value / countInMillion).toFixed(2).replace(/\.?0+$/, "")}M`;
  if (v >= countInKilo)
    return `${(value / countInKilo).toFixed(2).replace(/\.?0+$/, "")}K`;

  return `${value}`;
}

export function toHumanSignificantDuration(sec?: number): string {
  if (!sec) {
    return "00:00";
  }

  let seconds = sec;
  let hours = Math.floor(seconds / (60 * 60));
  seconds = seconds % (60 * 60);
  let minutes = Math.floor(seconds / 60);
  seconds = seconds % 60;
  const parts = [];
  if(hours)
    return hours + ((hours > 1) ? "hrs" : "hr");
  if(minutes)
    return minutes + ((minutes > 1) ? "mins" : "min");
  if(seconds)
    return seconds + ((seconds > 1) ? "secs" : "sec");
  return "";
}

export function toHumanTime(sec?: number): string {
  if (!sec) {
    return "00:00";
  }

  let seconds = sec;
  let hours = Math.floor(seconds / (60 * 60));
  seconds = seconds % (60 * 60);
  let minutes = Math.floor(seconds / 60);
  seconds = seconds % 60;
  const parts = [];
  if(hours)
    parts.push((hours + ""));
  if(hours && minutes)
    parts.push((minutes + "").padStart(2, "0"));
  else if(minutes)
    parts.push((minutes + ""));
  else parts.push("00");
  parts.push((seconds + "").padStart(2, "0"));

  return parts.join(":");
}


export function toHumanBitrate(value?: number): string | undefined {
  if(!value)
    return undefined;

    if(value >= countInGbit)
      return `${Math.floor(value / countInGbit)}gbps`;
    if(value >= countInMbit)
      return `${Math.floor(value / countInMbit)}mbps`;
    if(value >= countInKbit)
      return `${Math.floor(value / countInKbit)}kbps`;
    return value + "bps";
}
export function toHumanBytesSpeed(value?: number): string | undefined {
  if(!value)
    return undefined;

    if(value >= countInGbit)
      return `${(value / countInGbit).toFixed(2)}GB/s`;
    if(value >= countInMbit)
      return `${(value / countInMbit).toFixed(2)}MB/s`;
    if(value >= countInKbit)
      return `${(value / countInKbit).toFixed(2)}KB/s`;
    return value + " B/s";
}
export function toHumanBytesSize(value?: number): string | undefined {
  if(!value)
    return "0B";

    if(value >= countInGbit)
      return `${(value / countInGbit).toFixed(2)}GB`;
    if(value >= countInMbit)
      return `${(value / countInMbit).toFixed(2)}MB`;
    if(value >= countInKbit)
      return `${(value / countInKbit).toFixed(2)}KB`;
    return value + "B";
}

export function dateFromAny(value?: any, def?: DateTime): DateTime | undefined {
  if(!value)
    return def ?? undefined;
  
  if(!isNaN(value)) {
    //TODO: Alt solution
    const wrongOffset = ((new Date()).getTimezoneOffset()/60);
    return DateTime.fromSeconds(value).plus({hours: wrongOffset});
  }
  if(typeof value == "string")
    return DateTime.fromISO(value);
  return def ?? undefined;
}

export function toHumanNowDiffString(value?: any, abs: boolean = false): string | undefined {
  if (!value) {
    return undefined;
  }

  const now = DateTime.now().toUTC();
  const target = dateFromAny(value);
  if(!target)
    return undefined;
  const secDiff = new Date().getTime() - new Date(target?.toString()).getTime();
  let diff = now.diff(target, "seconds").as("seconds");

  let unit = "second";

  if (abs) {
    diff = Math.abs(diff);
  }

  if (Math.abs(diff) >= Duration.fromObject({ years: 1 }).as("seconds")) {
    diff = now.diff(target, "years").as("years");
    unit = "year";
  } else if (Math.abs(diff) >= Duration.fromObject({ months: 1 }).as("seconds")) {
    diff = now.diff(target, "months").as("months");
    unit = "month";
  } else if (Math.abs(diff) >= Duration.fromObject({ weeks: 1 }).as("seconds")) {
    diff = now.diff(target, "weeks").as("weeks");
    unit = "week";
  } else if (Math.abs(diff) >= Duration.fromObject({ days: 1 }).as("seconds")) {
    diff = now.diff(target, "days").as("days");
    unit = "day";
  } else if (Math.abs(diff) >= Duration.fromObject({ hours: 1 }).as("seconds")) {
    diff = now.diff(target, "hours").as("hours");
    unit = "hour";
  } else if (Math.abs(diff) >= Duration.fromObject({ minutes: 1 }).as("seconds")) {
    diff = now.diff(target, "minutes").as("minutes");
    unit = "min";
  }

  if(diff < 0)
    return `in ${Math.max(1, Math.floor(diff * -1))} ${unit}`;
  else
    return (Math.floor(diff) > 1) ? 
    `${Math.max(1, Math.floor(diff))} ${unit}s ago` : `${Math.max(1, Math.floor(diff))} ${unit} ago`;
}

export function toHumanNowDiffStringMinDay(value?: any, abs: boolean = false): string {
  const now = DateTime.now();
  const target = dateFromAny(value);
  if(!target)
    return "";
  let diff = now.diff(target, 'seconds').as('seconds');

  if (abs) {
    diff = Math.abs(diff);
  }

  if (diff >= 2 * Duration.fromObject({ days: 1 }).as('seconds')) {
    return `${toHumanNowDiffString(value, abs)}`;
  }

  if (diff >= Duration.fromObject({ days: 1 }).as('seconds')) {
    return 'Yesterday';
  }

  return 'Today';
}

export function formatDuration(duration: Duration) {
  const totalSeconds = duration.as('seconds');
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = Math.floor(totalSeconds % 60);

  const paddedMinutes = String(minutes).padStart(2, '0');
  const paddedSeconds = String(seconds).padStart(2, '0');

  if (hours > 0) {
    return `${hours}:${paddedMinutes}:${paddedSeconds}`;
  } else {
    return `${paddedMinutes}:${paddedSeconds}`;
  }
}

export function debounce<T extends (...args: any[]) => any>(func: T, wait: number): (...args: Parameters<T>) => void {
  let timeout: number | null = null;

  return (...args: Parameters<T>) => {
    const later = () => {
      timeout = null;
      func(...args);
    };

    if (timeout) {
      clearTimeout(timeout);
    }

    timeout = setTimeout(later, wait);
  };
}

export function getVideoProgressPercentage(position: number, duration: number){
  
  if(position > 10000)
    return Math.round((position / duration) / 10);
  else
    return Math.round((position / duration) * 100);
}


export function observePosition(element: HTMLElement, handler: (element: HTMLElement)=> void): ()=>void {
  const scrollListener = (ev: Event) =>{
    if((ev.target as HTMLElement).contains(element))
      handler(element);
  };
  const resizeListener = (ev: Event) =>{
    handler(element);
  };
  document.addEventListener("scroll", scrollListener, { capture: true });
  window.addEventListener("resize", resizeListener)

  const intersectionObserver = new IntersectionObserver((entries: IntersectionObserverEntry[])=>{
    handler(element);
  });
  intersectionObserver.observe(element);

  //TODO: Intersection listener?

  return function() {
    document.removeEventListener("scroll", scrollListener, { capture: true});
    intersectionObserver.disconnect();
  };
}

export function swap(a: any[], index1: number, index2: number) {
  const tmp = a[index1];
  a[index1] = a[index2];
  a[index2] = tmp;
  return a;
}

export function shuffleArray(array: any[]) {
  for (let i = array.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [array[i], array[j]] = [array[j], array[i]];
  }
  return array;
}

export function range(startInclusive: number, endInclusive: number): number[] {
  const arr = [];
  for (let i = startInclusive; i <= endInclusive; i++) {
    arr.push(i);
  }
  return arr;
}

export function sanitzeHtml(text?: string) {
  if (!text) {
    return undefined;
  }

  const urlPattern = /(\b(https?):\/\/[-A-Z0-9+&@#\/%?=~_|!:,.;]*[-A-Z0-9+&@#\/%=~_|])/ig;
  const timestampPattern = /\b(\d{1,2}:\d{2}(?::\d{2})?)\b/g;

  const isUrlWrapped = (text: string, url: string) => {
      const anchorPattern = new RegExp(`<a[^>]*>${url.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}</a>`, 'i');
      return anchorPattern.test(text);
  };

  let linkedText = decode(text).replace(urlPattern, (url) => {
      return isUrlWrapped(text, url) ? url : `<a href="${url}" target="_blank" rel="noopener noreferrer">${url}</a>`;
  });

  linkedText = linkedText.replace(timestampPattern, (timestamp) => {
      return `<a href="#" class="timestamp-link" data-timestamp="${timestamp}">${timestamp}</a>`;
  });

  return DOMPurify.sanitize(linkedText);
}

export function preventDragDrop(ev: DragEvent) {
  console.log("Preventing DragDrop", ev);
  ev.dataTransfer?.setData("prevent-drag", '');
}

/*export function createResourceDefault<T, S, R = unknown>(
  source: ResourceSource<S>,
  fetcher: ResourceFetcher<S, T, R>,
  options?: ResourceOptions<T, S>,
  defaultValue?: T
): ResourceReturn<T, R> {
  const safeFetcher: ResourceFetcher<S, T, R> = async (source, info) => {
    try {
      return await fetcher(source, info);
    } catch (error) {
      console.error("Resource fetcher error:", error);
      return defaultValue as T;
    }
  };

  return createResource(source, safeFetcher, options);
}*/

export function createResourceDefault<T, R = unknown>(
  fetcher: ResourceFetcher<true, T, R>,
  options?: ResourceOptions<T, true>,
  defaultValue?: T
): ResourceReturn<T, R>;
export function createResourceDefault<T, S, R = unknown>(
  source: ResourceSource<S>,
  fetcher: ResourceFetcher<S, T, R>,
  options?: ResourceOptions<T, S>,
  defaultValue?: T
): ResourceReturn<T, R>;
export function createResourceDefault<T, S, R>(
  pSource: ResourceSource<S> | ResourceFetcher<S, T, R>,
  pFetcher?: ResourceFetcher<S, T, R> | ResourceOptions<T, S>,
  pOptions?: ResourceOptions<T, S>,
  defaultValue?: T
): ResourceReturn<T, R> {
  let source: ResourceSource<S>;
  let fetcher: ResourceFetcher<S, T, R>;
  let options: ResourceOptions<T, S>;

  if (typeof pFetcher === "function") {
    source = pSource as ResourceSource<S>;
    fetcher = pFetcher as ResourceFetcher<S, T, R>;
    options = pOptions || {};
  } else {
    source = true as ResourceSource<S>;
    fetcher = pSource as ResourceFetcher<S, T, R>;
    options = (pFetcher || {}) as ResourceOptions<T, S>;
  }

  const safeFetcher: ResourceFetcher<S, T, R> = async (source, info) => {
    try {
      return await fetcher(source, info);
    } catch (e) {
      console.error("Fetcher error:", e);
      return defaultValue as T;
    }
  };

  return createResource(source, safeFetcher, options);
}

export function positiveOrQ(num: number | undefined){
  if(!num || num <= 0)
    return "?";
  return num;
}
export function resolutionOrUnknown(width: number | undefined, height: number | undefined){
  if(!width || !height || width <= 0 || height <= 0)
    return "";
  return width + "x" + height;
}

export function getDummyVideo() : IPlatformVideo {
  return {
    id: {
      pluginID: "",
      platform: "Dummy",
      value: crypto.randomUUID()
    } as IPlatformID,
    name: crypto.randomUUID(),
    author: {
      id: {
        pluginID: "",
        platform: "Dummy",
        value: crypto.randomUUID()
      } as IPlatformID,
      name: "Dummy",
      url: "",
      thumbnail: "",//?
      subscribers: 100//?
    } as IPlatformAuthorLink,
    dateTime: "",
    url: "",
    shareUrl: "",

    contentType: 1,
    thumbnails: {
      sources: [
        {
          url: "",
          quality: 1
        } as IThumbnail
      ]
    } as IThumbnails,
    duration: parseInt((Math.random() * 400)),
    viewCount: parseInt(Math.random() * 1000000),

    isLive: false
  } as IPlatformVideo
}

export function updateDataArray<T>(
  oldArray: T[],
  newArray: T[],
  modifiedCallback: (startIndex: number, endIndex: number) => void,
  addedCallback: (startIndex: number, endIndex: number) => void,
  removedCallback: (startIndex: number, endIndex: number) => void
) {
  const minLength = Math.min(oldArray.length, newArray.length);

  // Update common elements
  for (let i = 0; i < minLength; i++) {
    oldArray[i] = newArray[i];
  }
  if (minLength > 0) {
    modifiedCallback(0, minLength - 1);
  }

  // Handle additions
  if (newArray.length > oldArray.length) {
    const startIndex = minLength;
    const endIndex = newArray.length - 1;
    oldArray.push(...newArray.slice(minLength));
    addedCallback(startIndex, endIndex);
  }
  // Handle removals
  else if (newArray.length < oldArray.length) {
    const startIndex = minLength;
    const originalLength = oldArray.length;
    oldArray.length = newArray.length;
    if (startIndex < originalLength) {
      removedCallback(startIndex, originalLength - 1);
    }
  }
}