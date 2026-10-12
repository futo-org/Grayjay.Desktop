import type { MediaPlayerClass } from 'dashjs';
import type { ResponseInterceptor } from '@svta/common-media-library/request';
import { fromBase64 } from './bridge';
import { PlaybackSession } from './Session';

export function attachHelperDash(player: MediaPlayerClass, session: PlaybackSession, onError: (message: string) => void): () => void {
    const initializations = new Map<string, Uint8Array>();
    let disposed = false;
    const representationKey = (request: any) => [request.mediaType, request.representation?.adaptation?.period?.id,
        request.representation?.adaptation?.index, request.representation?.id].join(':');
    const intercept: ResponseInterceptor = async response => {
        if (disposed || !response.data || (response.status ?? 0) >= 400) return response;
        try {
            const request = response.request.customData?.request;
            if (request?.type === 'MPD' && typeof response.data === 'string') {
                const document = new DOMParser().parseFromString(response.data, 'application/xml');
                if (document.getElementsByTagName('parsererror').length) throw new Error('Invalid DASH manifest.');
                const protection = Array.from(document.getElementsByTagNameNS('*', 'ContentProtection'));
                protection.filter(element => /edef8ba9/i.test(element.getAttribute('schemeIdUri') ?? ''))
                    .flatMap(element => Array.from(element.getElementsByTagNameNS('*', 'pssh')))
                    .forEach(element => session.requestKey(fromBase64(element.textContent?.trim() ?? '')));
                for (const element of protection) element.remove();

                for (const element of Array.from(document.getElementsByTagNameNS('*', 'AdaptationSet'))) {
                    if (/webm/i.test(element.getAttribute('mimeType') ?? '')) element.remove();
                    else for (const representation of Array.from(element.getElementsByTagNameNS('*', 'Representation'))) {
                        if (/webm/i.test(representation.getAttribute('mimeType') ?? '')) representation.remove();
                    }
                }

                for (const element of Array.from(document.getElementsByTagName('*'))) {
                    if (['SegmentTemplate', 'SegmentList', 'SegmentBase'].includes(element.localName)) {
                        element.setAttribute('availabilityTimeComplete', 'true');
                        element.setAttribute('availabilityTimeOffset', '0');
                    }
                }
                response.data = new XMLSerializer().serializeToString(document);
            } else if (response.data instanceof ArrayBuffer && request) {
                const key = representationKey(request), data = new Uint8Array(response.data);

                if (request.type === 'InitializationSegment' && request.availabilityStartTime != null &&
                    (request.mediaType === 'audio' || request.mediaType === 'video')) {
                    initializations.set(key, data);
                    response.data = (await session.initialize(data)).buffer;
                } else if (request.type === 'MediaSegment' && Number.isFinite(request.startTime) &&
                    (request.mediaType === 'audio' || request.mediaType === 'video')) {
                    const init = initializations.get(key);
                    if (!init) throw new Error('The media initialization has not loaded.');
                    response.data = (await session.decrypt(init, data)).buffer;
                }
            }
            return response;
        } catch (error) {
            // A segment still in flight when the source changes is aborted by session.close(); failing it would make
            // the old player raise a download error over the next video
            if (disposed) return response;
            console.error('Protected segment failed', String(error));
            onError(String(error));

            response.status = 500; response.data = undefined; return response;
        }
    };
    player.addResponseInterceptor(intercept);
    return () => { disposed = true; player.removeResponseInterceptor(intercept); initializations.clear(); session.close(); };
}
