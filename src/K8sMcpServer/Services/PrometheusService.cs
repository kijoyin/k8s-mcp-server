using K8sMcpServer.Services;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace K8sMcpServer.Services;

public sealed class PrometheusService : IPrometheusService
{
    private readonly HttpClient _http;
    private readonly PrometheusOptions _options;
    private readonly ILogger<PrometheusService> _logger;

    public PrometheusService(HttpClient http, IOptions<PrometheusOptions> options, ILogger<PrometheusService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _http.BaseAddress = new Uri(_options.Url);
    }

    public async Task<PrometheusQueryResult> QueryAsync(string query, CancellationToken ct = default)
    {
        var url = $"/api/v1/query?query={Uri.EscapeDataString(query)}";
        return await ExecuteQueryAsync(url, ct);
    }

    public async Task<PrometheusQueryResult> QueryRangeAsync(string query, string range, string step, CancellationToken ct = default)
    {
        var end = DateTimeOffset.UtcNow;
        var start = end - ParseDuration(range);
        
        var url = $"/api/v1/query_range?query={Uri.EscapeDataString(query)}" +
                  $"&start={start.ToUnixTimeSeconds()}" +
                  $"&end={end.ToUnixTimeSeconds()}" +
                  $"&step={Uri.EscapeDataString(step)}";
        
        return await ExecuteQueryAsync(url, ct);
    }

    private async Task<PrometheusQueryResult> ExecuteQueryAsync(string url, CancellationToken ct)
    {
        try
        {
            var response = await _http.GetFromJsonAsync<PrometheusApiResponse>(url, ct);
            
            return new PrometheusQueryResult
            {
                Status = response?.Status ?? "error",
                Data = response?.Data ?? new PrometheusData(),
                Error = response?.Error
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Prometheus query failed: {Url}", url);
            return new PrometheusQueryResult
            {
                Status = "error",
                Error = ex.Message
            };
        }
    }

    private static TimeSpan ParseDuration(string duration)
    {
        // Parse "1h", "30m", "1d", etc.
        if (duration.EndsWith("h")) return TimeSpan.FromHours(int.Parse(duration[..^1]));
        if (duration.EndsWith("m")) return TimeSpan.FromMinutes(int.Parse(duration[..^1]));
        if (duration.EndsWith("s")) return TimeSpan.FromSeconds(int.Parse(duration[..^1]));
        if (duration.EndsWith("d")) return TimeSpan.FromDays(int.Parse(duration[..^1]));
        return TimeSpan.FromHours(1);
    }
}

// Prometheus API response types
internal sealed class PrometheusApiResponse
{
    public string Status { get; set; } = "";
    public PrometheusData Data { get; set; } = new();
    public string? Error { get; set; }
}