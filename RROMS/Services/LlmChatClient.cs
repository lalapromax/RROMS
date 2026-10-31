using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace RROMS.Services;

/// 
/// Talks to a hosted (or local) language model for the website chat assistant.
/// Two wire formats are supported so you can pick any provider:
///   Provider = "Anthropic"  -> Anthropic Messages API (api.anthropic.com)
///   Provider = "OpenAI"     -> any OpenAI-compatible /chat/completions endpoint
///                              (OpenAI, Groq, OpenRouter, Together, or a local Ollama at http://localhost:11434/v1)
///   Provider = "Rules"      -> no model at all (default): the chat uses the built-in FAQ matching only.
/// Nothing here runs until ChatbotSettings has a real key (or a local base URL) - see INTEGRATIONS.md.
/// 
public interface ILlmChatClient
{
    bool IsConfigured { get; }
    string ProviderLabel { get; }

    /// True when the model runs on this machine (a local Ollama), so nothing said to it leaves the server.
    bool IsLocal { get; }

    /// Returns the model's reply, or null when the call failed (the caller then falls back to the FAQ).
    Task<string?> AskAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken = default);

    /// The same with the conversation so far: turns alternate user / assistant and end with the user's new message.
    Task<string?> AskAsync(string systemPrompt, IReadOnlyList<ChatTurn> conversation, CancellationToken cancellationToken = default);
}

public class LlmChatClient : ILlmChatClient
{
    public const string HttpClientName = "llm-chat";

    /// After a call fails to connect, the model is left alone for this long, so every chat message does not wait for it.
    private static readonly TimeSpan RestAfterFailure = TimeSpan.FromSeconds(60);
    private const string DownKey = "llm-chat.down";

    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<LlmChatClient> _logger;
    private readonly IMemoryCache _cache;

    public LlmChatClient(IConfiguration config, IHttpClientFactory httpFactory, ILogger<LlmChatClient> logger, IMemoryCache cache)
    {
        _config = config;
        _httpFactory = httpFactory;
        _logger = logger;
        _cache = cache;
    }

    private string Provider => (_config["ChatbotSettings:Provider"] ?? "Rules").Trim();
    private string ApiKey => _config["ChatbotSettings:ApiKey"] ?? string.Empty;
    private string Model => _config["ChatbotSettings:Model"] ?? string.Empty;
    private int MaxTokens => Math.Clamp(_config.GetValue<int?>("ChatbotSettings:MaxTokens") ?? 300, 50, 1000);
    private int TimeoutSeconds => Math.Clamp(_config.GetValue<int?>("ChatbotSettings:TimeoutSeconds") ?? 20, 3, 60);

    private bool IsAnthropic => Provider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase);
    private bool IsOpenAiStyle => Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase);

    private string OpenAiBaseUrl
    {
        get
        {
            var url = _config["ChatbotSettings:BaseUrl"];
            return (IntegrationSettings.IsReal(url) ? url! : "https://api.openai.com/v1").TrimEnd('/');
        }
    }

    private bool BaseUrlIsLocal =>
        Uri.TryCreate(OpenAiBaseUrl, UriKind.Absolute, out var u) && (u.IsLoopback || u.Host == "host.docker.internal");

    public bool IsConfigured
    {
        get
        {
            if (!(_config.GetValue<bool?>("ChatbotSettings:Enabled") ?? true)) return false;
            if (!IntegrationSettings.IsReal(Model)) return false;
            if (IsAnthropic) return IntegrationSettings.IsReal(ApiKey);
            if (IsOpenAiStyle) return IntegrationSettings.IsReal(ApiKey) || BaseUrlIsLocal; // a local Ollama needs no key
            return false;
        }
    }

    public string ProviderLabel => IsAnthropic ? "Anthropic" : IsOpenAiStyle ? "OpenAI-compatible" : "Rules";

    public bool IsLocal => IsOpenAiStyle && BaseUrlIsLocal;

    public Task<string?> AskAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken = default) =>
        AskAsync(systemPrompt, new[] { new ChatTurn("user", userMessage) }, cancellationToken);

    public async Task<string?> AskAsync(string systemPrompt, IReadOnlyList<ChatTurn> conversation, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || conversation.Count == 0) return null;
        if (_cache.TryGetValue(DownKey, out _)) return null;   // it could not be reached a moment ago

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

            var http = _httpFactory.CreateClient(HttpClientName);
            using var request = IsAnthropic
                ? BuildAnthropicRequest(systemPrompt, conversation)
                : BuildOpenAiRequest(systemPrompt, conversation);

            using var response = await http.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                // The body usually says exactly what is wrong (bad key, unknown model, no credit). Log it, never show it to a diner.
                _logger.LogWarning("Chat model returned {Status}: {Body}", (int)response.StatusCode, Truncate(body, 400));
                return null;
            }

            return IsAnthropic ? ParseAnthropic(body) : ParseOpenAi(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            // Not reachable (no local model running, no network) or too slow: rest it for a minute before trying again.
            if (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
                _cache.Set(DownKey, true, RestAfterFailure);
            _logger.LogWarning(ex, "Chat model call failed; falling back to the built-in answers.");
            return null;
        }
    }

    private static IEnumerable<object> Messages(IReadOnlyList<ChatTurn> conversation) =>
        conversation.Select(t => (object)new { role = t.Role == "assistant" ? "assistant" : "user", content = t.Text });

    private HttpRequestMessage BuildAnthropicRequest(string systemPrompt, IReadOnlyList<ChatTurn> conversation)
    {
        var payload = new
        {
            model = Model,
            max_tokens = MaxTokens,
            system = systemPrompt,
            messages = Messages(conversation)
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        return request;
    }

    private HttpRequestMessage BuildOpenAiRequest(string systemPrompt, IReadOnlyList<ChatTurn> conversation)
    {
        var payload = new
        {
            model = Model,
            max_tokens = MaxTokens,
            messages = new object[] { new { role = "system", content = systemPrompt } }.Concat(Messages(conversation))
        };
        var request = new HttpRequestMessage(HttpMethod.Post, OpenAiBaseUrl + "/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        if (IntegrationSettings.IsReal(ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        return request;
    }

    private static string? ParseAnthropic(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return null;
        var sb = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var type) && type.GetString() == "text" && block.TryGetProperty("text", out var text))
                sb.Append(text.GetString());
        }
        var result = sb.ToString().Trim();
        return result.Length == 0 ? null : result;
    }

    private static string? ParseOpenAi(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            return null;
        var message = choices[0].GetProperty("message");
        var result = message.GetProperty("content").GetString()?.Trim();
        return string.IsNullOrEmpty(result) ? null : result;
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "...";
}
