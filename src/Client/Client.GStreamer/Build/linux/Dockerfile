# Builds the GStreamer runtime shipped with the client for linux-x64.
# Ubuntu 22.04 keeps the glibc requirement at 2.35, so the result runs on Ubuntu 22.04+, Debian 12+
# and distributions of the same age. Nothing GPL is built: no x264/x265, no libav, -Dgpl=disabled.
# Every component is its own layer, so a failed step rebuilds only from there. Each version ARG sits
# right before the step that uses it: an ARG is part of the environment of every later RUN, so
# declaring them all at the top would make any version change rebuild everything.
FROM ubuntu:22.04

ENV DEBIAN_FRONTEND=noninteractive \
    PREFIX=/opt/gst \
    PKG_CONFIG_PATH=/opt/gst/lib/pkgconfig \
    LD_LIBRARY_PATH=/opt/gst/lib \
    PATH=/root/.cargo/bin:$PATH

# Only LGPL/BSD/MIT/Apache libraries come from the distribution; they are bundled by collect.sh
RUN apt-get update && apt-get install -y --no-install-recommends \
        build-essential ca-certificates git curl pkg-config python3 python3-pip \
        ninja-build cmake nasm flex bison patchelf file xz-utils zstd \
        libglib2.0-dev libssl-dev libopus-dev libvpx-dev libsrtp2-dev \
        libva-dev libdrm-dev libpulse-dev libasound2-dev libffi-dev zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

RUN pip3 install --no-cache-dir 'meson>=1.4,<2'

ARG CARGO_C_VERSION=v0.10.25
RUN curl -sSf https://sh.rustup.rs | sh -s -- -y --profile minimal --default-toolchain stable \
    && curl -sSfL "https://github.com/lu-zero/cargo-c/releases/download/${CARGO_C_VERSION}/cargo-c-x86_64-unknown-linux-musl.tar.gz" \
        | tar -xz -C /root/.cargo/bin

WORKDIR /src

# AV1 software decoder, needed by the dav1ddec element of gst-plugins-rs (BSD-2-Clause)
ARG DAV1D_VERSION=1.5.1
RUN git clone --depth 1 --branch "${DAV1D_VERSION}" https://code.videolan.org/videolan/dav1d.git \
    && meson setup dav1d/build dav1d --prefix="$PREFIX" --libdir=lib --buildtype=release \
        -Denable_tools=false -Denable_tests=false \
    && ninja -C dav1d/build install

# AV1 software encoder for svtav1enc (BSD-3-Clause-Clear + AOM patent license)
ARG SVTAV1_VERSION=v3.1.2
RUN git clone --depth 1 --branch "${SVTAV1_VERSION}" https://gitlab.com/AOMediaCodec/SVT-AV1.git svtav1 \
    && cmake -S svtav1 -B svtav1/build -G Ninja -DCMAKE_BUILD_TYPE=Release \
        -DCMAKE_INSTALL_PREFIX="$PREFIX" -DCMAKE_INSTALL_LIBDIR=lib \
        -DBUILD_SHARED_LIBS=ON -DBUILD_APPS=OFF -DBUILD_TESTING=OFF \
    && ninja -C svtav1/build install

# libva, only to compile the va plugin against; the bundle never ships it (the system libva must match
# the installed VA drivers). The plugin is built twice, see the va steps below.
ARG LIBVA_BASE_VERSION=2.20.0
ARG LIBVA_MODERN_VERSION=2.22.0
RUN for version in "$LIBVA_BASE_VERSION" "$LIBVA_MODERN_VERSION"; do \
        git clone --depth 1 --branch "$version" https://github.com/intel/libva.git "libva-$version" \
        && meson setup "libva-$version/build" "libva-$version" --prefix="/opt/libva-$version" --libdir=lib \
            --buildtype=release -Dwith_x11=no -Dwith_glx=no -Dwith_wayland=no -Dwith_win32=no \
        && ninja -C "libva-$version/build" install || exit 1; \
    done

# GStreamer itself. libnice, openh264, orc and usrsctp come from the monorepo's own subprojects
ARG GST_VERSION=1.28.7
RUN git clone --depth 1 --branch "${GST_VERSION}" https://gitlab.freedesktop.org/gstreamer/gstreamer.git

COPY meson-options.txt /src/meson-options.txt

# The va plugin of this build is the base variant, compiled against the libva 2.20 headers: that has
# vaav1enc (VA-API 1.15+) but calls no libva function newer than 2.14, so it loads on every system.
RUN cd gstreamer \
    && PKG_CONFIG_PATH="/opt/libva-$LIBVA_BASE_VERSION/lib/pkgconfig:$PKG_CONFIG_PATH" meson setup build --prefix="$PREFIX" --libdir=lib --buildtype=release \
        $(grep -v '^\s*#' /src/meson-options.txt | xargs) \
    && ninja -C build install

# The modern va variant, for a system libva 2.21+: it also queries the encoder surface alignment that
# AV1 encoding on AMD needs, which takes vaMapBuffer2. The loader picks the variant at startup.
RUN PKG_CONFIG_PATH="/opt/libva-$LIBVA_MODERN_VERSION/lib/pkgconfig:$PKG_CONFIG_PATH" \
        meson setup va-modern gstreamer/subprojects/gst-plugins-bad --prefix=/opt/va-modern --libdir=lib \
        --buildtype=release -Dauto_features=disabled -Dgpl=disabled -Dva=enabled -Ddrm=enabled \
        -Dexamples=disabled -Dtests=disabled -Dtools=disabled -Dnls=disabled -Dintrospection=disabled -Ddoc=disabled \
    && ninja -C va-modern sys/va/libgstva.so \
    && mkdir -p "$PREFIX/lib/gstreamer-1.0-va-modern" \
    && install -m 644 va-modern/sys/va/libgstva.so "$PREFIX/lib/gstreamer-1.0-va-modern/" \
    && cp -L va-modern/gst-libs/gst/va/libgstva-1.0.so.0 "$PREFIX/lib/gstreamer-1.0-va-modern/"

# Only the GStreamer plugin of PipeWire. libpipewire itself stays a system library: it loads the
# SPA modules of the installed PipeWire, so it must match the daemon on the user's machine.
# The features listed after the first line default to 'enabled', which auto_features does not reach.
# spa-plugins, audioconvert and support stay on: the PipeWire modules need them to configure.
# The oldest libpipewire the plugin must run with is 0.3.48 (Ubuntu 22.04). 0.3.65 still calls only
# functions 0.3.48 has, and its pipewiresrc already has the target-object property the client sets.
# 1.x calls pw_stream_get_nsec and fails to load there.
ARG PIPEWIRE_VERSION=0.3.65
RUN git clone --depth 1 --branch "${PIPEWIRE_VERSION}" https://gitlab.freedesktop.org/pipewire/pipewire.git \
    && meson setup pipewire/build pipewire --prefix=/opt/pipewire --libdir=lib --buildtype=release \
        -Dauto_features=disabled -Dgstreamer=enabled -Dsession-managers=[] \
        -Dexamples=disabled -Dtests=disabled -Dman=disabled -Ddocs=disabled \
        -Dsystemd-user-service=disabled -Dpipewire-jack=disabled -Dpipewire-v4l2=disabled \
        -Daudiomixer=disabled \
        -Dbluez5-backend-hsp-native=disabled -Dbluez5-backend-hfp-native=disabled \
        -Dbluez5-backend-ofono=disabled -Dbluez5-backend-hsphfpd=disabled \
        -Dcontrol=disabled -Daudiotestsrc=disabled -Ddbus=disabled \
        -Dvideoconvert=disabled -Dvideotestsrc=disabled -Dflatpak=disabled \
    && ninja -C pipewire/build \
    && install -m 755 pipewire/build/src/gst/libgstpipewire.so "$PREFIX/lib/gstreamer-1.0/"

# A second build for libpipewire 1.2+, chosen by the loader where the system has it: its pipewiresrc
# negotiates DMA-BUF modifiers (DMA_DRM caps), which the VA postprocessor takes without a copy
ARG PIPEWIRE_MODERN_VERSION=1.2.7
RUN git clone --depth 1 --branch "${PIPEWIRE_MODERN_VERSION}" https://gitlab.freedesktop.org/pipewire/pipewire.git pipewire-modern \
    && meson setup pipewire-modern/build pipewire-modern --prefix=/opt/pipewire-modern --libdir=lib --buildtype=release \
        -Dauto_features=disabled -Dgstreamer=enabled -Dsession-managers=[] \
        -Dexamples=disabled -Dtests=disabled -Dman=disabled -Ddocs=disabled \
        -Dsystemd-user-service=disabled -Dpipewire-jack=disabled -Dpipewire-v4l2=disabled \
        -Daudiomixer=disabled \
        -Dbluez5-backend-hsp-native=disabled -Dbluez5-backend-hfp-native=disabled \
        -Dbluez5-backend-ofono=disabled -Dbluez5-backend-hsphfpd=disabled \
        -Dcontrol=disabled -Daudiotestsrc=disabled -Ddbus=disabled \
        -Dvideoconvert=disabled -Dvideotestsrc=disabled -Dflatpak=disabled \
    && ninja -C pipewire-modern/build src/gst/libgstpipewire.so \
    && mkdir -p "$PREFIX/lib/gstreamer-1.0-pipewire-modern" \
    && install -m 755 pipewire-modern/build/src/gst/libgstpipewire.so "$PREFIX/lib/gstreamer-1.0-pipewire-modern/"

# dav1ddec, rtpav1pay/rtpav1depay and audiornnoise live in gst-plugins-rs only. audiornnoise (audiofx,
# MPL-2.0) is RNNoise in pure Rust (nnnoiseless): it links nothing beyond GStreamer itself.
RUN git clone --depth 1 --branch "gstreamer-${GST_VERSION}" https://gitlab.freedesktop.org/gstreamer/gst-plugins-rs.git \
    && cd gst-plugins-rs \
    && cargo cinstall --release -p gst-plugin-dav1d -p gst-plugin-rtp -p gst-plugin-audiofx \
        --prefix="$PREFIX" --libdir=lib \
    && rm -rf target /root/.cargo/registry

# Read by collect.sh for THIRD-PARTY-NOTICES.txt
ENV GST_VERSION=${GST_VERSION}

COPY collect.sh verify.sh required-elements.txt /build/
RUN chmod +x /build/*.sh
