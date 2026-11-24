using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LexiPop.Services;

/// <summary>
/// WPF 不直接加载 CSS；读取 :root 中的自定义属性并映射到主题资源。
/// 每个自定义主题是 %LOCALAPPDATA%/LexiPop/themes 下的一份 CSS 文件。
/// </summary>
public static class CustomThemeService
{
    public static string DirectoryPath => Path.Combine(AppSettings.DataDirectory, "themes");

    public static IReadOnlyList<ThemePreset> LoadAll()
    {
        if (!Directory.Exists(DirectoryPath)) return [];

        var themes = new List<ThemePreset>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(DirectoryPath, "custom-*.css"))
            {
                try
                {
                    var id = Path.GetFileNameWithoutExtension(path);
                    if (TryParse(File.ReadAllText(path), id, out var theme, out _))
                        themes.Add(theme!);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return themes.OrderBy(theme => theme.Name, StringComparer.CurrentCulture).ToArray();
    }

    public static string? ReadCss(string id)
    {
        if (!IsCustomId(id)) return null;
        var path = Path.Combine(DirectoryPath, id + ".css");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public static bool TrySave(string css, string? existingId,
        out ThemePreset? theme, out string error)
    {
        var id = existingId is null ? "custom-" + Guid.NewGuid().ToString("N") : existingId;
        theme = null;
        if (!IsCustomId(id))
        {
            error = "只能编辑自定义主题。";
            return false;
        }
        if (!TryParse(css, id, out theme, out error)) return false;

        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var path = Path.Combine(DirectoryPath, id + ".css");
            var temp = path + ".tmp";
            File.WriteAllText(temp, css, Encoding.UTF8);
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "保存主题失败：" + ex.Message;
            return false;
        }
    }

    public static string CreateTemplate(ThemePreset source)
    {
        var css = new StringBuilder();
        css.AppendLine("/* 仅支持 :root 中的变量；颜色使用 #RRGGBB 或 #AARRGGBB。 */");
        css.AppendLine(":root {");
        css.AppendLine("  --name: 我的主题;");
        css.AppendLine($"  --is-dark: {source.IsDark.ToString().ToLowerInvariant()};");
        css.AppendLine($"  --is-glass: {source.IsGlass.ToString().ToLowerInvariant()};");
        Append("background", source.Background);
        Append("surface", source.Surface);
        Append("surface-elevated", source.SurfaceElevated);
        Append("surface-border", source.SurfaceBorder);
        Append("text-primary", source.TextPrimary);
        Append("text-secondary", source.TextSecondary);
        Append("text-muted", source.TextMuted);
        Append("accent", source.Accent);
        Append("accent-hover", source.AccentHover);
        Append("success", source.Success);
        Append("warning", source.Warning);
        Append("error", source.Error);
        Append("status-bar", source.StatusBar);
        Append("glass-surface", source.GlassSurface);
        Append("glass-highlight", source.GlassHighlight);
        Append("glass-border", source.GlassBorder);
        Append("radius-small", source.RadiusSmall.ToString(CultureInfo.InvariantCulture) + "px");
        Append("radius-medium", source.RadiusMedium.ToString(CultureInfo.InvariantCulture) + "px");
        Append("radius-large", source.RadiusLarge.ToString(CultureInfo.InvariantCulture) + "px");
        css.AppendLine("}");
        return css.ToString();

        void Append(string key, string value) => css.AppendLine($"  --{key}: {value};");
    }

    private static bool TryParse(string css, string id,
        out ThemePreset? theme, out string error)
    {
        theme = null;
        error = string.Empty;
        var cleaned = Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);
        var root = Regex.Match(cleaned, @":root\s*\{(?<body>[^}]*)\}", RegexOptions.IgnoreCase);
        if (!root.Success)
        {
            error = "CSS 需要包含 :root { ... }。";
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(root.Groups["body"].Value,
                     @"--(?<key>[a-z][a-z0-9-]*)\s*:\s*(?<value>[^;]+);",
                     RegexOptions.IgnoreCase))
            values[match.Groups["key"].Value] = match.Groups["value"].Value.Trim();

        try
        {
            var name = Value("name");
            if (name.Length > 40 || name.IndexOfAny(['\r', '\n', '{', '}']) >= 0)
                throw new FormatException("主题名称过长或包含无效字符。");

            theme = new ThemePreset(id, name,
                Boolean("is-dark"), Boolean("is-glass"),
                Color("background"), Color("surface"), Color("surface-elevated"),
                Color("surface-border"), Color("text-primary"), Color("text-secondary"),
                Color("text-muted"), Color("accent"), Color("accent-hover"),
                Color("success"), Color("warning"), Color("error"),
                Color("status-bar"), Color("glass-surface"),
                Color("glass-highlight"), Color("glass-border"),
                Radius("radius-small"), Radius("radius-medium"), Radius("radius-large"));
            return true;
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }

        string Value(string key)
        {
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
            throw new FormatException($"缺少 CSS 变量 --{key}。");
        }

        string Color(string key)
        {
            var value = Value(key);
            if (!Regex.IsMatch(value, @"^#(?:[0-9a-f]{6}|[0-9a-f]{8})$", RegexOptions.IgnoreCase))
                throw new FormatException($"--{key} 需要使用 #RRGGBB 或 #AARRGGBB。");
            return value;
        }

        bool Boolean(string key)
        {
            if (bool.TryParse(Value(key), out var value)) return value;
            throw new FormatException($"--{key} 需要是 true 或 false。");
        }

        double Radius(string key)
        {
            var value = Value(key);
            if (!value.EndsWith("px", StringComparison.OrdinalIgnoreCase) ||
                !double.TryParse(value[..^2], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var radius) || radius < 0 || radius > 32)
                throw new FormatException($"--{key} 需要是 0px 到 32px。");
            return radius;
        }
    }

    private static bool IsCustomId(string id) =>
        Regex.IsMatch(id, @"^custom-[0-9a-f]{32}$", RegexOptions.IgnoreCase);
}
