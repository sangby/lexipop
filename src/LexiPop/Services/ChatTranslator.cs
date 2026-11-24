using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LexiPop.Services;

/// <summary>
/// 通过 OpenAI 兼容的 Chat Completions 接口做长句翻译。
/// </summary>
public sealed class ChatTranslator
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly string _providerId;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _endpoint;

    public ChatTranslator(string providerId, string apiKey, string model, string endpoint)
    {
        _providerId = providerId;
        _apiKey = apiKey;
        _model = model.Trim();
        _endpoint = endpoint.Trim();
    }

    public async Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object>
        {
            ["model"] = _model,
            ["stream"] = false,
            ["messages"] = new[]
            {
                new
                {
                    role = "system",
                    content = "你是英译中翻译引擎。只输出中文译文，不要解释、不要加引号、不要保留原文。",
                },
                new { role = "user", content = text },
            },
        };

        // DeepSeek 专有参数，其他提供方不发送。
        if (_providerId == "deepseek")
            payload["thinking"] = new { type = "disabled" };

        var json = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await Http.SendAsync(request, cancellationToken);
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

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("模型没有返回译文，请换一个支持文本聊天的模型。");
        return content.Trim();
    }

    private static string Truncate(string value, int length)
        => value.Length <= length ? value : value[..length];
}
