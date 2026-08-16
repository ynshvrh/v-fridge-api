# Multi-stage build for the V-Fridge API.
# Render builds straight from this file when the service runtime is set to Docker.

# --- Build stage ---------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first as its own layer so source edits don't bust the package cache.
COPY src/VFridge.Api/VFridge.Api.csproj src/VFridge.Api/
RUN dotnet restore src/VFridge.Api/VFridge.Api.csproj

# Now bring in the rest of the source and publish. We let publish re-resolve packages
# instead of --no-restore — the restore-once optimisation is fragile when the source
# COPY brings in stray obj/ folders, and Docker layer caching makes the cost negligible.
COPY src/ src/
RUN dotnet publish src/VFridge.Api/VFridge.Api.csproj \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

# --- Runtime stage -------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Render injects PORT (usually 10000) but ASPNETCORE_URLS is the variable Kestrel reads.
# The .env / Render env vars can override this — the default just keeps Kestrel listening
# on a sensible interface if nothing is configured.
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
ENV DOTNET_USE_POLLING_FILE_WATCHER=true
EXPOSE 10000

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "VFridge.Api.dll"]
