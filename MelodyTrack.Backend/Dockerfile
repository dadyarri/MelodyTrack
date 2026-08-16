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
COPY ["MelodyTrack.Backend/MelodyTrack.Backend.csproj", "MelodyTrack.Backend/"]
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore "MelodyTrack.Backend/MelodyTrack.Backend.csproj"
COPY ["changelog/", "changelog/"]
COPY ["MelodyTrack.Backend/", "MelodyTrack.Backend/"]
WORKDIR "/src/MelodyTrack.Backend"
RUN dotnet publish "./MelodyTrack.Backend.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-restore /p:UseAppHost=false /p:InformationalVersion=$RELEASE_VERSION

FROM base AS final
ARG RELEASE_VERSION=dev
LABEL org.opencontainers.image.version=$RELEASE_VERSION
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MelodyTrack.Backend.dll"]
