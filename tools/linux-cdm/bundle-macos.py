#!/usr/bin/env python3
import argparse
from pathlib import Path
import shutil

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--publish', type=Path, required=True)
parser.add_argument('--app', type=Path, required=True)
args = parser.parse_args()
source = args.publish / 'playback'
runtime = source / 'runtime'
blink = runtime / 'blink'
if (not blink.is_file() or not (runtime / 'blink-runtime.json').is_file() or
        not (runtime / 'licenses/blink').is_dir() or not (source / 'guest/bundle.json').is_file()):
    parser.error('Published playback dependencies are missing')

if any((runtime / 'lib').glob('*.dylib')):
    parser.error('Mac playback packaging requires the system-dependency Blink build')
contents = args.app / 'Contents'
helper = contents / 'Helpers/blink'
helper.parent.mkdir(parents=True, exist_ok=True)
(helper.parent / 'qemu-system-x86_64').unlink(missing_ok=True)
shutil.copy2(blink, helper)
helper.chmod(0o755)
data = contents / 'Resources/playback'
if data.exists():
    shutil.rmtree(data)
data.mkdir(parents=True, exist_ok=True)
shutil.copy2(runtime / 'blink-runtime.json', data / 'blink-runtime.json')
shutil.copytree(runtime / 'licenses/blink', data / 'licenses/blink')
shutil.copytree(source / 'guest', data / 'guest', ignore=shutil.ignore_patterns('vmlinuz'))
print('Bundled playback helper:', helper)
