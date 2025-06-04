# Based on https://github.com/alfg/docker-ffmpeg
FROM alpine:3.21.0 AS ffmpeg-build
ARG FFMPEG_VERSION=7.1.1
ARG PREFIX=/opt/ffmpeg
ARG LD_LIBRARY_PATH=/opt/ffmpeg/lib
ARG MAKEFLAGS="-j4"

# FFmpeg build dependencies.
RUN apk add --update \
  build-base \
  coreutils \
  gcc \
  libvpx-dev \
  pkgconf \
  pkgconfig \
  yasm \
  zlib-dev

RUN cd /tmp/ && \
  wget https://ffmpeg.org/releases/ffmpeg-${FFMPEG_VERSION}.tar.gz && \
  tar zxf ffmpeg-${FFMPEG_VERSION}.tar.gz && rm ffmpeg-${FFMPEG_VERSION}.tar.gz

RUN cd /tmp/ffmpeg-${FFMPEG_VERSION} && \
  ./configure \
  --enable-version3 \
  --enable-gpl \
  --disable-everything \
  --enable-libvpx \
  --enable-zlib \
  --enable-decoder=libvpx_vp9,png \
  --enable-encoder=libvpx_vp9 \
  --enable-demuxer=matroska,image2 \
  --enable-muxer=matroska,webm \
  --enable-parser=vp9,png \
  --enable-protocol=file,unix \
  --enable-filter=split,alphaextract,scale,blend,alphamerge \
  --disable-debug \
  --disable-doc \
  --disable-ffplay \
  --disable-ffprobe \
  --extra-cflags="-I${PREFIX}/include" \
  --extra-ldflags="-L${PREFIX}/lib" \
  --extra-libs="-lpthread -lm" \
  --prefix="${PREFIX}" && \
  make && make install && make distclean

FROM mcr.microsoft.com/dotnet/runtime:9.0-alpine AS base
WORKDIR /app
RUN apk add --no-cache icu-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src
COPY ["Directory.Build.props", "."]
COPY ["RainbowAvatarBot/RainbowAvatarBot.csproj", "RainbowAvatarBot/"]
RUN dotnet restore "RainbowAvatarBot/RainbowAvatarBot.csproj"
COPY . .
WORKDIR "/src/RainbowAvatarBot"
RUN dotnet build "RainbowAvatarBot.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "RainbowAvatarBot.csproj" -c Release -r linux-x64 --self-contained false -o /app/publish

FROM base AS final
WORKDIR /app
RUN apk add --update libvpx
COPY --from=publish /app/publish .
COPY --from=ffmpeg-build /opt/ffmpeg/bin/ffmpeg /usr/local/bin/
ENTRYPOINT ["dotnet", "RainbowAvatarBot.dll"]

FROM build AS benchmarks
WORKDIR "/src/RainbowAvatarBot.Benchmarks"
COPY --from=ffmpeg-build /opt/ffmpeg/bin/ffmpeg /usr/local/bin/
RUN dotnet restore "RainbowAvatarBot.Benchmarks.csproj"
RUN dotnet build "RainbowAvatarBot.Benchmarks.csproj" -c Release
ENTRYPOINT ["dotnet", "run", "-c", "Release", "--no-build"]
