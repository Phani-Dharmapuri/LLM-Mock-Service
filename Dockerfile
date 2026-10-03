# syntax=docker/dockerfile:1

# Build on the host architecture and cross-compile for the target, so multi-arch builds avoid emulating the SDK.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Restore in its own layer so it is cached until a project or package version changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/LlmMockService.Core/LlmMockService.Core.csproj src/LlmMockService.Core/
COPY src/LlmMockService.Server/LlmMockService.Server.csproj src/LlmMockService.Server/
RUN dotnet restore src/LlmMockService.Server/LlmMockService.Server.csproj -a $TARGETARCH

COPY src/ src/
RUN dotnet publish src/LlmMockService.Server/LlmMockService.Server.csproj \
    -c Release -a $TARGETARCH --no-restore -o /app

# Chiseled runtime: no shell or package manager, runs as a non-root user, listens on 8080.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
LABEL org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.source="https://github.com/Phani-Dharmapuri/LLM-Mock-Service"
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "LlmMockService.Server.dll"]
