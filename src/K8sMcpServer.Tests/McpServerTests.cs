using K8sMcpServer.Services;
using K8sMcpServer.Tools;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using FluentAssertions;

namespace K8sMcpServer.Tests;

public class McpServerTests : IClassFixture<McpServerFixture>
{
    private readonly McpServerFixture _fixture;

    public McpServerTests(McpServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ClusterHealthTool_ReturnsHealthResult()
    {
        var client = _fixture.CreateClient();
        var response = await client.CallToolAsync("cluster_health", new { });
        response.Should().NotBeNull();
        response.Content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CheckDiskPressureTool_ReturnsPrometheusResult()
    {
        var client = _fixture.CreateClient();
        var response = await client.CallToolAsync("check_disk_pressure", new { threshold = 0.15 });
        response.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckOomKillsTool_ReturnsPrometheusResult()
    {
        var client = _fixture.CreateClient();
        var response = await client.CallToolAsync("check_oom_kills", new { range = "1h" });
        response.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckPodRestartsTool_ReturnsPrometheusResult()
    {
        var client = _fixture.CreateClient();
        var response = await client.CallToolAsync("check_pod_restarts", new { range = "1h", threshold = 3 });
        response.Should().NotBeNull();
    }

    [Fact]
    public async Task QueryMetricsTool_InstantQuery_ReturnsResult()
    {
        var client = _fixture.CreateClient();
        var response = await client.CallToolAsync("query_metrics", new { query = "up" });
        response.Should().NotBeNull();
    }

    [Fact]
    public async Task QueryMetricsTool_RangeQuery_ReturnsResult()
    {
        var client = _fixture.CreateClient();
        var response = await client.CallToolAsync("query_metrics", new { query = "up", range = "1h", step = "1m" });
        response.Should().NotBeNull();
    }

    [Fact]
    public async Task QueryErrorLogsTool_ReturnsLokiResult()
    {
        var client = _fixture.CreateClient();
        var response = await client.CallToolAsync("query_error_logs", new { range = "1h", limit = 10 });
        response.Should().NotBeNull();
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsHealthy()
    {
        var client = _fixture.CreateHttpClient();
        var response = await client.GetAsync("/health");
        response.IsSuccessStatusCode.Should().BeTrue();
    }
}

public class McpServerFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Override configuration for tests
                });
            });
    }

    public HttpClient CreateHttpClient() => _factory!.CreateClient();

    public McpTestClient CreateClient() => new McpTestClient(_factory!.CreateClient());

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await Task.CompletedTask;
    }
}

public class McpTestClient
{
    private readonly HttpClient _http;

    public McpTestClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<McpToolResponse> CallToolAsync(string toolName, object toolArguments)
    {
        // Initialize MCP session
        var initResponse = await _http.PostAsJsonAsync("/mcp", new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            parameters = new
            {
                protocolVersion = "2025-06-18",
                capabilities = new { }
            }
        });

        var sessionId = initResponse.Headers.GetValues("Mcp-Session-Id").FirstOrDefault();

        // Call tool
        var request = new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/call",
            parameters = new { name = toolName, arguments = toolArguments }
        };

        var headers = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(sessionId))
            headers["Mcp-Session-Id"] = sessionId;

        var response = await _http.PostAsJsonAsync("/mcp", request);
        
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var result = json.GetProperty("result");
        
        return new McpToolResponse
        {
            Content = result.GetRawText()
        };
    }
}

public class McpToolResponse
{
    public string Content { get; set; } = "";
}