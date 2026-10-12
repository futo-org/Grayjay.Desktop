#!/usr/bin/env python3
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import zipfile

module = Path(__file__).resolve().parent / 'runtime/blink'
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--blink', type=Path, required=True)
parser.add_argument('--source', type=Path, required=True)
parser.add_argument('--runtime', type=Path, required=True)
parser.add_argument('--output', type=Path, help='Optional staging ZIP')
parser.add_argument('--identity', default='-')
args = parser.parse_args()
pins = json.loads((module / 'sources.json').read_text())
revision = subprocess.check_output(['git', '-C', str(args.source), 'rev-parse', 'HEAD'], text=True).strip()
if revision != pins['blink_commit']:
    parser.error('Unexpected Blink source revision')
subprocess.run(['git', '-C', str(args.source), 'apply', '--reverse', '--check', str(module / 'cdm.patch')], check=True)
dependencies = subprocess.check_output(['otool', '-L', str(args.blink)], text=True).splitlines()[1:]
if any(not line.strip().startswith(('/usr/lib/', '/System/Library/')) for line in dependencies):
    parser.error('Blink must depend only on macOS system libraries')
with tempfile.TemporaryDirectory(prefix='grayjay-blink-package-') as temporary:
    root = Path(temporary)
    binary = root / 'blink'
    shutil.copy2(args.blink, binary)
    binary.chmod(0o755)
    subprocess.run(['strip', '-S', str(binary)], check=True)
    command = ['codesign', '--force', '--sign', args.identity, '--entitlements',
               str(module.parents[3] / 'Grayjay.Desktop.CEF/Entitlements/blink.entitlements'), '--options', 'runtime']
    if args.identity != '-':
        command += ['--timestamp']
    subprocess.run(command + [str(binary)], check=True)
    subprocess.run([str(binary), '-h'], check=True, capture_output=True)
    licenses = root / 'licenses/blink'
    licenses.mkdir(parents=True)
    shutil.copy2(args.source / 'LICENSE', licenses / 'LICENSE')
    metadata = {'version': '1', 'protocol': 4, 'blinkRepository': pins['blink_repository'],
                'blinkCommit': revision, 'blinkSha256': hashlib.sha256(binary.read_bytes()).hexdigest(),
                'architecture': subprocess.check_output(['lipo', '-archs', str(binary)], text=True).strip()}
    (root / 'blink-runtime.json').write_text(json.dumps(metadata, indent=2) + '\n')
    if args.runtime.exists():
        shutil.rmtree(args.runtime)
    shutil.copytree(root, args.runtime)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(args.output, 'w', zipfile.ZIP_DEFLATED) as archive:
            for path in sorted(root.rglob('*')):
                if path.is_file():
                    archive.write(path, path.relative_to(root))
print('Packaged Blink:', args.runtime)
