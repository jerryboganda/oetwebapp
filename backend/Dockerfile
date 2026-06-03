# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend/src/OetLearner.Api/OetLearner.Api.csproj backend/src/OetLearner.Api/
RUN dotnet restore backend/src/OetLearner.Api/OetLearner.Api.csproj

COPY backend/src/OetLearner.Api/ backend/src/OetLearner.Api/
# Touch all source files so their mtime is newer than the obj/ metadata
# created by the earlier `dotnet restore` step. Without this, .NET SDK
# 10.0.300's incremental-build check sees "sources older than obj/" and
# silently skips compilation (exits 0, no output files produced).
RUN find backend/src/OetLearner.Api -name "*.cs" -o -name "*.json" | xargs touch
# Rulebooks embed as resources via ..\..\..\rulebooks\**\*.json — must be present
# in the Docker build context at the repo-root path the .csproj expects.
COPY rulebooks/ rulebooks/
# Scripts referenced by .csproj (e.g. StripeProductSeeder/catalog.json)
COPY backend/scripts/ backend/scripts/
RUN dotnet publish backend/src/OetLearner.Api/OetLearner.Api.csproj \
        -c Release \
        -o /app/publish \
        /p:UseAppHost=false \
    && ls /app/publish/OetLearner.Api.dll

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
# - curl: required for the container HEALTHCHECK
# - tesseract-ocr / tesseract-ocr-eng: native binaries + English trained data
#   for the Tesseract NuGet wrapper used by WritingOcrService. The trained
#   data installs to /usr/share/tesseract-ocr/5/tessdata (Debian package
#   default), which matches WritingV2Options.TessdataPath's fallback.
# - libleptonica-dev: provides libleptonica (image processing primitives
#   that libtesseract depends on at runtime).
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        curl \
        tesseract-ocr \
        tesseract-ocr-eng \
        libleptonica-dev \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

RUN groupadd --gid 10001 appgroup \
    && useradd --uid 10001 --gid appgroup --create-home --home-dir /home/appuser appuser \
    && mkdir -p /var/opt/oet-learner/storage \
    && chown -R appuser:appgroup /app /var/opt/oet-learner /home/appuser

COPY --from=build /app/publish ./

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=5 \
  CMD curl --fail http://127.0.0.1:8080/health/ready || exit 1

USER appuser

ENTRYPOINT ["dotnet", "OetLearner.Api.dll"]
