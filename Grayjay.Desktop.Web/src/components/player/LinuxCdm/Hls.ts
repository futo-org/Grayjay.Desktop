import Hls, { type LoaderCallbacks, type LoaderConfiguration, type LoaderContext, type FragmentLoaderContext } from 'hls.js';
import { fromBase64 } from './bridge';
import { PlaybackSession } from './Session';

export function createHelperHls(session: PlaybackSession): Hls {
    const initializations = new Map<string, Uint8Array>();
    let disposed = false;
    class Loader extends Hls.DefaultConfig.loader {
        load(context: LoaderContext, config: LoaderConfiguration, callbacks: LoaderCallbacks<LoaderContext>) {
            super.load(context, config, { ...callbacks, onSuccess: (response, stats, ctx, details) => {
                const transform = async () => {
                    if (disposed || stats.aborted) return;
                    if (typeof response.data === 'string' && response.data.startsWith('#EXTM3U')) {
                        response.data = response.data.split('\n').filter(line => {
                            if (!/^#EXT-X-(?:SESSION-)?KEY:/.test(line)) return true;
                            // Keys for other DRM systems (PlayReady, FairPlay) would make hls.js fail to load a key it cannot use
                            if (!/edef8ba9-79d6-4ace-a3c8-27dcd51d21ed/i.test(line)) return !/KEYFORMAT="(?!identity")/i.test(line);
                            const data = line.match(/URI="data:[^,]*;base64,([^\"]+)"/i);
                            if (!data) throw new Error('Unsupported Widevine initialization data.');
                            session.requestKey(fromBase64(data[1])); return false;
                        }).join('\n');
                    } else if (response.data instanceof ArrayBuffer && 'frag' in ctx) {
                        const frag = (ctx as FragmentLoaderContext).frag;
                        const bytes = new Uint8Array(response.data);
                        if (frag.sn === 'initSegment') {
                            initializations.set(frag.url, bytes);
                            response.data = (await session.initialize(bytes)).buffer;
                        } else if (frag.initSegment) {
                            const init = initializations.get(frag.initSegment.url);
                            if (!init) throw new Error('The media initialization has not loaded.');
                            response.data = (await session.decrypt(init, bytes)).buffer;
                        }
                    }
                    if (!disposed && !stats.aborted) callbacks.onSuccess(response, stats, ctx, details);
                };
                void transform().catch(error => {
                    if (!disposed && !stats.aborted) callbacks.onError({ code: 0, text: String(error) }, ctx, details, stats);
                });
            } });
        }
    }
    const player = new Hls({ startPosition: -1, loader: Loader, progressive: false });
    player.on(Hls.Events.DESTROYING, () => { disposed = true; initializations.clear(); session.close(); });
    return player;
}
