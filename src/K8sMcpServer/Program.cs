using K8sMcpServer.Middleware;
using K8sMcpServer.Services;
using K8sMcpServer.Tools;
using ModelContextProtocol.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"));

// Configuration
builder.Services.Configure<KubernetesOptions>(builder.Configuration.GetSection("Kubernetes"));
builder.Services.Configure<PrometheusOptions>(builder.Configuration.GetSection("Prometheus"));
builder.Services.Configure<LokiOptions>(builder.Configuration.GetSection("Loki"));
builder.Services.Configure<McpOptions>(builder.Configuration.GetSection("Mcp"));

// Services
builder.Services.AddSingleton<IKubernetesService, KubernetesService>();
builder.Services.AddSingleton<IPrometheusService, PrometheusService>();
builder.Services.AddSingleton<ILokiService, LokiService>();

// MCP Server with HTTP transport (v2.0 stateless-first)
builder.Services.AddMcpServer(options =>
{
    options.ServerInfo = new()
    {
        Name = builder.Configuration["Mcp:ServerName"] ?? "k8s-mcp-server",
        Version = builder.Configuration["Mcp:Version"] ?? "1.0.0"
    };
})
.WithHttpTransport()
.WithToolsFromAssembly(typeof(Program).Assembly);

// HTTP client resilience
builder.Services.AddHttpClient<IPrometheusService, PrometheusService>()
    .AddStandardResilienceHandler(o =>
    {
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
        o.Retry.MaxRetryAttempts = 3;
    });

builder.Services.AddHttpClient<ILokiService, LokiService>()
    .AddStandardResilienceHandler(o =>
    {
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
        o.Retry.MaxRetryAttempts = 3;
    });

var app = builder.Build();

// Middleware pipeline
if (app.Configuration.GetValue<bool>("Mcp:RequireAuth"))
{
    app.UseMiddleware<McpAuthMiddleware>();
}

app.UseMiddleware<McpRequestLoggingMiddleware>();

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));

// MCP endpoint
app.MapMcp();

app.Run();

// Options classes
public sealed class KubernetesOptions
{
    public bool UseInClusterConfig { get; set; } = true;
    public string? KubeConfigPath { get; set; }
}

public sealed class PrometheusOptions
{
    public string Url { get; set; } = "http://prometheus:9090";
    public int QueryTimeoutSeconds { get; set; } = 30;
}

public sealed class LokiOptions
{
    public string Url { get; set; } = "http://loki:3100";
    public int QueryTimeoutSeconds { get; set; } = 30;
}

public sealed class McpOptions
{
    public string ServerName { get; set; } = "k8s-mcp-server";
    public string Version { get; set; } = "1.0.0";
    public bool RequireAuth { get; set; } = false;
}