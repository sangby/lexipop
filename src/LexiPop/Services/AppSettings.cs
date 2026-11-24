using System.IO;
using System.Text.Json;

namespace LexiPop.Services;

/// <summary>
/// 本地配置：%LOCALAPPDATA%\LexiPop\settings.json
/// 环境变量 LEXIPOP_API_KEY 优先级高于配置文件。
/// </summary>
public sealed class AppSettings
{
    public string ProviderId { get; set; } = "deepseek";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "deepseek-flash";
    public string Endpoint { get; set; } = "https://api.deepseek.com/chat/completions";
    public string ModelListEndpoint { get; set; } = "https://api.deepseek.com/models";

    /// <summary>划词翻译的全局热键，格式如 "Ctrl+Alt+S"。</summary>
    public string HotkeyTranslate { get; set; } = "Ctrl+Alt+S";

    /// <summary>再次显示上次查询结果的全局热键。</summary>
    public string HotkeyRecall { get; set; } = "Ctrl+Alt+R";

    public bool EnableTranslation { get; set; } = true;

    public bool EnableSelectionPopup { get; set; } = true;

    public string SpeechEnglishVoice { get; set; } = "Microsoft Zira Desktop";

    public string SpeechChineseVoice { get; set; } = "Microsoft Huihui Desktop";

    /// <summary>语速，-10 到 10，0 为正常。</summary>
    public int SpeechRate { get; set; }

    /// <summary>内置主题 id 或 custom- 开头的 CSS 主题 id。</summary>
    public string ThemeId { get; set; } = "light";

    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LexiPop");

    public static string FilePath => Path.Combine(DataDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return ParseJson(File.ReadAllText(FilePath));
            }
        }
        catch
        {
            // 配置损坏时回落到默认值，不阻塞启动
        }

        return new AppSettings();
    }

    internal static AppSettings ParseJson(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

        // 从旧版 DeepSeek 专用字段迁移；保存后只写入通用字段。
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty(nameof(ApiKey), out _) &&
            root.TryGetProperty("DeepSeekApiKey", out var key))
            settings.ApiKey = key.GetString() ?? string.Empty;
        if (!root.TryGetProperty(nameof(Model), out _) &&
            root.TryGetProperty("DeepSeekModel", out var model))
            settings.Model = model.GetString() ?? settings.Model;
        if (!root.TryGetProperty(nameof(Endpoint), out _) &&
            root.TryGetProperty("DeepSeekEndpoint", out var endpoint))
            settings.Endpoint = endpoint.GetString() ?? settings.Endpoint;
        return settings;
    }

    public static bool TrySave(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>环境变量优先，其次配置文件。</summary>
    public string ResolveApiKey()
    {
        var generic = Environment.GetEnvironmentVariable("LEXIPOP_API_KEY");
        if (!string.IsNullOrWhiteSpace(generic)) return generic.Trim();

        // 兼容旧版 DeepSeek 环境变量。
        var fromEnvironment = Environment.GetEnvironmentVariable("LEXIPOP_DEEPSEEK_KEY");
        if (ProviderId == "deepseek" && !string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment.Trim();
        return ApiKey.Trim();
    }
}
