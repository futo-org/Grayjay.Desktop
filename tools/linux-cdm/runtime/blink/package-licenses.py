"""Package pinned Cygwin license and corresponding source alongside Blink."""
import hashlib
import json
from pathlib import Path
import sys
import tarfile
import urllib.request

module = Path(__file__).resolve().parent
pins = json.loads((module / "sources.json").read_text())
cygwin, output = map(Path, sys.argv[1:])
output.mkdir(parents=True, exist_ok=True)
for kind in ("install", "source"):
    pin = pins[kind]
    filename = pin["url"].rsplit("/", 1)[1]
    archive = output / filename if kind == "source" else cygwin / "packages" / filename
    if not archive.exists():
        archive.parent.mkdir(parents=True, exist_ok=True)
        partial = archive.with_suffix(archive.suffix + ".partial")
        urllib.request.urlretrieve(pin["url"], partial)
        partial.replace(archive)
    if archive.stat().st_size != pin["size"] or hashlib.sha512(archive.read_bytes()).hexdigest() != pin["sha512"]:
        raise ValueError("Pinned Cygwin archive integrity check failed")
    if kind == "install":
        with tarfile.open(archive) as bundle:
            for name in ("COPYING", "CYGWIN_LICENSE"):
                member = bundle.getmember("usr/share/doc/Cygwin/" + name)
                (output / ("CYGWIN-" + name + ".txt")).write_bytes(bundle.extractfile(member).read())

for notice in pins["gcc_notices"]:
    target = output / ("GCC-" + notice["name"] + ".txt")
    data = target.read_bytes() if target.exists() else urllib.request.urlopen(notice["url"], timeout=30).read()
    if hashlib.sha256(data).hexdigest() != notice["sha256"]:
        raise ValueError("Pinned GCC notice integrity check failed")
    target.write_bytes(data)
