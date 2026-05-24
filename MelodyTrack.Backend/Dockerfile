FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["global.json", "."]
COPY ["MelodyTrack.slnx", "."]
COPY ["MelodyTrack.Backend/MelodyTrack.Backend.csproj", "MelodyTrack.Backend/"]
RUN dotnet restore "MelodyTrack.Backend/MelodyTrack.Backend.csproj"
COPY ["MelodyTrack.Backend/", "MelodyTrack.Backend/"]
WORKDIR "/src/MelodyTrack.Backend"
RUN dotnet publish "./MelodyTrack.Backend.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MelodyTrack.Backend.dll"]
