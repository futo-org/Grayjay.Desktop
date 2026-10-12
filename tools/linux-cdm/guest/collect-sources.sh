#!/bin/sh

set -eu
sed -i 's/^Types: deb$/Types: deb deb-src/' /etc/apt/sources.list.d/debian.sources
apt-get update -qq
mkdir -p /out/debian
cd /out/debian
for package in busybox-static libc6 libstdc++6 libgcc-s1; do
    source=$(dpkg-query -W '-f=${source:Package}=${source:Version}' "$package")
    apt-get source --download-only "$source"
done
kernel=$(ls /boot/vmlinuz-* | sort -V | tail -1)
package="linux-image-${kernel#/boot/vmlinuz-}"
source=$(dpkg-query -W '-f=${source:Package}=${source:Version}' "$package")
apt-get source --download-only "$source"
version=$(dpkg-query -W '-f=${Version}' "$package")
apt-get source --download-only "linux=$version"
