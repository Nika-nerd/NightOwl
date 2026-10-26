# syntax=docker/dockerfile:1

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Restore отдельным слоем: перевыполняется только при изменении файлов проектов
COPY src/NightOwl.Core/NightOwl.Core.csproj               src/NightOwl.Core/
COPY src/NightOwl.Search/NightOwl.Search.csproj           src/NightOwl.Search/
COPY src/NightOwl.Extraction/NightOwl.Extraction.csproj   src/NightOwl.Extraction/
COPY src/NightOwl.Credibility/NightOwl.Credibility.csproj src/NightOwl.Credibility/
COPY src/NightOwl.AI/NightOwl.AI.csproj                   src/NightOwl.AI/
COPY src/NightOwl.Pipeline/NightOwl.Pipeline.csproj       src/NightOwl.Pipeline/
COPY src/NightOwl.Api/NightOwl.Api.csproj                 src/NightOwl.Api/
RUN dotnet restore src/NightOwl.Api/NightOwl.Api.csproj

COPY src/ src/
RUN dotnet publish src/NightOwl.Api/NightOwl.Api.csproj \
        -c Release -o /app/publish --no-restore \
 && test -f /app/publish/sources.json

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# curl нужен для healthcheck
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Каталог для SQLite-кэша; владелец: непривилегированный пользователь
RUN mkdir /data && chown $APP_UID /data

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    NightOwl__Cache__Path=/data/cache.db

EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "NightOwl.Api.dll"]