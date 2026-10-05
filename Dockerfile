# Production image for DotNetForge CMS (.docs/guides/deployment.md).
# The image runs as a non-root user and works with a read-only root filesystem:
#   docker run --read-only --tmpfs /tmp ...
# Configuration (database, object storage) comes from environment variables - never baked into the image.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish DotNetForge.Web.csproj --configuration Release --output /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "DotNetForge.Web.dll"]
