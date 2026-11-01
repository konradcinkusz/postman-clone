# syntax=docker/dockerfile:1
#
# The ExecutionService image (P6). Built with the repository root as the context (.dockerignore excludes tests,
# docs and build output), so the build copies the kernel and this service and nothing else. CI builds it without
# pushing (docker-build); deployment is out of scope (ADR-0007).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Build settings and the central package versions first, so the restore layer is reused when only code changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/PostmanClone.ServiceDefaults/ src/PostmanClone.ServiceDefaults/
COPY src/PostmanClone.Sending/ src/PostmanClone.Sending/
COPY src/PostmanClone.ExecutionService/ src/PostmanClone.ExecutionService/
RUN dotnet restore src/PostmanClone.ExecutionService/PostmanClone.ExecutionService.csproj
RUN dotnet publish src/PostmanClone.ExecutionService/PostmanClone.ExecutionService.csproj \
      --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
# The aspnet image ships the non-root user "app" (uid 1654); the process never runs as root.
USER app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .

LABEL org.opencontainers.image.title="PostmanClone ExecutionService" \
      org.opencontainers.image.source="https://github.com/konradcinkusz/postman-clone" \
      org.opencontainers.image.description="Sends API requests server-side with SSRF protection and keeps the history."

ENTRYPOINT ["dotnet", "PostmanClone.ExecutionService.dll"]
