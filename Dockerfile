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
RUN apk add ffmpeg
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "RainbowAvatarBot.dll"]

FROM build AS benchmarks
WORKDIR "/src/RainbowAvatarBot.Benchmarks"
RUN apk add ffmpeg
RUN dotnet restore "RainbowAvatarBot.Benchmarks.csproj"
RUN dotnet build "RainbowAvatarBot.Benchmarks.csproj" -c Release
ENTRYPOINT ["dotnet", "run", "-c", "Release", "--no-build"]
