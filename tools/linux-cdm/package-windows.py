#!/usr/bin/env python3
"""Package the Windows CDM-only Blink runtime for dependency staging."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--runtime', type=Path, default=Path(__file__).resolve().parents[2] / 'Grayjay.ClientServer/deps/playback/runtimes/win-x64')
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
manifest = json.loads((args.runtime / 'blink-runtime.json').read_text())
if manifest['version'] != '1' or manifest['protocol'] != 4:
    raise ValueError('Incompatible Blink runtime')
files = [args.runtime / name for name in ('blink.exe', 'cygwin1.dll', 'blink-runtime.json')]
for name, key in (('blink.exe', 'blinkSha256'), ('cygwin1.dll', 'cygwinSha256')):
    if hashlib.sha256((args.runtime / name).read_bytes()).hexdigest() != manifest[key]:
        raise ValueError('Damaged runtime: ' + name)
licenses = args.runtime / 'licenses/blink'
for name in ('BLINK-ISC.txt', 'CYGWIN-COPYING.txt', 'CYGWIN-CYGWIN_LICENSE.txt', 'sources.json'):
    if not (licenses / name).is_file():
        raise ValueError('Missing license or source receipt: ' + name)
pins = json.loads((licenses / 'sources.json').read_text())
source = pins['source']
archive_path = licenses / source['url'].rsplit('/', 1)[1]
if archive_path.stat().st_size != source['size'] or hashlib.sha512(archive_path.read_bytes()).hexdigest() != source['sha512']:
    raise ValueError('Missing or damaged corresponding Cygwin source archive')
for notice in pins['gcc_notices']:
    if hashlib.sha256((licenses / ('GCC-' + notice['name'] + '.txt')).read_bytes()).hexdigest() != notice['sha256']:
        raise ValueError('Missing or damaged GCC notice')
for name, key in [('cdm.patch', 'patchSha256'), ('winmap.c', 'mapperSha256'), ('winmap.h', 'mapperHeaderSha256')]:
    if hashlib.sha256((licenses / name).read_bytes()).hexdigest() != manifest[key]:
        raise ValueError('Damaged Blink source receipt: ' + name)
if pins['blink_commit'] != manifest['blinkCommit']:
    raise ValueError('Blink source revision differs from the runtime manifest')
files.extend(path for path in licenses.rglob('*') if path.is_file())
args.output.parent.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(args.output, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for file in sorted(files):
        archive.write(file, file.relative_to(args.runtime))
print(json.dumps({'file': str(args.output), 'size': args.output.stat().st_size,
                  'sha256': hashlib.sha256(args.output.read_bytes()).hexdigest()}, indent=2))
