#!/usr/bin/env python3
"""Check that dependency staging cannot preserve or reintroduce Windows QEMU."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import zipfile

stager = Path(__file__).resolve().parents[1] / 'stage-bundled.py'
with tempfile.TemporaryDirectory(prefix='grayjay-staging-test-') as temporary:
    root = Path(temporary)
    archives = root / 'archives'
    archives.mkdir()
    output = root / 'output'
    windows = output / 'runtimes/win-x64'
    windows.mkdir(parents=True)
    (windows / 'qemu-system-x86_64.exe').write_bytes(b'obsolete')

    for rid in ('osx-arm64', 'osx-x64'):
        obsolete = output / 'runtimes' / rid / 'data'
        obsolete.mkdir(parents=True)
        (obsolete.parent / 'qemu-system-x86_64').write_bytes(b'obsolete')
        (obsolete / 'bios-256k.bin').write_bytes(b'obsolete')

    def archive(name, files):
        with zipfile.ZipFile(archives / name, 'w') as bundle:
            for path, data in files.items():
                bundle.writestr(path, data)

    archive('guest-linux-x64.zip', {
        'vmlinuz': b'kernel', 'initramfs.cpio.gz': b'guest',
        'image.json': json.dumps({'protocol': 4})})
    for rid in ('osx-arm64', 'osx-x64'):
        archive('runtime-' + rid + '.zip', {
            'blink': b'mac', 'licenses/blink/LICENSE': b'license', 'blink-runtime.json': json.dumps({
                'version': '1', 'protocol': 4, 'blinkSha256': hashlib.sha256(b'mac').hexdigest()})})
    files = {'blink.exe': b'blink', 'cygwin1.dll': b'cygwin'}
    files['blink-runtime.json'] = json.dumps({
        'version': '1', 'protocol': 4,
        'blinkSha256': hashlib.sha256(b'blink').hexdigest(),
        'cygwinSha256': hashlib.sha256(b'cygwin').hexdigest()})
    archive('runtime-win-x64.zip', files)
    command = [sys.executable, str(stager), '--directory', str(archives),
               '--output', str(output)]
    subprocess.run(command, check=True, capture_output=True)
    assert not (windows / 'qemu-system-x86_64.exe').exists()
    assert (windows / 'blink.exe').read_bytes() == b'blink'

    for rid in ('osx-arm64', 'osx-x64'):
        runtime = output / 'runtimes' / rid
        assert not (runtime / 'qemu-system-x86_64').exists()
        assert not (runtime / 'data').exists()
        assert (runtime / 'blink').read_bytes() == b'mac'

    publish = root / 'publish/playback'
    shutil.copytree(output / 'runtimes/osx-arm64', publish / 'runtime')
    shutil.copytree(output / 'guest', publish / 'guest')
    app = root / 'Grayjay.app'
    helpers = app / 'Contents/Helpers'
    helpers.mkdir(parents=True)
    (helpers / 'qemu-system-x86_64').write_bytes(b'obsolete')
    resources = app / 'Contents/Resources/playback/data'
    resources.mkdir(parents=True)
    (resources / 'bios-256k.bin').write_bytes(b'obsolete')
    subprocess.run([sys.executable, str(stager.parent / 'bundle-macos.py'),
                    '--publish', str(publish.parent), '--app', str(app)], check=True, capture_output=True)
    assert (helpers / 'blink').read_bytes() == b'mac'
    assert not (helpers / 'qemu-system-x86_64').exists()
    assert not resources.exists()
    assert not (resources.parent / 'guest/vmlinuz').exists()
    assert (resources.parent / 'blink-runtime.json').is_file()
    assert (resources.parent / 'guest/bundle.json').is_file()

    archive('runtime-osx-x64.zip', {
        'blink': b'mac', 'licenses/blink/LICENSE': b'license', 'blink-runtime.json': json.dumps({
            'version': '1', 'protocol': 4, 'blinkSha256': hashlib.sha256(b'mac').hexdigest()}),
        'qemu-system-x86_64': b'forbidden'})
    rejected = subprocess.run(command, capture_output=True)
    assert rejected.returncode != 0 and b'Unexpected osx-x64 runtime file' in rejected.stderr
    assert (output / 'runtimes/osx-x64/blink').read_bytes() == b'mac'
    archive('runtime-osx-x64.zip', {
        'blink': b'mac', 'licenses/blink/LICENSE': b'license', 'blink-runtime.json': json.dumps({
            'version': '1', 'protocol': 4, 'blinkSha256': hashlib.sha256(b'mac').hexdigest()})})

    files['qemu-system-x86_64.exe'] = b'forbidden'
    archive('runtime-win-x64.zip', files)
    rejected = subprocess.run(command, capture_output=True)
    assert rejected.returncode != 0
    assert b'Unexpected win-x64 runtime file' in rejected.stderr
    assert (windows / 'blink.exe').read_bytes() == b'blink'
    assert not (windows / 'qemu-system-x86_64.exe').exists()
print('PASS stale Windows/macOS QEMU removal, app bundling and mixed-archive rejection')
