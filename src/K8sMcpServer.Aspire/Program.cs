using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Prometheus container
var prometheus = builder.AddContainer("prometheus", "prom/prometheus:v2.54.0")
    .WithHttpEndpoint(port: 9090, targetPort: 9090, name: "http")
    .WithVolume("prometheus-data", "/prometheus")
    .WithBindMount("../deploy/docker/prometheus.yml", "/etc/prometheus/prometheus.yml")
    .WithArgs("--config.file=/etc/prometheus/prometheus.yml", "--storage.tsdb.path=/prometheus");

// Loki container
var loki = builder.AddContainer("loki", "grafana/loki:2.9.0")
    .WithHttpEndpoint(port: 3100, targetPort: 3100, name: "http")
    .WithVolume("loki-data", "/loki")
    .WithBindMount("../deploy/docker/loki-config.yaml", "/etc/loki/local-config.yaml")
    .WithArgs("-config.file=/etc/loki/local-config.yaml");

// MCP Server project
var mcpServer = builder.AddProject("k8s-mcp-server", "../K8sMcpServer/K8sMcpServer.csproj")
    .WithEnvironment("Prometheus__Url", prometheus.GetEndpoint("http"))
    .WithEnvironment("Loki__Url", loki.GetEndpoint("http"))
    .WithEnvironment("Mcp__RequireAuth", "false")
    .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "http")
    .WaitFor(prometheus)
    .WaitFor(loki);

builder.Build().Run();