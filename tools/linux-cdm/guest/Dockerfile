FROM debian:bookworm-slim
RUN apt-get update && apt-get install -y --no-install-recommends build-essential ca-certificates curl xz-utils pkg-config busybox-static linux-image-cloud-amd64 python3 kmod cpio && rm -rf /var/lib/apt/lists/*
ARG FFMPEG_VERSION=5.1.10
ARG FFMPEG_SHA256=392306d6fc45dab0e9e0ea55381e071842e83a2fb31d320aeda40477a7766293
RUN curl -fL --retry 3 "https://ffmpeg.org/releases/ffmpeg-${FFMPEG_VERSION}.tar.xz" -o /tmp/ffmpeg.tar.xz && \
    echo "${FFMPEG_SHA256}  /tmp/ffmpeg.tar.xz" | sha256sum -c - && \
    mkdir /build && tar -xf /tmp/ffmpeg.tar.xz -C /build --strip-components=1 && \
    cd /build && ./configure --disable-everything --disable-autodetect --disable-network \
      --disable-programs --disable-doc --disable-x86asm --disable-avdevice \
      --disable-swscale --disable-swresample --disable-postproc --disable-shared \
      --enable-static --enable-demuxer=mov --enable-protocol=file --prefix=/opt/ffmpeg && \
    make -j4 && make install
WORKDIR /source
CMD ["python3", "build-image.py", "/out"]
