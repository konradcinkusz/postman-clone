# CollectionsService image (architecture-standards P6). Build from the repository root, so the build context holds
# only what the service needs: the solution-wide props and the SDK pin, the kernel and the service itself.
#   docker build --file src/PostmanClone.CollectionsService/Dockerfile .
# Multi-stage: the SDK image builds and publishes, the ASP.NET runtime image runs the result as a non-root user.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files and the solution-wide settings are copied and restored first, so a source change does not
# invalidate the restore layer (P6).
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/PostmanClone.ServiceDefaults/PostmanClone.ServiceDefaults.csproj src/PostmanClone.ServiceDefaults/
COPY src/PostmanClone.CollectionsService/PostmanClone.CollectionsService.csproj src/PostmanClone.CollectionsService/
RUN dotnet restore src/PostmanClone.CollectionsService/PostmanClone.CollectionsService.csproj

COPY src/PostmanClone.ServiceDefaults/ src/PostmanClone.ServiceDefaults/
COPY src/PostmanClone.CollectionsService/ src/PostmanClone.CollectionsService/
RUN dotnet publish src/PostmanClone.CollectionsService/PostmanClone.CollectionsService.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The service listens on plain HTTP 8080; the platform terminates TLS in front of it (ADR-0007).
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_NOLOGO=1

COPY --from=build /app/publish ./

# The image's built-in non-root user (uid 1654). Never run the service as root.
USER app
EXPOSE 8080

LABEL org.opencontainers.image.title="PostmanClone CollectionsService" \
      org.opencontainers.image.description="Collections, folders and saved requests of a workspace." \
      org.opencontainers.image.source="https://github.com/konradcinkusz/postman-clone"

ENTRYPOINT ["dotnet", "PostmanClone.CollectionsService.dll"]
