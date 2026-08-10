using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace K8sMcpServer.Middleware;

public sealed class McpRequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<McpRequestLoggingMiddleware> _logger;

    public McpRequestLoggingMiddleware(RequestDelegate next, ILogger<McpRequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N")[..8];
        context.Items["CorrelationId"] = correlationId;

        var start = Stopwatch.GetTimestamp();
        var method = context.Request.Method;
        var path = context.Request.Path;

        _logger.LogInformation("MCP Request started {CorrelationId} {Method} {Path}", correlationId, method, path);

        try
        {
            await _next(context);
        }
        finally
        {
            var elapsedMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            _logger.LogInformation("MCP Request completed {CorrelationId} {Method} {Path} {StatusCode} in {ElapsedMs:F1}ms",
                correlationId, method, path, context.Response.StatusCode, elapsedMs);
        }
    }
}

public sealed class McpAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<McpAuthMiddleware> _logger;
    private readonly string? _expectedApiKey;

    public McpAuthMiddleware(RequestDelegate next, ILogger<McpAuthMiddleware> logger, IOptions<McpAuthOptions> options)
    {
        _next = next;
        _logger = logger;
        _expectedApiKey = options.Value.ApiKey;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip auth for health checks
        if (context.Request.Path == "/health")
        {
            await _next(context);
            return;
        }

        // Check for API key in header
        var apiKey = context.Request.Headers["X-MCP-API-Key"].FirstOrDefault();
        
        if (string.IsNullOrEmpty(_expectedApiKey))
        {
            // No API key configured - allow (dev mode)
            await _next(context);
            return;
        }

        if (string.IsNullOrEmpty(apiKey) || apiKey != _expectedApiKey)
        {
            _logger.LogWarning("MCP Unauthorized request from {RemoteIp} to {Path}", 
                context.Connection.RemoteIpAddress, context.Request.Path);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized", message = "Valid X-MCP-API-Key header required" });
            return;
        }

        await _next(context);
    }
}

public sealed class McpAuthOptions
{
    public string? ApiKey { get; set; }
}