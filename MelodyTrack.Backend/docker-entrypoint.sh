#!/bin/sh
set -eu

dotnet /app/init/MelodyTrack.Init.dll --mode production
exec dotnet /app/backend/MelodyTrack.Backend.dll
