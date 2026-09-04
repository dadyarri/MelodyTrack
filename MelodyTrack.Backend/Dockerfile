FROM node:26-alpine AS frontend-build
ARG VITE_API_BASE_URL=/api
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL
WORKDIR /src
COPY . .
WORKDIR /src/MelodyTrack.Web
RUN --mount=type=cache,target=/root/.npm npm ci
RUN npm run build

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
ENV ASPNETCORE_URLS="http://+:8080;http://+:8081"
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0.400 AS publish
ARG BUILD_CONFIGURATION=Release
ARG RELEASE_VERSION=dev
WORKDIR /src
COPY . .
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore "MelodyTrack.Backend/MelodyTrack.Backend.csproj" && \
    dotnet restore "MelodyTrack.Init/MelodyTrack.Init.csproj"
COPY --from=frontend-build /src/MelodyTrack.Web/dist MelodyTrack.Web/dist
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish "MelodyTrack.Backend/MelodyTrack.Backend.csproj" -c $BUILD_CONFIGURATION -o /app/publish/backend --no-restore /p:UseAppHost=false /p:InformationalVersion=$RELEASE_VERSION /p:SkipFrontendBuild=true /p:SkipApiContractGeneration=true && \
    dotnet publish "MelodyTrack.Init/MelodyTrack.Init.csproj" -c $BUILD_CONFIGURATION -o /app/publish/init --no-restore /p:UseAppHost=false /p:InformationalVersion=$RELEASE_VERSION

FROM base AS final
ARG RELEASE_VERSION=dev
LABEL org.opencontainers.image.version=$RELEASE_VERSION
WORKDIR /app
COPY --from=publish /app/publish/ ./
COPY --from=publish /src/MelodyTrack.Backend/docker-entrypoint.sh ./docker-entrypoint.sh
COPY --chmod=755 --from=publish /src/MelodyTrack.Backend/melodytrack /usr/local/bin/melodytrack
WORKDIR /app/backend
ENTRYPOINT ["sh", "/app/docker-entrypoint.sh"]
