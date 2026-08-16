FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
ARG BUILD_CONFIGURATION=Release
ARG RELEASE_VERSION=dev
WORKDIR /src
COPY ["global.json", "."]
COPY ["MelodyTrack.slnx", "."]
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["MelodyTrack.Core/MelodyTrack.Core.csproj", "MelodyTrack.Core/"]
COPY ["MelodyTrack.Data/MelodyTrack.Data.csproj", "MelodyTrack.Data/"]
COPY ["MelodyTrack.Init/MelodyTrack.Init.csproj", "MelodyTrack.Init/"]
COPY ["MelodyTrack.Backend/MelodyTrack.Backend.csproj", "MelodyTrack.Backend/"]
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore "MelodyTrack.Backend/MelodyTrack.Backend.csproj" && \
    dotnet restore "MelodyTrack.Init/MelodyTrack.Init.csproj"
COPY ["changelog/", "changelog/"]
COPY ["MelodyTrack.Core/", "MelodyTrack.Core/"]
COPY ["MelodyTrack.Data/", "MelodyTrack.Data/"]
COPY ["MelodyTrack.Init/", "MelodyTrack.Init/"]
COPY ["MelodyTrack.Backend/", "MelodyTrack.Backend/"]
RUN dotnet publish "MelodyTrack.Backend/MelodyTrack.Backend.csproj" -c $BUILD_CONFIGURATION -o /app/publish/backend --no-restore /p:UseAppHost=false /p:InformationalVersion=$RELEASE_VERSION && \
    dotnet publish "MelodyTrack.Init/MelodyTrack.Init.csproj" -c $BUILD_CONFIGURATION -o /app/publish/init --no-restore /p:UseAppHost=false /p:InformationalVersion=$RELEASE_VERSION

FROM base AS final
ARG RELEASE_VERSION=dev
LABEL org.opencontainers.image.version=$RELEASE_VERSION
WORKDIR /app
COPY --from=publish /app/publish/backend ./backend
COPY --from=publish /app/publish/init ./init
COPY ["MelodyTrack.Backend/docker-entrypoint.sh", "/app/docker-entrypoint.sh"]
WORKDIR /app/backend
ENTRYPOINT ["sh", "/app/docker-entrypoint.sh"]
