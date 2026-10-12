#!/usr/bin/env python3
import argparse, base64, gzip, json, os, queue, shutil, struct, subprocess, tempfile, threading, urllib.request
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--runtime', type=Path, required=True)
parser.add_argument('--guest', type=Path, required=True)
parser.add_argument('--cdm', type=Path, required=True)
parser.add_argument('--reuse', action='store_true')
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
if os.name == 'nt':
    parser.error('Windows uses Blink; run DownloadTests.csproj -- --blink-reference instead.')
b64 = lambda value: base64.b64encode(value).decode()
pssh = 'AAAAPnBzc2gAAAAA7e+LqXnWSs6jyCfc1R0h7QAAAB4iFnNoYWthX2NlYzJmNjRhYTc4OTBhMTFI49yVmwY='
media = urllib.request.urlopen('https://storage.googleapis.com/shaka-demo-assets/angel-one-widevine/v-0144p-0100k-libx264.mp4', timeout=30).read()
at = 0
while media[at + 4:at + 8] != b'moof':
    length = struct.unpack('>I', media[at:at + 4])[0]
    if length < 8: raise RuntimeError('Invalid public MP4')
    at += length
report = {'success': False}
with tempfile.TemporaryDirectory(prefix='grayjay-guest-probe-') as temporary:
    root = Path(temporary); shutil.copy2(args.guest / 'vmlinuz', root / 'vmlinuz')
    initrd = root / 'boot.cpio.gz'; shutil.copy2(args.guest / 'initramfs.cpio.gz', initrd)
    with gzip.open(initrd, 'ab') as output:
        def entry(name, mode, data=b''):
            encoded = name.encode() + b'\0'
            fields = [1, mode, 0, 0, 1, 0, len(data), 0, 0, 0, 0, len(encoded), 0]
            header = b'070701' + b''.join(f'{value:08x}'.encode() for value in fields)
            output.write(header + encoded + b'\0' * ((-len(header) - len(encoded)) % 4))
            output.write(data + b'\0' * (-len(data) % 4))
        entry('opt', 0o40755); entry('opt/widevine', 0o40755)
        entry('opt/widevine/libwidevinecdm.so', 0o100644, args.cdm.read_bytes()); entry('TRAILER!!!', 0)
    command = [str(args.runtime / 'qemu-system-x86_64')]
    firmware, kernel, boot, log = str(args.runtime / 'data'), str(root / 'vmlinuz'), str(initrd), str(root / 'console.log')
    command += ['-nodefaults', '-no-user-config', '-no-reboot', '-machine', 'q35', '-accel', 'tcg,thread=multi,tb-size=16', '-cpu', 'max', '-smp', '1', '-m', '256',
        '-kernel', kernel, '-initrd', boot, '-append', 'console=ttyS0 rdinit=/init panic=-1', '-display', 'none', '-monitor', 'none', '-nic', 'none', '-L', firmware,
        '-chardev', 'file,id=boot,path=' + log.replace(',', ',,'), '-device', 'isa-serial,chardev=boot', '-device', 'virtio-serial-pci',
        '-chardev', 'stdio,id=cdm,signal=off', '-device', 'virtserialport,chardev=cdm,nr=1,name=grayjay.cdm']
    stopped = threading.Event(); lines = queue.Queue(); lock = threading.Lock()
    def send(line):
        with lock: process.stdin.write(line + '\n'); process.stdin.flush()
    def read():
        for line in process.stdout: lines.put(line)
        lines.put(None)
    def heartbeat():
        while not stopped.wait(2):
            try: send('PING')
            except (OSError, ValueError): return
    with (root / 'stderr.log').open('w') as diagnostics:
        process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=diagnostics, text=True)
        threading.Thread(target=read, daemon=True).start()
        threading.Thread(target=heartbeat, daemon=True).start()
        try:
            sent = False; rounds = 0; active_session = None; closing = None; missing_key = False
            while True:
                line = lines.get(timeout=45)
                if line is None: raise RuntimeError('Guest stopped: ' + (root / 'stderr.log').read_text()[-1500:])
                event = json.loads(line)
                if event['event'] == 'initialized':
                    assert event['protocol'] == 4 and event['success']; report['initialized'] = True
                    send('SESSION 1 ' + pssh)
                elif event['event'] == 'created':
                    active_session = event['session']
                elif event['event'] == 'message':
                    request = urllib.request.Request('https://proxy.uat.widevine.com/proxy', data=base64.b64decode(event['data']), headers={'Content-Type': 'application/octet-stream'})
                    with urllib.request.urlopen(request, timeout=30) as response:
                        report['licenseStatus'] = response.status; license = response.read()
                    send('UPDATE 2 ' + event['session'] + ' ' + b64(license))
                elif event['event'] == 'keys' and 0 in event['statuses'] and not sent:
                    sent = True; send('FRAGMENT 3 ' + b64(struct.pack('>I', at) + media))
                elif event['event'] == 'fragment':
                    if missing_key: raise RuntimeError('Closed license still decrypts media')
                    assert event['samples'] == 1290; assert len(base64.b64decode(event['data'])) == len(media) - at
                    rounds += 1; report['decryptedSamples'] = event['samples']; report['rounds'] = rounds
                    if args.reuse and rounds < 2:
                        closing = active_session; send('CLOSE 4 ' + closing)
                    else:
                        report['success'] = True; send('QUIT'); break
                elif event['event'] == 'closed' and event['session'] == closing:
                    report['closedPreviousSession'] = True; closing = None; missing_key = True
                    send('FRAGMENT 5 ' + b64(struct.pack('>I', at) + media))
                elif event['event'] == 'error' and event.get('id') == 5 and missing_key:
                    assert 'could not decrypt' in event['message'], event
                    report['closedKeysReleased'] = True; missing_key = False; sent = False
                    send('SESSION 1 ' + pssh)
                elif event['event'] in ('error', 'rejected'): raise RuntimeError(event['message'])
            process.wait(timeout=5)
        except Exception as error:
            report['error'] = str(error)
        finally:
            stopped.set()
            if process.poll() is None: process.kill(); process.wait()
args.output.write_text(json.dumps(report, indent=2)); print(json.dumps(report))
if not report['success']: raise SystemExit(1)
