# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend/src/OetLearner.Api/OetLearner.Api.csproj backend/src/OetLearner.Api/
RUN dotnet restore backend/src/OetLearner.Api/OetLearner.Api.csproj

COPY backend/src/OetLearner.Api/ backend/src/OetLearner.Api/
# Rulebooks embed as resources via ..\..\..\rulebooks\**\*.json — must be present
# in the Docker build context at the repo-root path the .csproj expects.
COPY rulebooks/ rulebooks/
# Scripts referenced by .csproj (e.g. StripeProductSeeder/catalog.json)
COPY backend/scripts/ backend/scripts/
# Remove restore-created obj/bin so dotnet publish doesn't silently skip due to
# Docker BuildKit setting COPY timestamps to epoch (1970), which appears older
# than the just-restored obj/ artifacts and triggers the .NET 10 up-to-date check.
# Also touch all .cs files to force-update timestamps past the restored obj/ cache.
RUN rm -rf backend/src/OetLearner.Api/obj backend/src/OetLearner.Api/bin \
    && find backend/src/OetLearner.Api -name "*.cs" -exec touch {} + \
    && mkdir -p /app/publish \
    && dotnet publish backend/src/OetLearner.Api/OetLearner.Api.csproj \
       -c Release -o /app/publish /p:UseAppHost=false \
    && test "$(ls -A /app/publish | wc -l)" -gt 0 \
       || (echo "ERROR: dotnet publish wrote no output — possible OOM kill" && exit 1)

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
