#!/bin/bash
set -euo pipefail
module=$(cd "$(dirname "$0")" && pwd)
arch=${1:-$(uname -m)}
case "$arch" in arm64) rid=osx-arm64 ;; x86_64) rid=osx-x64 ;; *) echo 'Expected arm64 or x86_64' >&2; exit 1 ;; esac
root=${CDM_BUILD_ROOT:-/tmp/grayjay-cdm-blink-$arch}
output=${2:-$module/../../../../Grayjay.ClientServer/deps/playback/runtimes/$rid}
revision=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["blink_commit"])' "$module/sources.json")
if [ ! -d "$root/.git" ]; then
    git clone --no-checkout https://github.com/jart/blink.git "$root"
    git -C "$root" checkout --detach "$revision"
fi
[ "$(git -C "$root" rev-parse HEAD)" = "$revision" ] || { echo 'Unexpected Blink revision' >&2; exit 1; }
if ! git -C "$root" apply --reverse --check "$module/cdm.patch" 2>/dev/null; then
    git -C "$root" apply --check "$module/cdm.patch"
    git -C "$root" apply "$module/cdm.patch"
fi
cp "$module"/winmap.{c,h} "$root/blink/"
cd "$root"
export MACOSX_DEPLOYMENT_TARGET=11.0
export CC="clang -arch $arch" CXX="clang++ -arch $arch"
./configure --enable-vfs --disable-sockets
python3 - "$arch" <<'PY'
from pathlib import Path
import sys
p = Path('build/config.mk')
s = p.read_text().replace('-march=native', '-march=x86-64 -mtune=generic' if sys.argv[1] == 'x86_64' else '')
p.write_text(s)
p = Path('config.mk')
s = p.read_text()
import re
s = re.sub(r'^HOST_ARCH = .*$', 'HOST_ARCH = ' + sys.argv[1], s, flags=re.M)
p.write_text(s)
PY
gmake -j4 MODE=opt o/opt/blink/blink
python3 "$module/../../package-macos.py" --blink "$root/o/opt/blink/blink" --source "$root" --runtime "$output"
