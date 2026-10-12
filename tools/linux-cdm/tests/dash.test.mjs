import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import { transformSync } from '../../../Grayjay.Desktop.Web/node_modules/esbuild/lib/main.js';

const source = readFileSync(new URL('../../../Grayjay.Desktop.Web/src/components/player/LinuxCdm/Dash.ts', import.meta.url), 'utf8');
const code = transformSync(source, { loader: 'ts', format: 'cjs', target: 'es2022' }).code;
const context = { module: { exports: {} }, require: () => ({ fromBase64: () => {} }),
    ArrayBuffer, Uint8Array, Number, Map, Date, Error, console: { error() {} } };
vm.runInNewContext(code, context);
const { attachHelperDash } = context.module.exports;
function fixture() {
    let intercept, removed, closed = false;
    const initialized = [], decrypted = [], errors = [];
    const session = {
        initialize: async data => { initialized.push(data); return new Uint8Array([11]); },
        decrypt: async (init, data) => { decrypted.push({ init, data }); return new Uint8Array([22]); },
        close: () => closed = true
    };
    const cleanup = attachHelperDash({ addResponseInterceptor: fn => intercept = fn, removeResponseInterceptor: fn => removed = fn }, session, error => errors.push(error));
    const representation = { id: 'v1', adaptation: { index: 2, period: { id: 'p1' } } };
    const response = (type, properties = {}, data = new Uint8Array([1, 2, 3]).buffer) => ({ status: 200, data,
        request: { customData: { request: { type, mediaType: 'video', representation, ...properties } } } });
    return { intercept, response, initialized, decrypted, errors, cleanup, state: () => ({ closed, removed }) };
}
test('SegmentBase index and partial initialization probes remain untouched', async () => {
    const f = fixture();
    for (const response of [f.response('MediaSegment', { startTime: NaN }), f.response('InitializationSegment', { availabilityStartTime: null })]) {
        const before = response.data;
        assert.equal(await f.intercept(response), response);
        assert.equal(response.data, before);
    }
    assert.equal(f.initialized.length, 0); assert.equal(f.decrypted.length, 0); assert.equal(f.errors.length, 0);
});
test('real playback keeps original init and a stable key across mutable mediaInfo', async () => {
    const f = fixture(), init = f.response('InitializationSegment', { availabilityStartTime: new Date() });
    const original = init.data;
    await f.intercept(init);
    assert.deepEqual([...new Uint8Array(init.data)], [11]);
    const media = f.response('MediaSegment', { startTime: 0, mediaInfo: { id: 'changed-by-dash' } });
    await f.intercept(media);
    assert.equal(f.decrypted.length, 1); assert.equal(f.decrypted[0].init.buffer, original);
    assert.deepEqual([...new Uint8Array(media.data)], [22]); assert.equal(f.errors.length, 0);
});
test('plain subtitles stay untouched and missing media init becomes an error response', async () => {
    const f = fixture(), text = f.response('InitializationSegment', { mediaType: 'text', availabilityStartTime: new Date() });
    const before = text.data; await f.intercept(text); assert.equal(text.data, before);
    const media = f.response('MediaSegment', { startTime: 30 }); await f.intercept(media);
    assert.equal(media.status, 500); assert.equal(media.data, undefined); assert.equal(f.errors.length, 1);
});
test('closing removes the exact interceptor, closes the session and ignores late responses', async () => {
    const f = fixture(); f.cleanup(); assert.equal(f.state().removed, f.intercept); assert.equal(f.state().closed, true);
    const init = f.response('InitializationSegment', { availabilityStartTime: new Date() });
    const before = init.data; await f.intercept(init); assert.equal(init.data, before); assert.equal(f.initialized.length, 0);
});
