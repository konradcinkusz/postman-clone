# The Web image, built by CI (pr-validation docker-build, no push). Build context: the repository root, because the
# project references ServiceDefaults and the central package and SDK files live at the root. Deployment is out of
# scope (ADR-0007): nothing here pushes, tags or deploys an image.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/PostmanClone.ServiceDefaults/ src/PostmanClone.ServiceDefaults/
COPY src/PostmanClone.Web/ src/PostmanClone.Web/
RUN dotnet publish src/PostmanClone.Web/PostmanClone.Web.csproj --configuration Release --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
# The image's non-root user, not root.
USER app
ENTRYPOINT ["dotnet", "PostmanClone.Web.dll"]
