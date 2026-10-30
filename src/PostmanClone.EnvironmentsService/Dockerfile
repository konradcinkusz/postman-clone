# EnvironmentsService image (P6). Built from the repository root, which is the build context:
#   docker build --file src/PostmanClone.EnvironmentsService/Dockerfile .
# Multi-stage: the SDK publishes, the ASP.NET runtime image runs the output as its non-root user.
# The build context excludes tests, docs and .local-stack/ (.dockerignore).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/PostmanClone.EnvironmentsService/PostmanClone.EnvironmentsService.csproj \
      --configuration Release \
      --output /app/publish \
      --property:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
LABEL org.opencontainers.image.title="PostmanClone EnvironmentsService" \
      org.opencontainers.image.source="https://github.com/konradcinkusz/postman-clone" \
      org.opencontainers.image.description="Environments, variables and encrypted secret values for PostmanClone"
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# The aspnet image ships a non-root user named "app" (uid 1654).
USER app
ENTRYPOINT ["dotnet", "PostmanClone.EnvironmentsService.dll"]
