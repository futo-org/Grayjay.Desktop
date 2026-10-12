#!/usr/bin/env python3
import argparse, hashlib, json, shutil, subprocess, tarfile, urllib.request
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--cache', type=Path, required=True)
parser.add_argument('--windows-deps', type=Path, help='Also extract the pinned cross-build dependencies')
args = parser.parse_args()
args.cache.mkdir(parents=True, exist_ok=True)
here = Path(__file__).resolve().parent
sources = json.loads((here / 'sources.json').read_text())
def fetch(source):
    destination = args.cache / source['url'].split('/')[-1]
    if not destination.exists() or hashlib.sha256(destination.read_bytes()).hexdigest() != source['sha256']:
        temporary = destination.with_suffix(destination.suffix + '.partial')
        try:
            with urllib.request.urlopen(source['url'], timeout=60) as response, temporary.open('wb') as output:
                shutil.copyfileobj(response, output)
            if hashlib.sha256(temporary.read_bytes()).hexdigest() != source['sha256']:
                raise RuntimeError('Source hash mismatch: ' + str(destination))
            temporary.replace(destination)
        finally:
            temporary.unlink(missing_ok=True)
    print(destination)
    return destination
for name, source in sources.items():
    if name != 'mingw_headers' or args.windows_deps: fetch(source)
if args.windows_deps:
    args.windows_deps.mkdir(parents=True, exist_ok=True)
    for source in json.loads((here / 'windows-dependencies.json').read_text()):
        archive = fetch(source)
        process = subprocess.Popen(['zstd', '-dc', str(archive)], stdout=subprocess.PIPE)
        with tarfile.open(fileobj=process.stdout, mode='r|') as contents:
            contents.extractall(args.windows_deps, filter='data')
        if process.wait(): raise RuntimeError('Could not extract ' + str(archive))
    archive = fetch(sources['mingw_headers'])
    headers = {'winhvplatform.h', 'winhvplatformdefs.h', 'winhvemulation.h'}
    process = subprocess.Popen(['zstd', '-dc', str(archive)], stdout=subprocess.PIPE)
    with tarfile.open(fileobj=process.stdout, mode='r|') as contents:
        for member in contents:
            if member.isfile() and Path(member.name).name.lower() in headers:
                destination = args.windows_deps / 'mingw64/include' / Path(member.name).name.lower()
                destination.parent.mkdir(parents=True, exist_ok=True)
                with destination.open('wb') as output: shutil.copyfileobj(contents.extractfile(member), output)
                headers.remove(destination.name)
    if process.wait() or headers: raise RuntimeError('Incomplete WHPX headers')
