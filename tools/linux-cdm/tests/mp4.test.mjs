import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import { transformSync } from '../../../Grayjay.Desktop.Web/node_modules/esbuild/lib/main.js';

const source = readFileSync(new URL('../../../Grayjay.Desktop.Web/src/components/player/LinuxCdm/mp4.ts', import.meta.url), 'utf8');
const code = transformSync(source, { loader: 'ts', format: 'cjs', target: 'es2022' }).code;
const context = { module: { exports: {} }, Uint8Array, DataView, BigInt, Number, Set, Array, String, Error };
vm.runInNewContext(code, context);
const { assertSupportedEncryption, clearEncryptionMetadata, initializationData } = context.module.exports;
const bytes = text => Buffer.from(text, 'ascii');
function box(type, ...data) {
    const payload = Buffer.concat(data); const header = Buffer.alloc(8);
    header.writeUInt32BE(8 + payload.length); header.write(type, 4); return Buffer.concat([header, payload]);
}
function extended(type, payload) {
    const header = Buffer.alloc(16); header.writeUInt32BE(1); header.write(type, 4); header.writeBigUInt64BE(BigInt(16 + payload.length), 8);
    return Buffer.concat([header, payload]);
}
function track(type, format, version = 0) {
    const header = Buffer.alloc(type === 'encv' ? 78 : [28, 44, 64][version]);
    if (type === 'enca') header.writeUInt16BE(version, 8);
    const entry = box(type, header, box('sinf', box('frma', bytes(format)), box('schi', box('tenc', Buffer.alloc(24)))));
    return box('moov', box('trak', box('mdia', box('minf', box('stbl', box('stsd', Buffer.alloc(8), entry))))));
}
test('restores the original video and audio codecs and preserves box offsets', () => {
    for (const [type, format, version] of [['encv', 'avc1', 0], ['encv', 'hvc1', 0], ['enca', 'mp4a', 0], ['enca', 'ac-3', 1], ['enca', 'ec-3', 2]]) {
        const input = track(type, format, version), before = Buffer.from(input);
        const result = Buffer.from(clearEncryptionMetadata(input));
        assert.equal(result.length, input.length); assert.deepEqual(input, before);
        assert.equal(result.toString('ascii', input.indexOf(type), input.indexOf(type) + 4), format);
        assert.equal(result.toString('ascii', input.indexOf('sinf'), input.indexOf('sinf') + 4), 'free');
    }
});
test('retains media bytes and unrelated sample groups', () => {
    const input = Buffer.concat([box('moof', box('traf', box('senc', Buffer.alloc(12)), box('sgpd', Buffer.alloc(4), bytes('seig')), box('sgpd', Buffer.alloc(4), bytes('roll')))), box('mdat', bytes('untouched clear samples'))]);
    const result = Buffer.from(clearEncryptionMetadata(input));
    assert.deepEqual(result.subarray(input.indexOf('mdat') - 4), input.subarray(input.indexOf('mdat') - 4));
    assert.equal(result.toString('ascii', input.indexOf('sgpd'), input.indexOf('sgpd') + 4), 'free');
    assert.equal(result.toString('ascii', input.lastIndexOf('sgpd'), input.lastIndexOf('sgpd') + 4), 'sgpd');
});
test('finds only Widevine PSSH through extended-size containers', () => {
    const widevine = box('pssh', Buffer.alloc(4), Buffer.from('edef8ba979d64acea3c827dcd51d21ed', 'hex'), Buffer.alloc(4));
    const other = box('pssh', Buffer.alloc(24));
    const input = extended('moov', Buffer.concat([widevine, other]));
    const found = initializationData(input);
    assert.equal(found.length, 1); assert.deepEqual(Buffer.from(found[0]), widevine);
    assert.equal(Buffer.from(clearEncryptionMetadata(input)).toString('ascii', 20, 24), 'free');
});
test('rejects truncated boxes and encrypted entries without an original format', () => {
    assert.throws(() => clearEncryptionMetadata(Buffer.from([0, 0, 0, 20, 109, 100, 97, 116])));
    assert.throws(() => clearEncryptionMetadata(box('enca', Buffer.alloc(28))));
});
test('rejects unsupported key overrides while retaining unrelated sample groups', () => {
    assert.throws(() => assertSupportedEncryption(box('moof', box('traf', box('sgpd', Buffer.alloc(4), bytes('seig'))))), /sample-group encryption/);
    assert.doesNotThrow(() => assertSupportedEncryption(box('moof', box('traf', box('sgpd', Buffer.alloc(4), bytes('roll'))))));
});
