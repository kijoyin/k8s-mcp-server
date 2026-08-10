using K8sMcpServer.Services;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace K8sMcpServer.Services;

public sealed class LokiService : ILokiService
{
    private readonly HttpClient _http;
    private readonly LokiOptions _options;
    private readonly ILogger<LokiService> _logger;

    public LokiService(HttpClient http, IOptions<LokiOptions> options, ILogger<LokiService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _http.BaseAddress = new Uri(_options.Url);
    }

    public async Task<LokiQueryResult> QueryRangeAsync(string query, string range, int limit, CancellationToken ct = default)
    {
        var end = DateTimeOffset.UtcNow;
        var start = end - ParseDuration(range);

        var url = $"/loki/api/v1/query_range?query={Uri.EscapeDataString(query)}" +
                  $"&start={start.ToUnixTimeSeconds() * 1_000_000_000}" +
                  $"&end={end.ToUnixTimeSeconds() * 1_000_000_000}" +
                  $"&limit={limit}";

        try
        {
            var response = await _http.GetFromJsonAsync<LokiApiResponse>(url, ct);

            return new LokiQueryResult
            {
                Status = response?.Status ?? "error",
                Data = MapLokiData(response?.Data)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loki query failed: {Url}", url);
            return new LokiQueryResult
            {
                Status = "error",
                Data = new LokiData()
            };
        }
    }

    private static LokiData MapLokiData(LokiApiData? apiData)
    {
        if (apiData == null) return new LokiData();
        
        return new LokiData
        {
            ResultType = apiData.ResultType,
            Result = apiData.Result?.Select(s => new LokiStream
            {
                Stream = s.Stream,
                Values = s.Values?.Select(v => new LokiValue
                {
                    Timestamp = v.Timestamp,
                    Line = v.Line
                }).ToList() ?? []
            }).ToList() ?? []
        };
    }

    private static TimeSpan ParseDuration(string duration)
    {
        if (duration.EndsWith("h")) return TimeSpan.FromHours(int.Parse(duration[..^1]));
        if (duration.EndsWith("m")) return TimeSpan.FromMinutes(int.Parse(duration[..^1]));
        if (duration.EndsWith("s")) return TimeSpan.FromSeconds(int.Parse(duration[..^1]));
        if (duration.EndsWith("d")) return TimeSpan.FromDays(int.Parse(duration[..^1]));
        return TimeSpan.FromHours(1);
    }
}

// Loki API response types (internal - for deserialization only)
internal sealed class LokiApiResponse
{
    public string Status { get; set; } = "";
    public LokiApiData Data { get; set; } = new();
}

internal sealed class LokiApiData
{
    public string ResultType { get; set; } = "";
    public List<LokiApiStream> Result { get; set; } = [];
}

internal sealed class LokiApiStream
{
    public Dictionary<string, string> Stream { get; set; } = [];
    public List<LokiApiValue> Values { get; set; } = [];
}

internal sealed class LokiApiValue
{
    public string Timestamp { get; set; } = "";
    public string Line { get; set; } = "";
}