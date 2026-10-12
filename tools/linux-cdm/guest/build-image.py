import gzip,hashlib,json,os,pathlib,re,shutil,stat,subprocess,sys,zipfile
out=pathlib.Path(sys.argv[1]);out.mkdir(parents=True,exist_ok=True)
root=out/'root';shutil.rmtree(root,ignore_errors=True);root.mkdir()
host=out/'cdm-host'
subprocess.run(['g++','-O2','-std=c++17','host.cc','-I/opt/ffmpeg/include','-L/opt/ffmpeg/lib','-lavformat','-lavcodec','-lavutil','-ldl','-lm','-pthread','-o',str(host)],check=True)
def copy(source,destination=None):
 p=root/(destination or str(source)).lstrip('/');p.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,p,follow_symlinks=True);return p
def dependencies(binary):
 result=subprocess.check_output(['ldd',str(binary)],text=True)
 for path in re.findall(r'(/[^\s()]+)',result):
  if pathlib.Path(path).is_file():copy(path)
copy('/bin/busybox')
for name in ['sh','mount','insmod','mkdir','poweroff','cat','sleep']:(root/'bin'/name).symlink_to('/bin/busybox')
copy(host,'/usr/local/bin/cdm-host');dependencies(host)

for name in ['libdl.so.2','libpthread.so.0']:
 copy('/lib/x86_64-linux-gnu/'+name)
for name in ['proc','sys','dev','tmp','opt/widevine']:(root/name).mkdir(parents=True,exist_ok=True)
(root/'tmp').chmod(0o1777)
kernel=max(pathlib.Path('/lib/modules').iterdir(),key=lambda p:[int(x) if x.isdigit() else x for x in re.split(r'(\d+)',p.name)])
modules={p.stem.replace('-','_'):p for p in kernel.rglob('*.ko')};ordered=[]
def module(name):
 p=modules[name]
 if p in ordered:return
 deps=subprocess.check_output(['modinfo','-F','depends',str(p)],text=True).strip()
 for dependency in filter(None,deps.split(',')):module(dependency.replace('-','_'))
 ordered.append(p);copy(p)
module('virtio_pci');module('virtio_console')
init='''#!/bin/sh
mount -t proc proc /proc
mount -t sysfs sysfs /sys
mount -t devtmpfs devtmpfs /dev
'''+ '\n'.join('insmod '+str(p) for p in ordered)+'''
attempt=0
while [ ! -c /dev/vport0p1 ] && [ "$attempt" -lt 100 ]; do
    sleep 0.1
    attempt=$((attempt + 1))
done
if [ ! -c /dev/vport0p1 ]; then
    echo "Playback transport device did not initialize" >&2
    poweroff -f
fi
exec 3<> /dev/vport0p1
/usr/local/bin/cdm-host /opt/widevine/libwidevinecdm.so <&3 >&3
poweroff -f
'''
(root/'init').write_text(init);(root/'init').chmod(0o755)
shutil.copy2('/boot/vmlinuz-'+kernel.name,out/'vmlinuz')
with gzip.open(out/'initramfs.cpio.gz','wb',compresslevel=6) as stream:
 inode=0
 def entry(name,mode,data=b'',major=0,minor=0):
  global inode
  inode+=1;encoded=name.encode()+b'\0';fields=[inode,mode,0,0,1,0,len(data),0,0,major,minor,len(encoded),0]
  header=b'070701'+b''.join(f'{v:08x}'.encode() for v in fields)
  stream.write(header+encoded+b'\0'*((-len(header)-len(encoded))%4));stream.write(data+b'\0'*(-len(data)%4))
 for p in [root]+sorted(root.rglob('*')):
  name='.' if p==root else str(p.relative_to(root));mode=p.lstat().st_mode
  if p.is_symlink():entry(name,mode,os.readlink(p).encode())
  elif p.is_dir():entry(name,mode)
  elif p.is_file():entry(name,mode,p.read_bytes())
 entry('dev/console',stat.S_IFCHR|0o600,major=5,minor=1);entry('dev/null',stat.S_IFCHR|0o666,major=1,minor=3)
 entry('TRAILER!!!',0)
licenses=out/'licenses';licenses.mkdir(exist_ok=True)
for source,name in [('/build/COPYING.LGPLv2.1','FFmpeg-LGPL-2.1.txt'),('/usr/share/doc/busybox-static/copyright','BusyBox-copyright.txt'),('/usr/share/doc/linux-image-'+kernel.name+'/copyright','Linux-copyright.txt'),('/usr/share/doc/libc6/copyright','glibc-copyright.txt'),('/usr/share/doc/libstdc++6/copyright','libstdc++-copyright.txt'),('/usr/share/doc/libgcc-s1/copyright','libgcc-copyright.txt')]:
 if pathlib.Path(source).exists():shutil.copy2(source,licenses/name)
metadata={'protocol':4,'kernel':kernel.name,'ffmpeg':'5.1.10','cdm_interface':10,'network':False,'persistent_disk':False,'bundled_cdm':False}
(out/'image.json').write_text(json.dumps(metadata,indent=2))
(out/'bundle.json').write_text(json.dumps({'version':'1','protocol':4,'kernelSha256':hashlib.sha256((out/'vmlinuz').read_bytes()).hexdigest(),'initramfsSha256':hashlib.sha256((out/'initramfs.cpio.gz').read_bytes()).hexdigest()},indent=2)+'\n')
(licenses/'debian-packages.txt').write_text(subprocess.check_output(['dpkg-query','-W','-f=${binary:Package}\t${Version}\t${source:Package}\t${source:Version}\n'],text=True))
with zipfile.ZipFile(out/'guest-linux-x64.zip','w',compression=zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
 for p in [out/'vmlinuz',out/'initramfs.cpio.gz',out/'image.json',out/'bundle.json']+sorted(licenses.rglob('*')):
  if p.is_file():archive.write(p,str(p.relative_to(out)))
print(json.dumps(metadata,indent=2))
