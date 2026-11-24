using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LexiPop.Services;

/// <summary>
/// 通过 DeepSeek Chat Completions 做长句翻译。
/// 显式关闭 thinking 模式，避免推理 token 拖慢速度、抬高成本。
/// </summary>
public sealed class DeepSeekTranslator
{
    private const string DefaultEndpoint = "https://api.deepseek.com/chat/completions";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _endpoint;

    public DeepSeekTranslator(string apiKey, string model, string? endpoint = null)
    {
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? "deepseek-flash" : model;
        _endpoint = string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint : endpoint.Trim();

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            model = _model,
            stream = false,
            thinking = new { type = "disabled" },
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = "你是英译中翻译引擎。只输出中文译文，不要解释、不要加引号、不要保留原文。",
                },
                new { role = "user", content = text },
            },
        };

        var json = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"HTTP {(int)response.StatusCode}: {Truncate(body, 200)}");
        }

        using var document = JsonDocument.Parse(body);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return content?.Trim() ?? string.Empty;
    }

    private static string Truncate(string value, int length)
        => value.Length <= length ? value : value[..length];
}
