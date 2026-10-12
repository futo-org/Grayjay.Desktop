type Box = { at: number; end: number; header: number; type: string };
const containers = new Set(['moov', 'trak', 'mdia', 'minf', 'stbl', 'moof', 'traf', 'sinf', 'schi', 'mvex']);
const widevine = 'edef8ba979d64acea3c827dcd51d21ed';
function boxes(data: Uint8Array, begin: number, end: number): Box[] {
    const view = new DataView(data.buffer, data.byteOffset, data.byteLength);
    const result: Box[] = [];
    for (let at = begin; at < end;) {
        if (end - at < 8) throw new Error('Truncated MP4 box.');
        let size = view.getUint32(at), header = 8;
        if (size === 1) {
            if (end - at < 16) throw new Error('Truncated extended MP4 box.');
            const large = view.getBigUint64(at + 8);
            if (large > BigInt(Number.MAX_SAFE_INTEGER)) throw new Error('MP4 box is too large.');
            size = Number(large); header = 16;
        } else if (size === 0) size = end - at;
        if (size < header || size > end - at) throw new Error('Invalid MP4 box size.');
        result.push({ at, end: at + size, header, type: String.fromCharCode(...data.subarray(at + 4, at + 8)) });
        at += size;
    }
    return result;
}
function childStart(data: Uint8Array, box: Box): number | undefined {
    if (containers.has(box.type)) return box.at + box.header;
    if (box.type === 'stsd') return box.at + box.header + 8;
    if (box.type === 'encv') return box.at + box.header + 78;
    if (box.type === 'enca') {
        const view = new DataView(data.buffer, data.byteOffset, data.byteLength);
        if (box.end - box.at < box.header + 28) throw new Error('Truncated audio sample entry.');
        const version = view.getUint16(box.at + box.header + 8);
        if (version > 2) throw new Error('Unsupported audio sample entry version.');
        return box.at + box.header + [28, 44, 64][version];
    }
}

export function assertSupportedEncryption(data: Uint8Array): void {
    const walk = (begin: number, end: number, depth: number) => {
        if (depth > 16) throw new Error('MP4 nesting is too deep.');
        for (const box of boxes(data, begin, end)) {
            if ((box.type === 'sgpd' || box.type === 'sbgp') && box.end - box.at >= box.header + 8 &&
                String.fromCharCode(...data.subarray(box.at + box.header + 4, box.at + box.header + 8)) === 'seig')
                throw new Error('This MP4 uses sample-group encryption, which the playback helper does not yet support.');
            const child = childStart(data, box);
            if (child !== undefined) walk(child, box.end, depth + 1);
        }
    };
    walk(0, data.length, 0);
}
export function initializationData(data: Uint8Array): Uint8Array<ArrayBuffer>[] {
    const found: Uint8Array<ArrayBuffer>[] = [];
    const walk = (begin: number, end: number, depth: number) => {
        if (depth > 16) throw new Error('MP4 nesting is too deep.');
        for (const box of boxes(data, begin, end)) {
            if (box.type === 'pssh' && box.end - box.at >= box.header + 20) {
                const id = Array.from(data.subarray(box.at + box.header + 4, box.at + box.header + 20), x => x.toString(16).padStart(2, '0')).join('');
                if (id === widevine) found.push(data.slice(box.at, box.end));
            }
            const child = childStart(data, box);
            if (child !== undefined) walk(child, box.end, depth + 1);
        }
    };
    walk(0, data.length, 0); return found;
}

export function clearEncryptionMetadata(input: Uint8Array): Uint8Array<ArrayBuffer> {
    const data = new Uint8Array(input);
    const rename = (at: number, name: string) => data.set(Array.from(name, c => c.charCodeAt(0)), at + 4);
    const walk = (begin: number, end: number, depth: number) => {
        if (depth > 16) throw new Error('MP4 nesting is too deep.');
        for (const box of boxes(data, begin, end)) {
            const child = childStart(data, box);
            if (box.type === 'enca' || box.type === 'encv') {
                const sinf = boxes(data, child!, box.end).find(x => x.type === 'sinf');
                const frma = sinf && boxes(data, sinf.at + sinf.header, sinf.end).find(x => x.type === 'frma');
                if (!frma || frma.end - frma.at !== frma.header + 4) throw new Error('Encrypted sample entry lacks its original format.');
                rename(box.at, String.fromCharCode(...data.subarray(frma.at + frma.header, frma.end)));
            }
            if (child !== undefined) walk(child, box.end, depth + 1);
            if (['sinf', 'pssh', 'senc', 'saiz', 'saio'].includes(box.type)) rename(box.at, 'free');
            if ((box.type === 'sgpd' || box.type === 'sbgp') && box.end - box.at >= box.header + 8 &&
                String.fromCharCode(...data.subarray(box.at + box.header + 4, box.at + box.header + 8)) === 'seig') rename(box.at, 'free');
        }
    };
    walk(0, data.length, 0); return data;
}
