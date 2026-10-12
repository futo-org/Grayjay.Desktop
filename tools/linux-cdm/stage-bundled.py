#!/usr/bin/env python3
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import tempfile
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--directory', type=Path, required=True, help='Directory containing locally built component ZIPs')
parser.add_argument('--output', type=Path, default=Path(__file__).resolve().parents[2] / 'Grayjay.ClientServer/deps/playback')
args = parser.parse_args()
with tempfile.TemporaryDirectory(prefix='grayjay-playback-stage-') as temporary:
    staged = Path(temporary)
    for archive, directory in [('guest-linux-x64.zip', 'guest'), ('runtime-osx-arm64.zip', 'runtimes/osx-arm64'),
                               ('runtime-osx-x64.zip', 'runtimes/osx-x64'), ('runtime-win-x64.zip', 'runtimes/win-x64')]:
        target = staged / directory
        with zipfile.ZipFile(args.directory / archive) as zip:
            expanded = 0
            for entry in zip.infolist():
                name = entry.filename.replace('\\', '/')
                expanded += entry.file_size
                if name.startswith('/') or ':' in name or any(part in ('.', '..') for part in name.split('/')) or \
                        ((entry.external_attr >> 16) & 0o170000) == 0o120000 or expanded > 512 * 1024 * 1024:
                    raise ValueError('Invalid archive entry: ' + name)
                zip.extract(entry, target)
                path = target / entry.filename
                mode = (entry.external_attr >> 16) & 0o777
                if path.is_file() and mode:
                    path.chmod(mode)
    guest = staged / 'guest'
    if json.loads((guest / 'image.json').read_text())['protocol'] != 4:
        raise ValueError('Guest protocol is incompatible')
    metadata = {'version': '1', 'protocol': 4,
                'kernelSha256': hashlib.sha256((guest / 'vmlinuz').read_bytes()).hexdigest(),
                'initramfsSha256': hashlib.sha256((guest / 'initramfs.cpio.gz').read_bytes()).hexdigest()}
    (guest / 'bundle.json').write_text(json.dumps(metadata, indent=2) + '\n')
    for rid in ('osx-arm64', 'osx-x64', 'win-x64'):
        runtime = staged / 'runtimes' / rid
        windows = rid == 'win-x64'
        files = [('blink.exe', 'blinkSha256'), ('cygwin1.dll', 'cygwinSha256')] if windows else [('blink', 'blinkSha256')]
        allowed = {name for name, _ in files} | {'blink-runtime.json'}
        for path in runtime.rglob('*'):
            if path.is_file() and path.relative_to(runtime).as_posix() not in allowed and not path.is_relative_to(runtime / 'licenses/blink'):
                raise ValueError('Unexpected ' + rid + ' runtime file: ' + str(path))
        manifest = json.loads((runtime / 'blink-runtime.json').read_text())
        if manifest['version'] != '1' or manifest['protocol'] != 4:
            raise ValueError('Incompatible Blink runtime')
        for filename, key in files:
            if hashlib.sha256((runtime / filename).read_bytes()).hexdigest() != manifest[key]:
                raise ValueError('Damaged Blink runtime: ' + filename)
        (runtime / ('blink.exe' if windows else 'blink')).chmod(0o755)
    args.output.mkdir(parents=True, exist_ok=True)
    for rid in ('win-x64', 'osx-arm64', 'osx-x64'):
        destination = (args.output / 'runtimes' / rid).resolve()
        if not destination.is_relative_to(args.output.resolve()):
            raise ValueError('Runtime destination escapes the output directory')
        if destination.exists():
            shutil.rmtree(destination)
    shutil.copytree(staged, args.output, dirs_exist_ok=True)
print('Staged playback dependencies:', args.output)
