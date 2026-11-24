using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace LexiPop.Services;

public sealed record ModelProvider(
    string Id, string Name, string ChatEndpoint, string ModelsEndpoint);

/// <summary>OpenAI 兼容的聊天与模型列表端点。</summary>
public static class ModelProviders
{
    public static IReadOnlyList<ModelProvider> All { get; } =
    [
        new("deepseek", "DeepSeek", "https://api.deepseek.com/chat/completions",
            "https://api.deepseek.com/models"),
        new("openai", "OpenAI", "https://api.openai.com/v1/chat/completions",
            "https://api.openai.com/v1/models"),
        new("openrouter", "OpenRouter", "https://openrouter.ai/api/v1/chat/completions",
            "https://openrouter.ai/api/v1/models"),
        new("custom", "自定义兼容服务", string.Empty, string.Empty),
    ];

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static ModelProvider Find(string? id) =>
        All.FirstOrDefault(provider => provider.Id == id) ?? All[0];

    public static bool IsValidEndpoint(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    public static async Task<IReadOnlyList<string>> FetchModelsAsync(
        string endpoint, string? apiKey, CancellationToken cancellationToken = default)
    {
        if (!IsValidEndpoint(endpoint))
            throw new ArgumentException("模型列表网址必须是完整的 HTTP 或 HTTPS 地址。");

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

        using var response = await Http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"获取模型失败：HTTP {(int)response.StatusCode}。");

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("接口未返回 data 模型列表。");

        var models = data.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object &&
                           item.TryGetProperty("id", out var id) &&
                           id.ValueKind == JsonValueKind.String)
            .Select(item => item.GetProperty("id").GetString()!)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (models.Length == 0)
            throw new InvalidOperationException("接口返回了空模型列表。");
        return models;
    }
}
