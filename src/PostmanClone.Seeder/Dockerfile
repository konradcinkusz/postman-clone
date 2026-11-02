# The seeder image (ADR-0030). A one-shot job: it runs once per `compose up`, creates the demo data through the APIs and exits.
# It is development tooling and is never deployed. Build context: the repository root.
#   docker build --file src/PostmanClone.Seeder/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/PostmanClone.Seeder/ src/PostmanClone.Seeder/
RUN dotnet publish src/PostmanClone.Seeder/PostmanClone.Seeder.csproj --configuration Release --output /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
# The image's built-in non-root user.
USER app
ENTRYPOINT ["dotnet", "PostmanClone.Seeder.dll"]
