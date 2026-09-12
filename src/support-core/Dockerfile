# Build context is the repo root (see compose.yaml) so this stage can also read ../web —
# the built SPA is copied into the api image's wwwroot below.
FROM node:24-alpine AS web-build
WORKDIR /web
COPY src/web/package.json src/web/pnpm-lock.yaml ./
RUN corepack enable && corepack prepare pnpm@10.15.0 --activate \
    && pnpm install --frozen-lockfile
COPY src/web/. .
RUN pnpm build

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:2fa828c68761b1b8c23d7662dc134421b9d3b59fe1425fdbc80804e390cdb24d AS build
WORKDIR /src
COPY src/support-core/. .
RUN dotnet restore TenderHack.sln
# BuildWebSpa=false: the web-build stage above already produced the SPA; this stage has no node.
RUN dotnet publish src/TenderHack.Api/TenderHack.Api.csproj -c Release -o /out/api --no-restore -p:BuildWebSpa=false \
    && dotnet publish src/TenderHack.Worker/TenderHack.Worker.csproj -c Release -o /out/worker --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:6a94333d37514e385650a3c81a55e5350b67253dbe136e9cf17e499c35606a8c AS api
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /out/api .
# Built SPA lands at wwwroot's root; the pre-existing dev console lives under wwwroot/dev-console/
# (see Program.cs) and is unaffected since it's a different subpath.
COPY --from=web-build /web/dist ./wwwroot
EXPOSE 8080
ENTRYPOINT ["dotnet", "TenderHack.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:6a94333d37514e385650a3c81a55e5350b67253dbe136e9cf17e499c35606a8c AS worker
WORKDIR /app
COPY --from=build /out/worker .
ENTRYPOINT ["dotnet", "TenderHack.Worker.dll"]
