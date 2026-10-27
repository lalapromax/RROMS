using System.Text;
using System.Text.Json;

namespace RROMS.Services;

/// One dish the AI is allowed to choose from. Built by <see cref="RecommendationService"/> AFTER the
/// dietary filter, so the model never even sees a dish the customer can't eat.
public record AiCandidate(int Id, string Name, string Category, string? Description, decimal Price, bool IsVegetarian, bool IsSpicy, double? AverageRating);

public record AiPick(int Id, string? Reason);

public interface ILocalAiRecommender
{
    /// True only when the admin has switched local AI on in configuration.
    bool IsEnabled { get; }
    string ModelName { get; }

    /// 
    /// Asks the local model to choose and explain up to <paramref name="count"/> dishes from the candidate list.
    /// Returns null on ANY problem (server not running, slow, bad output) so the caller falls back to the rule-based list.
    /// 
    Task<List<AiPick>?> RecommendAsync(string tasteSummary, IReadOnlyList<AiCandidate> candidates, int count, CancellationToken cancellationToken = default);
}

/// 
/// Optional "You might like" ranking by a model running on this machine through Ollama (https://ollama.com),
/// so no paid external API is involved and nothing leaves the computer.
///
/// OFF by default. When it is off - or Ollama isn't running, or answers too slowly or badly - the menu uses the
/// plain rule-based recommender and labels it as such. The rule-based list is never described as AI.
///
/// Safety design: the model is only a *ranker* over candidates that have already passed the allergy/diet filter.
/// Its answer is then checked: any id that wasn't in the candidate list is thrown away, so even a confused or
/// manipulated model cannot add a dish that was filtered out. The customer's allergy text is never sent to it.
///
/// Configuration (appsettings.json / user-secrets):
///   RecommendationSettings:LocalAi:Enabled        true to turn on
///   RecommendationSettings:LocalAi:BaseUrl        default http://localhost:11434
///   RecommendationSettings:LocalAi:Model          a model you have pulled, for example "ollama pull llama3.2:3b"
///   RecommendationSettings:LocalAi:TimeoutSeconds default 8
/// 
public class OllamaRecommender : ILocalAiRecommender
{
    private const string Section = "RecommendationSettings:LocalAi:";

    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<OllamaRecommender> _logger;

    public OllamaRecommender(IConfiguration config, IHttpClientFactory httpFactory, ILogger<OllamaRecommender> logger)
    {
        _config = config;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public bool IsEnabled => _config.GetValue<bool>(Section + "Enabled") && !string.IsNullOrWhiteSpace(ModelName);

    public string ModelName => _config[Section + "Model"] ?? string.Empty;

    private string BaseUrl => (_config[Section + "BaseUrl"] ?? "http://localhost:11434").TrimEnd('/');

    private int TimeoutSeconds => Math.Clamp(_config.GetValue<int?>(Section + "TimeoutSeconds") ?? 8, 2, 60);

    public async Task<List<AiPick>?> RecommendAsync(string tasteSummary, IReadOnlyList<AiCandidate> candidates, int count, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || candidates.Count == 0 || count < 1)
            return null;

        var prompt = BuildPrompt(tasteSummary, candidates, count);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

            var client = _httpFactory.CreateClient();
            var body = JsonSerializer.Serialize(new
            {
                model = ModelName,
                prompt,
                stream = false,
                format = "json",
                options = new { temperature = 0.2 }
            });

            using var response = await client.PostAsync(
                BaseUrl + "/api/generate",
                new StringContent(body, Encoding.UTF8, "application/json"),
                timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Local AI recommender returned HTTP {Status}; using the rule-based list instead.", (int)response.StatusCode);
                return null;
            }

            using var envelope = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if (!envelope.RootElement.TryGetProperty("response", out var generated) || generated.ValueKind != JsonValueKind.String)
                return null;

            return ParsePicks(generated.GetString() ?? string.Empty, candidates, count);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException)
        {
            // Not running, too slow, or garbled: all of these just mean "use the rule-based list".
            _logger.LogInformation("Local AI recommender unavailable ({Reason}); using the rule-based list instead.", ex.GetType().Name);
            return null;
        }
    }

    private static string BuildPrompt(string tasteSummary, IReadOnlyList<AiCandidate> candidates, int count)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You recommend dishes for a restaurant customer. Choose from the CANDIDATE list only.");
        sb.AppendLine($"Pick up to {count} dishes this customer is most likely to enjoy, best first.");
        sb.AppendLine("Reply with JSON only, in exactly this shape: {\"picks\":[{\"id\":<number>,\"reason\":\"<one short sentence>\"}]}");
        sb.AppendLine("Use only ids from the candidate list. Keep each reason under 15 words and base it on the customer's history or the dish.");
        sb.AppendLine();
        sb.AppendLine("CUSTOMER:");
        sb.AppendLine(tasteSummary);
        sb.AppendLine();
        sb.AppendLine("CANDIDATES:");
        foreach (var c in candidates)
        {
            sb.Append($"- id {c.Id}: {Clean(c.Name)} ({Clean(c.Category)}), {c.Price:0.00}");
            if (c.IsVegetarian) sb.Append(", vegetarian");
            if (c.IsSpicy) sb.Append(", spicy");
            if (c.AverageRating.HasValue) sb.Append($", guests rate it {c.AverageRating.Value:0.0}/5");
            if (!string.IsNullOrWhiteSpace(c.Description)) sb.Append($" - {Clean(c.Description)}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // Dish text is written by administrators, but it still goes into a prompt, so flatten it to one plain line.
    private static string Clean(string text)
    {
        var flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flat.Length > 140 ? flat[..140] : flat;
    }

    private static List<AiPick>? ParsePicks(string json, IReadOnlyList<AiCandidate> candidates, int count)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("picks", out var picks) || picks.ValueKind != JsonValueKind.Array)
            return null;

        var allowed = candidates.Select(c => c.Id).ToHashSet();
        var result = new List<AiPick>();

        foreach (var pick in picks.EnumerateArray())
        {
            if (pick.ValueKind != JsonValueKind.Object || !pick.TryGetProperty("id", out var idElement) || !idElement.TryGetInt32(out var id))
                continue;

            // The safety check: ids the model invented or that were filtered out earlier are simply dropped.
            if (!allowed.Contains(id) || result.Any(r => r.Id == id))
                continue;

            string? reason = null;
            if (pick.TryGetProperty("reason", out var reasonElement) && reasonElement.ValueKind == JsonValueKind.String)
            {
                reason = reasonElement.GetString()?.Trim();
                if (reason != null && reason.Length > 120) reason = reason[..120];
            }

            result.Add(new AiPick(id, string.IsNullOrWhiteSpace(reason) ? null : reason));
            if (result.Count >= count) break;
        }

        return result.Count > 0 ? result : null;
    }
}
