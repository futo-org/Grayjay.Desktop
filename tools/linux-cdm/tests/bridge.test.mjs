import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import { transformSync } from '../../../Grayjay.Desktop.Web/node_modules/esbuild/lib/main.js';

const source = readFileSync(new URL('../../../Grayjay.Desktop.Web/src/components/player/LinuxCdm/bridge.ts', import.meta.url), 'utf8');
const code = transformSync(source, { loader: 'ts', format: 'cjs', target: 'es2022' }).code;
function load(capabilities) {
    const rpc = { register() {}, call: async method => {
        if (method !== 'linuxCdm.capabilities') throw new Error('unexpected ' + method);
        if (capabilities instanceof Error) throw capabilities;
        return capabilities;
    } };
    const context = { module: { exports: {} }, window: { bridge: { rpc } } };
    context.exports = context.module.exports;
    vm.runInNewContext(code, context);
    return context.module.exports;
}

test('capabilities accept the protocol 4 helper', async () => {
    assert.deepEqual({ ...await load({ enabled: true, ready: true, protocol: 4 }).capabilities() }, { enabled: true, ready: true });
});

test('capabilities reject an older helper protocol', async () => {
    assert.deepEqual({ ...await load({ enabled: true, ready: true, protocol: 3 }).capabilities() }, { enabled: false, ready: true });
});

test('capabilities report unavailable when the bridge call fails', async () => {
    assert.deepEqual({ ...await load(new Error('no bridge')).capabilities() }, { enabled: false, ready: false });
});
