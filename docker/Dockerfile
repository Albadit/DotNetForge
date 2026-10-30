# Production image for DotNetForge CMS (.docs/guides/deployment.md). Build from the repository root:
#   docker build -f docker/Dockerfile -t dotnetforge .
# The image runs as a non-root user and works with a read-only root filesystem:
#   docker run --read-only --tmpfs /tmp ...
# docker/Dockerfile.dockerignore (next to this file) limits the build context.
# Configuration (database, object storage) comes from environment variables - never baked into the image.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
# Extensions with code are compiled into their own bin/ folders first; the publish below copies extensions/ as files
# (.docs/features/extensions.md#deployment).
RUN set -e; for project in $(find extensions -name '*.csproj'); do dotnet build "$project" --configuration Release; done
# Only this platform's native libraries, not those of every operating system.
RUN dotnet publish src/DotNetForge.Web/DotNetForge.Web.csproj --configuration Release --output /app --use-current-runtime --self-contained false /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Optional ffmpeg for video compression (.docs/features/media-storage.md#optimization), about 200 MB more:
#   docker build -f docker/Dockerfile --build-arg WITH_FFMPEG=true -t dotnetforge .
# Without it FFMPEG_PATH stays empty and videos are stored as uploaded.
ARG WITH_FFMPEG=
RUN if [ -n "$WITH_FFMPEG" ]; then \
        apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*; \
    fi
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    FFMPEG_PATH=${WITH_FFMPEG:+/usr/bin/ffmpeg}
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "DotNetForge.Web.dll"]
