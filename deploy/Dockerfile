# Multi-stage build for .NET 10 MCP Server
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy solution and project files
COPY k8s-mcp-server.slnx .
COPY src/K8sMcpServer/K8sMcpServer.csproj src/K8sMcpServer/
COPY src/K8sMcpServer.Aspire/K8sMcpServer.Aspire.csproj src/K8sMcpServer.Aspire/

# Restore dependencies
RUN dotnet restore k8s-mcp-server.slnx

# Copy source and build
COPY src/ src/
RUN dotnet publish src/K8sMcpServer/K8sMcpServer.csproj -c Release -o /app/publish \
    --no-restore \
    -p:PublishAot=true \
    -p:InvariantGlobalization=true

# Runtime stage - distroless for minimal attack surface
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-preview-distroless AS runtime
WORKDIR /app

# Copy published app
COPY --from=build /app/publish .

# Non-root user (distroless uses nonroot:nonroot by default)
USER nonroot:nonroot

# Environment
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_EnableDiagnostics=0

EXPOSE 8080

ENTRYPOINT ["/app/K8sMcpServer"]