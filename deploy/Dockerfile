# Builds the Veyra image. Run from the repository root:
#   docker build -f deploy/Dockerfile -t veyra:local .

# ---- Frontend: build the React app (ADR 0004) with the same Node major version as CI ----
FROM node:24-alpine AS frontend
WORKDIR /frontend

# Dependencies first, so this layer is reused while only source files change.
COPY src/frontend/package.json src/frontend/package-lock.json ./
RUN npm ci

COPY src/frontend/ ./
RUN npm run build

# ---- Build: restore and publish the API with the SDK pinned in global.json ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Build settings that apply to every project.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./

# Backend sources only. Tests, docs, and tooling stay out of the build context (see .dockerignore).
COPY src/backend/ src/backend/

RUN dotnet restore src/backend/Veyra.Api/Veyra.Api.csproj

RUN dotnet publish src/backend/Veyra.Api/Veyra.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app

# ---- Runtime: ASP.NET Core only, running as the image's non-root user ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app ./

# The API serves the built app from wwwroot.
COPY --from=frontend /frontend/dist ./wwwroot

# Plain HTTP inside the container network. TLS is terminated by the reverse proxy (ADR 0008).
# 8080: REST API, UI, and OTLP/HTTP. 4317: OTLP/gRPC, which needs HTTP/2.
# 4318: OTLP/HTTP on its standard port.
ENV Kestrel__Endpoints__Api__Url=http://+:8080 \
    Kestrel__Endpoints__OtlpGrpc__Url=http://+:4317 \
    Kestrel__Endpoints__OtlpGrpc__Protocols=Http2 \
    Kestrel__Endpoints__OtlpHttp__Url=http://+:4318
EXPOSE 8080 4317 4318

# ASP.NET Core encrypts session and antiforgery cookies with data protection keys kept here. The folder is created
# for the unprivileged user, so the Compose volume mounted on it inherits that owner and the keys survive restarts.
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys && chown -R $APP_UID /home/app/.aspnet

# APP_UID is defined by the .NET base images: an unprivileged user created for this purpose.
USER $APP_UID

ENTRYPOINT ["dotnet", "Veyra.Api.dll"]
