using System.IO;
using System.Text.Json;

namespace LexiPop.Services;

/// <summary>
/// 本地配置：%LOCALAPPDATA%\LexiPop\settings.json
/// 环境变量 LEXIPOP_DEEPSEEK_KEY 优先级高于配置文件，避免密钥写进文件被误提交。
/// </summary>
public sealed class AppSettings
{
    public string DeepSeekApiKey { get; set; } = string.Empty;

    public string DeepSeekModel { get; set; } = "deepseek-flash";

    /// <summary>聊天补全端点，默认 DeepSeek 官方；可换成兼容 OpenAI 格式的任意服务。</summary>
    public string DeepSeekEndpoint { get; set; } = "https://api.deepseek.com/chat/completions";

    /// <summary>唤出窗口的全局热键，格式如 "Ctrl+Alt+D"。</summary>
    public string HotkeyToggle { get; set; } = "Ctrl+Alt+D";

    /// <summary>划词翻译的全局热键，格式如 "Ctrl+Alt+S"。</summary>
    public string HotkeyTranslate { get; set; } = "Ctrl+Alt+S";

    public bool EnableTranslation { get; set; } = true;

    public bool EnableSelectionPopup { get; set; } = true;

    public bool ShowWindowOnStartup { get; set; } = true;

    public string SpeechEnglishVoice { get; set; } = "Microsoft Zira Desktop";

    public string SpeechChineseVoice { get; set; } = "Microsoft Huihui Desktop";

    /// <summary>语速，-10 到 10，0 为正常。</summary>
    public int SpeechRate { get; set; }

    /// <summary>界面主题预设 id：graphite / midnight / amber / violet / light。</summary>
    public string ThemeId { get; set; } = "graphite";

    /// <summary>词库路径；为空表示使用程序目录下的 dict.bin。</summary>
    public string DictionaryPath { get; set; } = string.Empty;

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
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (settings is not null) return settings;
            }
        }
        catch
        {
            // 配置损坏时回落到默认值，不阻塞启动
        }

        return new AppSettings();
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
    public string ResolveDeepSeekKey()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("LEXIPOP_DEEPSEEK_KEY");
        if (!string.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment.Trim();
        return DeepSeekApiKey.Trim();
    }
}
