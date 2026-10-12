#!/usr/bin/env python3
import argparse, hashlib, json
from pathlib import Path
from urllib.parse import urlparse

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--directory', type=Path, required=True)
parser.add_argument('--base-url', required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
url = urlparse(args.base_url)
if url.scheme != 'https' and not (url.scheme == 'http' and url.hostname in ('localhost', '127.0.0.1', '::1')):
    parser.error('Use HTTPS for releases or loopback HTTP for local tests')
def asset(name):
    path = args.directory / name
    return {'url': args.base_url.rstrip('/') + '/' + name,
        'sha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'size': path.stat().st_size}
runtimes = {rid: asset('runtime-' + rid + '.zip') for rid in ('osx-arm64', 'osx-x64', 'win-x64')}

runtimes['win-arm64'] = runtimes['win-x64']
manifest = {'version': '1', 'protocol': 4, 'guest': asset('guest-linux-x64.zip'), 'runtimes': runtimes}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(manifest, indent=2) + '\n')
print(args.output)
